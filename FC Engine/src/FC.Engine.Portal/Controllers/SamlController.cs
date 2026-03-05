using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.MvcCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Portal.Controllers;

[Route("saml")]
public class SamlController : Controller
{
    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly InstitutionAuthService _authService;
    private readonly IAuditLogger _auditLogger;

    public SamlController(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        InstitutionAuthService authService,
        IAuditLogger auditLogger)
    {
        _db = db;
        _entitlementService = entitlementService;
        _authService = authService;
        _auditLogger = auditLogger;
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login([FromQuery] string tenantSlug)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return BadRequest("tenantSlug is required.");
        }

        var ssoConfig = await _db.TenantSsoConfigs
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Tenant!.TenantSlug == tenantSlug && c.SsoEnabled);
        if (ssoConfig is null)
        {
            return BadRequest("SSO not configured for this tenant.");
        }

        var hasSsoAccess = await _entitlementService.HasFeatureAccess(ssoConfig.TenantId, "sso");
        if (!hasSsoAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "SSO requires an Enterprise+ plan.");
        }

        var saml2Configuration = CreateSaml2Configuration(ssoConfig);
        var binding = new Saml2RedirectBinding();
        binding.SetRelayStateQuery(new Dictionary<string, string>
        {
            ["tid"] = ssoConfig.TenantId.ToString()
        });

        return binding.Bind(new Saml2AuthnRequest(saml2Configuration)).ToActionResult();
    }

    [HttpPost("acs")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AssertionConsumerService()
    {
        var relayState = ExtractRelayStateTenantId();
        if (!relayState.HasValue)
        {
            return BadRequest("Missing tenant relay state.");
        }

        var ssoConfig = await _db.TenantSsoConfigs
            .FirstOrDefaultAsync(c => c.TenantId == relayState.Value && c.SsoEnabled);
        if (ssoConfig is null)
        {
            return BadRequest("SSO not configured for this tenant.");
        }

        var hasSsoAccess = await _entitlementService.HasFeatureAccess(ssoConfig.TenantId, "sso");
        if (!hasSsoAccess)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "SSO requires an Enterprise+ plan.");
        }

        var saml2Configuration = CreateSaml2Configuration(ssoConfig);
        var binding = new Saml2PostBinding();
        var saml2AuthnResponse = new Saml2AuthnResponse(saml2Configuration);

        var httpRequest = Request.ToGenericHttpRequest(validate: true);
        binding.ReadSamlResponse(httpRequest, saml2AuthnResponse);
        if (saml2AuthnResponse.Status != Saml2StatusCodes.Success)
        {
            return Unauthorized();
        }

        binding.Unbind(httpRequest, saml2AuthnResponse);
        var identity = saml2AuthnResponse.ClaimsIdentity;
        var claims = identity?.Claims?.ToList() ?? new List<System.Security.Claims.Claim>();

        var mapping = ParseMapping(ssoConfig.AttributeMapping);
        var email = ResolveMappedValue(claims, mapping, "email");
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest("SAML assertion does not include a mapped email attribute.");
        }

        var firstName = ResolveMappedValue(claims, mapping, "firstName");
        var lastName = ResolveMappedValue(claims, mapping, "lastName");
        var samlRole = ResolveMappedValue(claims, mapping, "role");
        var roleName = string.IsNullOrWhiteSpace(samlRole) ? ssoConfig.DefaultRole : samlRole;

        var user = await _db.InstitutionUsers
            .FirstOrDefaultAsync(u => u.TenantId == ssoConfig.TenantId && u.Email == email);

        if (user is null)
        {
            if (!ssoConfig.JitProvisioningEnabled)
            {
                return StatusCode(StatusCodes.Status403Forbidden, "User not provisioned for this tenant.");
            }

            var defaultInstitution = await _db.Institutions
                .Where(i => i.TenantId == ssoConfig.TenantId && i.IsActive)
                .OrderBy(i => i.Id)
                .FirstOrDefaultAsync();

            if (defaultInstitution is null)
            {
                return BadRequest("Tenant has no active institution for SSO provisioning.");
            }

            var displayName = BuildDisplayName(firstName, lastName, email);
            var username = await BuildUniqueUsername(email);
            var parsedRole = ParseInstitutionRole(roleName, ssoConfig.DefaultRole);

            user = new InstitutionUser
            {
                TenantId = ssoConfig.TenantId,
                InstitutionId = defaultInstitution.Id,
                Username = username,
                Email = email,
                DisplayName = displayName,
                PasswordHash = InstitutionAuthService.HashPassword(GenerateTemporaryPassword()),
                Role = parsedRole,
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.InstitutionUsers.Add(user);
            await _db.SaveChangesAsync();

            await _auditLogger.Log(
                "InstitutionUser",
                user.Id,
                "User auto-provisioned via SSO",
                null,
                new { user.Email, user.DisplayName, user.Role },
                "sso");
        }

        var principal = await _authService.BuildClaimsPrincipalWithPermissions(user);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4)
            });

        await _authService.RecordLogin(user.Id, HttpContext.Connection.RemoteIpAddress?.ToString());
        return Redirect("/");
    }

    private static Saml2Configuration CreateSaml2Configuration(TenantSsoConfig config)
    {
        var saml2Config = new Saml2Configuration
        {
            Issuer = config.SpEntityId,
            SingleSignOnDestination = new Uri(config.IdpSsoUrl)
        };

        if (!string.IsNullOrWhiteSpace(config.IdpSloUrl))
        {
            saml2Config.SingleLogoutDestination = new Uri(config.IdpSloUrl);
        }

        saml2Config.AllowedAudienceUris.Add(config.SpEntityId);
        saml2Config.SignatureValidationCertificates.Add(LoadCertificate(config.IdpCertificate));
        return saml2Config;
    }

    private static X509Certificate2 LoadCertificate(string rawCertificate)
    {
        if (rawCertificate.Contains("BEGIN CERTIFICATE", StringComparison.OrdinalIgnoreCase))
        {
            return X509Certificate2.CreateFromPem(rawCertificate);
        }

        var derBytes = Convert.FromBase64String(rawCertificate);
        return new X509Certificate2(derBytes);
    }

    private Guid? ExtractRelayStateTenantId()
    {
        var relayStateRaw = Request.Form["RelayState"].ToString();
        if (string.IsNullOrWhiteSpace(relayStateRaw))
        {
            return null;
        }

        var query = relayStateRaw.StartsWith('?', StringComparison.Ordinal)
            ? relayStateRaw
            : $"?{relayStateRaw}";

        var parsed = QueryHelpers.ParseQuery(query);
        if (!parsed.TryGetValue("tid", out var tidValue))
        {
            return null;
        }

        return Guid.TryParse(tidValue.ToString(), out var tenantId) ? tenantId : null;
    }

    private static Dictionary<string, string> ParseMapping(string rawMapping)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(rawMapping)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ResolveMappedValue(
        IReadOnlyCollection<System.Security.Claims.Claim> claims,
        IReadOnlyDictionary<string, string> mapping,
        string logicalName)
    {
        if (!mapping.TryGetValue(logicalName, out var claimType))
        {
            return null;
        }

        return claims
            .FirstOrDefault(c => string.Equals(c.Type, claimType, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private async Task<string> BuildUniqueUsername(string email)
    {
        var localPart = email.Split('@')[0].Trim();
        var normalized = string.Concat(localPart.Where(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-'));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "sso-user";
        }

        var candidate = normalized;
        var suffix = 0;
        while (await _db.InstitutionUsers.AnyAsync(u => u.Username == candidate))
        {
            suffix++;
            candidate = $"{normalized}{suffix}";
        }

        return candidate;
    }

    private static InstitutionRole ParseInstitutionRole(string role, string defaultRole)
    {
        if (Enum.TryParse<InstitutionRole>(role, ignoreCase: true, out var mapped))
        {
            return mapped;
        }

        if (Enum.TryParse<InstitutionRole>(defaultRole, ignoreCase: true, out mapped))
        {
            return mapped;
        }

        return InstitutionRole.Viewer;
    }

    private static string BuildDisplayName(string? firstName, string? lastName, string email)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? email : fullName;
    }

    private static string GenerateTemporaryPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes);
    }
}
