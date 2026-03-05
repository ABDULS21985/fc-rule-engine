using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace FC.Engine.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/login", async (
            LoginRequest request,
            InstitutionAuthService authService,
            IJwtTokenService jwtService,
            IMfaService mfaService,
            IEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            var (success, user, _) = await authService.ValidateCredentials(request.Email, request.Password, ct);
            if (!success || user is null)
            {
                return Results.Unauthorized();
            }

            var hasApiAccess = await entitlementService.HasFeatureAccess(user.TenantId, "api_access", ct);
            if (!hasApiAccess)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var mfaEnabled = await mfaService.IsMfaEnabled(user.UserId, user.UserType);
            var mfaRequired = await mfaService.IsMfaRequired(user.TenantId, user.Role);
            if (mfaEnabled || mfaRequired)
            {
                if (string.IsNullOrWhiteSpace(request.MfaCode) && string.IsNullOrWhiteSpace(request.BackupCode))
                {
                    return Results.Json(new { requiresMfa = true, mfaChallenge = "totp" });
                }

                var verified = false;
                if (!string.IsNullOrWhiteSpace(request.MfaCode))
                {
                    verified = await mfaService.VerifyCode(user.UserId, request.MfaCode, user.UserType);
                }
                else if (!string.IsNullOrWhiteSpace(request.BackupCode))
                {
                    verified = await mfaService.VerifyBackupCode(user.UserId, request.BackupCode, user.UserType);
                }

                if (!verified)
                {
                    return Results.Json(new { error = "Invalid MFA code" }, statusCode: StatusCodes.Status401Unauthorized);
                }
            }

            var tokens = await jwtService.GenerateTokenPair(user);
            return Results.Ok(tokens);
        })
        .AllowAnonymous();

        group.MapPost("/refresh", async (
            RefreshRequest request,
            IJwtTokenService jwtService,
            HttpContext httpContext) =>
        {
            try
            {
                var tokens = await jwtService.RefreshToken(
                    request.RefreshToken,
                    httpContext.Connection.RemoteIpAddress?.ToString());
                return Results.Ok(tokens);
            }
            catch (SecurityTokenException)
            {
                return Results.Unauthorized();
            }
        })
        .AllowAnonymous();

        group.MapPost("/revoke", async (
            RevokeRequest request,
            IJwtTokenService jwtService,
            HttpContext httpContext) =>
        {
            await jwtService.RevokeToken(
                request.RefreshToken,
                httpContext.Connection.RemoteIpAddress?.ToString());
            return Results.Ok(new { revoked = true });
        })
        .RequireAuthorization();
    }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? MfaCode { get; set; }
    public string? BackupCode { get; set; }
}

public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class RevokeRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
