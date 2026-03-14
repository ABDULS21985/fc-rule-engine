using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

internal sealed class AdminRegulatorIqTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FC.Admin.RegIq.TestAuth";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantHeader = "X-Test-TenantId";
    public const string RegulatorCodeHeader = "X-Test-RegulatorCode";

    public AdminRegulatorIqTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = userValues.ToString().Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId),
            new(ClaimTypes.Email, $"{userId}@regos.test")
        };

        if (Request.Headers.TryGetValue(TenantHeader, out var tenantValues))
        {
            var tenantId = tenantValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                claims.Add(new Claim("TenantId", tenantId));
                claims.Add(new Claim("tid", tenantId));
            }
        }

        if (Request.Headers.TryGetValue(RegulatorCodeHeader, out var regulatorValues))
        {
            var regulatorCode = regulatorValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(regulatorCode))
            {
                claims.Add(new Claim("RegulatorCode", regulatorCode.ToUpperInvariant()));
            }
        }

        if (Request.Headers.TryGetValue(RolesHeader, out var roleValues))
        {
            foreach (var role in roleValues
                         .ToString()
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(role => !string.IsNullOrWhiteSpace(role)))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
