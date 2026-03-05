using System.Security.Claims;

namespace FC.Engine.Domain.Abstractions;

public interface IJwtTokenService
{
    Task<TokenResponse> GenerateTokenPair(AuthenticatedUser user);
    Task<TokenResponse> RefreshToken(string refreshToken, string? ipAddress);
    Task RevokeToken(string refreshToken, string? ipAddress);
    ClaimsPrincipal ValidateAccessToken(string accessToken);
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string TokenType { get; set; } = "Bearer";
}

public class AuthenticatedUser
{
    public int UserId { get; set; }
    public string UserType { get; set; } = string.Empty; // InstitutionUser | PortalUser | ApiKey
    public Guid TenantId { get; set; }
    public int? InstitutionId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public List<string> EntitledModules { get; set; } = new();
}
