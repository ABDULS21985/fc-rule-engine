using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Security;
using Microsoft.IdentityModel.Tokens;

namespace FC.Engine.Api.Middleware;

public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string TenantHeaderName = "X-Tenant-Id";
    private const string AuthorizationHeaderName = "Authorization";
    private readonly RequestDelegate _next;
    private readonly string? _legacyApiKey;
    private readonly IApiKeyService _apiKeyService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(
        RequestDelegate next,
        IApiKeyService apiKeyService,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _apiKeyService = apiKeyService;
        _jwtTokenService = jwtTokenService;
        _legacyApiKey = configuration["ApiKey"];
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for health, Swagger and auth bootstrap endpoints.
        var path = context.Request.Path.Value ?? "";
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (TryGetBearerToken(context, out var bearerToken))
        {
            try
            {
                var principal = _jwtTokenService.ValidateAccessToken(bearerToken!);
                context.User = principal;

                var tenantClaim = principal.FindFirst("tid")?.Value
                    ?? principal.FindFirst("TenantId")?.Value;

                if (Guid.TryParse(tenantClaim, out var tenantId))
                {
                    context.Items["TenantId"] = tenantId;
                }

                await _next(context);
                return;
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "Bearer token validation failed, falling back to API key path.");
            }
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey) ||
            string.IsNullOrWhiteSpace(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing authentication credentials" });
            return;
        }

        var apiKeyResult = await _apiKeyService.ValidateApiKey(
            extractedApiKey.ToString(),
            context.Connection.RemoteIpAddress?.ToString(),
            context.RequestAborted);

        if (apiKeyResult is not null)
        {
            var claims = new List<Claim>
            {
                new("sub", $"apikey:{apiKeyResult.ApiKeyId}"),
                new("tid", apiKeyResult.TenantId.ToString()),
                new("utype", "ApiKey")
            };

            foreach (var permission in apiKeyResult.Permissions)
            {
                claims.Add(new Claim("perm", permission));
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
            context.Items["ApiKeyValidated"] = true;
            context.Items["TenantId"] = apiKeyResult.TenantId;

            await _next(context);
            return;
        }

        // Backward compatibility path: static key in configuration.
        if (string.IsNullOrWhiteSpace(_legacyApiKey) ||
            !string.Equals(extractedApiKey, _legacyApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
            return;
        }

        context.Items["ApiKeyValidated"] = true;

        if (!context.Request.Headers.TryGetValue(TenantHeaderName, out var tenantHeader) ||
            !Guid.TryParse(tenantHeader, out var legacyTenantId))
        {
            _logger.LogWarning(
                "API request missing required {TenantHeader} header after API key validation",
                TenantHeaderName);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                error = $"Missing or invalid {TenantHeaderName} header"
            });
            return;
        }

        var legacyClaims = new List<Claim>
        {
            new("sub", "apikey:legacy"),
            new("tid", legacyTenantId.ToString()),
            new("utype", "ApiKeyLegacy")
        };

        foreach (var permission in PermissionCatalog.All)
        {
            legacyClaims.Add(new Claim("perm", permission));
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(legacyClaims, "ApiKeyLegacy"));
        context.Items["TenantId"] = legacyTenantId;

        await _next(context);
    }

    private static bool TryGetBearerToken(HttpContext context, out string? token)
    {
        token = null;
        if (!context.Request.Headers.TryGetValue(AuthorizationHeaderName, out var header))
        {
            return false;
        }

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = value["Bearer ".Length..].Trim();
        return !string.IsNullOrWhiteSpace(token);
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ApiKeyMiddleware>();
    }
}
