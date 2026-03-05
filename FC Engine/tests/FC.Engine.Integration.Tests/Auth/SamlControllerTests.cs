using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Portal.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FC.Engine.Integration.Tests.Auth;

public class SamlControllerTests
{
    [Fact]
    public async Task SSO_Login_Redirects_To_IdP()
    {
        await using var db = CreateDb();
        var tenant = Tenant.Create("SSO Tenant", "sso-tenant", TenantType.Institution);
        tenant.Activate();
        db.Tenants.Add(tenant);
        db.TenantSsoConfigs.Add(new TenantSsoConfig
        {
            TenantId = tenant.TenantId,
            SsoEnabled = true,
            IdpEntityId = "https://idp.example.com/entity",
            IdpSsoUrl = "https://idp.example.com/sso",
            IdpCertificate = TestCertificateBase64,
            SpEntityId = "https://sso-tenant.regos.app/saml",
            AttributeMapping = "{\"email\":\"email\"}"
        });
        await db.SaveChangesAsync();

        var sut = new SamlController(
            db,
            new StubEntitlementService(hasFeatureAccess: true),
            null!,
            null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await sut.Login("sso-tenant");

        result.Should().NotBeNull();
        result.Should().NotBeOfType<BadRequestObjectResult>();
        if (result is ObjectResult objectResult)
        {
            objectResult.StatusCode.Should().NotBe(StatusCodes.Status403Forbidden);
        }
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Url.Should().Contain("idp.example.com");
    }

    [Fact]
    public async Task SSO_Requires_Enterprise_Plan()
    {
        await using var db = CreateDb();
        var tenant = Tenant.Create("SSO Blocked", "sso-blocked", TenantType.Institution);
        tenant.Activate();
        db.Tenants.Add(tenant);
        db.TenantSsoConfigs.Add(new TenantSsoConfig
        {
            TenantId = tenant.TenantId,
            SsoEnabled = true,
            IdpEntityId = "https://idp.example.com/entity",
            IdpSsoUrl = "https://idp.example.com/sso",
            IdpCertificate = TestCertificateBase64,
            SpEntityId = "https://sso-blocked.regos.app/saml",
            AttributeMapping = "{\"email\":\"email\"}"
        });
        await db.SaveChangesAsync();

        var sut = new SamlController(
            db,
            new StubEntitlementService(hasFeatureAccess: false),
            null!,
            null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await sut.Login("sso-blocked");

        var forbidden = result.Should().BeOfType<ObjectResult>().Subject;
        forbidden.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }

    private sealed class StubEntitlementService : IEntitlementService
    {
        private readonly bool _hasFeatureAccess;

        public StubEntitlementService(bool hasFeatureAccess)
        {
            _hasFeatureAccess = hasFeatureAccess;
        }

        public Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult(new TenantEntitlement
            {
                TenantId = tenantId,
                TenantStatus = TenantStatus.Active,
                EligibleModules = Array.Empty<EntitledModule>(),
                ActiveModules = Array.Empty<EntitledModule>(),
                Features = _hasFeatureAccess ? new[] { "sso" } : Array.Empty<string>(),
                PlanCode = _hasFeatureAccess ? "ENTERPRISE" : "STARTER",
                ResolvedAt = DateTime.UtcNow
            });

        public Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
            => Task.FromResult(_hasFeatureAccess && string.Equals(featureCode, "sso", StringComparison.OrdinalIgnoreCase));

        public Task InvalidateCache(Guid tenantId) => Task.CompletedTask;
    }

    // Self-signed test certificate (DER bytes, base64 encoded) used only for parsing in SAML config.
    private const string TestCertificateBase64 =
        "MIIDDzCCAfegAwIBAgIUM+s9VuUG5U3CWr69lqlTDDyfqX8wDQYJKoZIhvcNAQELBQAwFzEVMBMGA1UEAwwMVGVzdCBTQU1MIENBMB4XDTI2MDMwNTEyNTczMVoXDTM2MDMwMjEyNTczMVowFzEVMBMGA1UEAwwMVGVzdCBTQU1MIENBMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAz99dBeCqw0qTT9SM6X2K3lg4meFwSt8pEMZMUUXdjCQDXIq7AYRtThjSkK19pw6p3AUAbwFJqLxq2z/JB3TEj0sev7n+GetdyCw7J+8i5OyL24mkXoWOMGqjKuz7LryKx7snixpnS2u2xd/73qtF9sspf02ng1WH2PJZQ7qo8fAuEOP0eVcuNxGrF0G4x3fyFVq3vqBE82mRN5NjQKvmta39+w9G4Bbhi2CHUFbLUPvj/IDv3hYQ9ZfSVzQbDtEkjagikiAn/Co6pF8fBrMxm8slb2RqU1XanGUVQdmMPB4YEMSE1RkCfT22TSrmLm4JwOjXusZXGj4GtEtX/KpAlwIDAQABo1MwUTAdBgNVHQ4EFgQUWfj5V2DD9lMxfJ/vg/ZuDOv+xmQwHwYDVR0jBBgwFoAUWfj5V2DD9lMxfJ/vg/ZuDOv+xmQwDwYDVR0TAQH/BAUwAwEB/zANBgkqhkiG9w0BAQsFAAOCAQEAq1GqGB9eCWqxjUe9G6LSIqSzISvcxBWzZGk6Tw2BoJchaJHUS014bBzO8p9kIgtqLHXFYoP4dPB8utImmsQezSma8fne4ASjHlmOvNxom2OJQi5NjyO8Stj7WDQTKC87FF3dYPI8jpn6HeIiRUhw+aMlU4gGnaXK82sf6Tg9MzFljNBp3mc6opWw886IJWMEbagrV/Mbi9kFCynKwecQAmMNtBNvqHuI3BWcgCA4SeFseKPJ5iXHAjPuFrzJ+gXqY8VmYqc6HSZVePfhKYgvax7YWo0LYoSsCp/OuNv1X6+HTIzAv82wdIEWIXjmxTR6NHaBUzqUicgyF66LRv1ugA==";
}
