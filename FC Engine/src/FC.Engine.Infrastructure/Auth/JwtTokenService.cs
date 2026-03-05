using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FC.Engine.Infrastructure.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly IPermissionService _permissionService;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly RSA _signingKey;
    private readonly RsaSecurityKey _validationKey;
    private readonly TokenValidationParameters _tokenValidationParameters;

    public JwtTokenService(
        IOptions<JwtSettings> settings,
        MetadataDbContext db,
        IEntitlementService entitlementService,
        IPermissionService permissionService,
        ILogger<JwtTokenService> logger)
    {
        _settings = settings.Value;
        _db = db;
        _entitlementService = entitlementService;
        _permissionService = permissionService;
        _logger = logger;

        _signingKey = LoadRsaPrivateKey(_settings.SigningKeyPath);
        _validationKey = new RsaSecurityKey(_signingKey.ExportParameters(false));

        _tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _validationKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<TokenResponse> GenerateTokenPair(AuthenticatedUser user)
    {
        var claims = BuildTokenClaims(user);
        var credentials = new SigningCredentials(new RsaSecurityKey(_signingKey), SecurityAlgorithms.RsaSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var refreshTokenString = GenerateSecureRandomString(64);
        var refreshTokenHash = ComputeSha256(refreshTokenString);

        _db.RefreshTokens.Add(new RefreshToken
        {
            TenantId = user.TenantId,
            UserId = user.UserId,
            UserType = user.UserType,
            Token = refreshTokenString,
            TokenHash = refreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenString,
            ExpiresIn = _settings.AccessTokenExpiryMinutes * 60
        };
    }

    public async Task<TokenResponse> RefreshToken(string refreshToken, string? ipAddress)
    {
        var hash = ComputeSha256(refreshToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash && !rt.IsRevoked && !rt.IsUsed);

        if (stored is null || stored.ExpiresAt <= DateTime.UtcNow)
        {
            throw new SecurityTokenException("Invalid or expired refresh token.");
        }

        stored.IsUsed = true;

        var user = await LoadAuthenticatedUser(stored.UserId, stored.UserType, stored.TenantId);
        var newTokens = await GenerateTokenPair(user);

        stored.ReplacedByTokenHash = ComputeSha256(newTokens.RefreshToken);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Refresh token rotated for user {UserId}/{UserType} from IP {IpAddress}",
            stored.UserId,
            stored.UserType,
            ipAddress ?? "unknown");

        return newTokens;
    }

    public async Task RevokeToken(string refreshToken, string? ipAddress)
    {
        var hash = ComputeSha256(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
        if (stored is null)
        {
            return;
        }

        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedByIp = ipAddress;
        await _db.SaveChangesAsync();
    }

    public ClaimsPrincipal ValidateAccessToken(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(accessToken, _tokenValidationParameters, out _);
    }

    private List<Claim> BuildTokenClaims(AuthenticatedUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new("tid", user.TenantId.ToString()),
            new("utype", user.UserType),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new(ClaimTypes.Role, user.Role),
            new("role", user.Role)
        };

        if (user.InstitutionId.HasValue)
        {
            claims.Add(new Claim("iid", user.InstitutionId.Value.ToString()));
        }

        foreach (var permission in user.Permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("perm", permission));
        }

        foreach (var moduleCode in user.EntitledModules.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim("mod", moduleCode));
        }

        return claims;
    }

    private async Task<AuthenticatedUser> LoadAuthenticatedUser(int userId, string userType, Guid tenantId)
    {
        if (string.Equals(userType, "InstitutionUser", StringComparison.OrdinalIgnoreCase))
        {
            var user = await _db.InstitutionUsers
                .Include(x => x.Institution)
                .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId);

            if (user is null || !user.IsActive)
            {
                throw new SecurityTokenException("The user referenced by the refresh token is unavailable.");
            }

            var permissions = await _permissionService.GetPermissions(user.TenantId, user.Role.ToString());
            var entitlement = await _entitlementService.ResolveEntitlements(user.TenantId);

            return new AuthenticatedUser
            {
                UserId = user.Id,
                UserType = "InstitutionUser",
                TenantId = user.TenantId,
                InstitutionId = user.InstitutionId,
                Email = user.Email,
                FullName = user.DisplayName,
                Role = user.Role.ToString(),
                Permissions = permissions.ToList(),
                EntitledModules = entitlement.ActiveModules.Select(m => m.ModuleCode).ToList()
            };
        }

        if (string.Equals(userType, "PortalUser", StringComparison.OrdinalIgnoreCase))
        {
            var user = await _db.PortalUsers.FirstOrDefaultAsync(x => x.Id == userId);
            if (user is null || !user.IsActive || !user.TenantId.HasValue || user.TenantId.Value != tenantId)
            {
                throw new SecurityTokenException("The user referenced by the refresh token is unavailable.");
            }

            var permissions = await _permissionService.GetPermissions(user.TenantId.Value, user.Role.ToString());
            var entitlement = await _entitlementService.ResolveEntitlements(user.TenantId.Value);

            return new AuthenticatedUser
            {
                UserId = user.Id,
                UserType = "PortalUser",
                TenantId = user.TenantId.Value,
                Email = user.Email,
                FullName = user.DisplayName,
                Role = user.Role.ToString(),
                Permissions = permissions.ToList(),
                EntitledModules = entitlement.ActiveModules.Select(m => m.ModuleCode).ToList()
            };
        }

        throw new SecurityTokenException($"Unsupported refresh token user type '{userType}'.");
    }

    private static RSA LoadRsaPrivateKey(string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            throw new InvalidOperationException("Jwt.SigningKeyPath is required.");
        }

        if (!File.Exists(keyPath))
        {
            throw new FileNotFoundException($"JWT signing key file was not found: {keyPath}");
        }

        var pem = File.ReadAllText(keyPath);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static string GenerateSecureRandomString(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
