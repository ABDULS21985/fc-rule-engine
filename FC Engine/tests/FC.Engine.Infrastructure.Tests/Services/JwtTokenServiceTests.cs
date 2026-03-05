using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace FC.Engine.Infrastructure.Tests.Services;

public class JwtTokenServiceTests : IDisposable
{
    private readonly MetadataDbContext _db;
    private readonly Mock<IEntitlementService> _entitlement = new();
    private readonly Mock<IPermissionService> _permissions = new();
    private readonly string _pemPath;
    private readonly JwtTokenService _sut;

    public JwtTokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new MetadataDbContext(options);

        _pemPath = Path.Combine(Path.GetTempPath(), $"jwt-test-{Guid.NewGuid():N}.pem");
        using (var rsa = System.Security.Cryptography.RSA.Create(2048))
        {
            File.WriteAllText(_pemPath, rsa.ExportRSAPrivateKeyPem());
        }

        _permissions
            .Setup(x => x.GetPermissions(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "submission.create", "report.read" });

        _entitlement
            .Setup(x => x.ResolveEntitlements(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntitlement
            {
                TenantId = Guid.NewGuid(),
                TenantStatus = TenantStatus.Active,
                ActiveModules = new[]
                {
                    new EntitledModule { ModuleCode = "FC_RETURNS", ModuleName = "FC Returns", IsActive = true }
                }
            });

        _sut = new JwtTokenService(
            Options.Create(new JwtSettings
            {
                Issuer = "https://api.regos.app",
                Audience = "regos-api",
                AccessTokenExpiryMinutes = 15,
                RefreshTokenExpiryDays = 7,
                SigningKeyPath = _pemPath
            }),
            _db,
            _entitlement.Object,
            _permissions.Object,
            NullLogger<JwtTokenService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_pemPath))
        {
            File.Delete(_pemPath);
        }
    }

    [Fact]
    public async Task JWT_Login_Returns_AccessToken_And_RefreshToken()
    {
        var tenantId = Guid.NewGuid();
        var response = await _sut.GenerateTokenPair(new AuthenticatedUser
        {
            UserId = 77,
            UserType = "InstitutionUser",
            TenantId = tenantId,
            InstitutionId = 11,
            Email = "tester@tenant.local",
            FullName = "Token Tester",
            Role = "Maker",
            Permissions = new List<string> { "submission.create" },
            EntitledModules = new List<string> { "FC_RETURNS" }
        });

        response.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.RefreshToken.Should().NotBeNullOrWhiteSpace();
        response.ExpiresIn.Should().Be(900);
        response.TokenType.Should().Be("Bearer");

        var principal = _sut.ValidateAccessToken(response.AccessToken);
        GetClaimValue(principal, "tid", "http://schemas.microsoft.com/identity/claims/tenantid")
            .Should().Be(tenantId.ToString());
        GetClaimValue(principal, "perm").Should().Be("submission.create");
        GetClaimValue(principal, "mod").Should().Be("FC_RETURNS");
    }

    [Fact]
    public async Task JWT_AccessToken_Expires_After_15_Minutes()
    {
        var response = await _sut.GenerateTokenPair(new AuthenticatedUser
        {
            UserId = 12,
            UserType = "InstitutionUser",
            TenantId = Guid.NewGuid(),
            Email = "expiry@test.local",
            FullName = "Expiry User",
            Role = "Maker",
            Permissions = new List<string>(),
            EntitledModules = new List<string>()
        });

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        token.ValidTo.Should().BeAfter(DateTime.UtcNow.AddMinutes(14));
        token.ValidTo.Should().BeBefore(DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public async Task Refresh_Token_Single_Use_Rotation()
    {
        var tenant = Tenant.Create("Token Tenant", "token-tenant", TenantType.Institution, "token@test.local");
        tenant.Activate();
        _db.Tenants.Add(tenant);
        _db.Institutions.Add(new Institution
        {
            Id = 10,
            TenantId = tenant.TenantId,
            InstitutionCode = "TOK",
            InstitutionName = "Token Institution",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        _db.InstitutionUsers.Add(new InstitutionUser
        {
            Id = 44,
            TenantId = tenant.TenantId,
            InstitutionId = 10,
            Username = "token.user",
            Email = "token.user@test.local",
            DisplayName = "Token User",
            PasswordHash = "ignored",
            Role = InstitutionRole.Maker,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        var first = await _sut.GenerateTokenPair(new AuthenticatedUser
        {
            UserId = 44,
            UserType = "InstitutionUser",
            TenantId = tenant.TenantId,
            InstitutionId = 10,
            Email = "token.user@test.local",
            FullName = "Token User",
            Role = "Maker",
            Permissions = new List<string> { "submission.create" },
            EntitledModules = new List<string> { "FC_RETURNS" }
        });

        var rotated = await _sut.RefreshToken(first.RefreshToken, "127.0.0.1");
        rotated.RefreshToken.Should().NotBe(first.RefreshToken);
        rotated.AccessToken.Should().NotBe(first.AccessToken);

        Func<Task> replay = async () => await _sut.RefreshToken(first.RefreshToken, "127.0.0.1");
        await replay.Should().ThrowAsync<SecurityTokenException>();
    }

    [Fact]
    public async Task Expired_Refresh_Token_Rejected()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = Guid.NewGuid(),
            UserId = 999,
            UserType = "InstitutionUser",
            Token = "expired-refresh-token",
            TokenHash = ComputeSha256("expired-refresh-token"),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
            IsRevoked = false,
            IsUsed = false
        });
        await _db.SaveChangesAsync();

        Func<Task> act = async () => await _sut.RefreshToken("expired-refresh-token", "127.0.0.1");
        await act.Should().ThrowAsync<SecurityTokenException>();
    }

    [Fact]
    public async Task Revoked_Refresh_Token_Rejected()
    {
        _db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = Guid.NewGuid(),
            UserId = 999,
            UserType = "InstitutionUser",
            Token = "revoked-refresh-token",
            TokenHash = ComputeSha256("revoked-refresh-token"),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = true,
            IsUsed = false
        });
        await _db.SaveChangesAsync();

        Func<Task> act = async () => await _sut.RefreshToken("revoked-refresh-token", "127.0.0.1");
        await act.Should().ThrowAsync<SecurityTokenException>();
    }

    private static string GetClaimValue(System.Security.Claims.ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var claim = principal.FindFirst(claimType);
            if (claim is not null)
            {
                return claim.Value;
            }
        }

        throw new InvalidOperationException($"None of the expected claims were present: {string.Join(", ", claimTypes)}");
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
