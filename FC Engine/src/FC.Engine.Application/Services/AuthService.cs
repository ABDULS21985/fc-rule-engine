using System.Security.Cryptography;
using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace FC.Engine.Application.Services;

public class AuthService
{
    private readonly IPortalUserRepository _userRepo;
    private readonly ILoginAttemptRepository _loginAttemptRepo;
    private readonly IPasswordResetTokenRepository _resetTokenRepo;
    private readonly IEntitlementService _entitlementService;
    private readonly IPermissionService _permissionService;

    // Lockout policy
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(30);

    // Reset token validity
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    public AuthService(
        IPortalUserRepository userRepo,
        ILoginAttemptRepository loginAttemptRepo,
        IPasswordResetTokenRepository resetTokenRepo,
        IEntitlementService entitlementService,
        IPermissionService permissionService)
    {
        _userRepo = userRepo;
        _loginAttemptRepo = loginAttemptRepo;
        _resetTokenRepo = resetTokenRepo;
        _entitlementService = entitlementService;
        _permissionService = permissionService;
    }

    /// <summary>
    /// Validates login credentials with lockout enforcement.
    /// Returns (User, ErrorCode) where ErrorCode is null on success.
    /// </summary>
    public async Task<(PortalUser? User, string? ErrorCode)> ValidateLogin(
        string username, string password, string? ipAddress = null, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByUsername(username, ct);

        // User not found — record attempt but don't reveal user doesn't exist
        if (user is null)
        {
            await RecordAttempt(username, ipAddress, false, "user_not_found", ct);
            return (null, "invalid");
        }

        // Account inactive
        if (!user.IsActive)
        {
            await RecordAttempt(username, ipAddress, false, "inactive", ct);
            return (null, "denied");
        }

        // Check lockout
        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
        {
            await RecordAttempt(username, ipAddress, false, "locked", ct);
            return (null, "locked");
        }

        // Verify password
        if (!VerifyPassword(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
            }

            await _userRepo.Update(user, ct);
            await RecordAttempt(username, ipAddress, false, "bad_password", ct);

            return user.FailedLoginAttempts >= MaxFailedAttempts
                ? (null, "locked")
                : (null, "invalid");
        }

        // Success — reset lockout counters
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTime.UtcNow;
        await _userRepo.Update(user, ct);
        await RecordAttempt(username, ipAddress, true, null, ct);

        return (user, null);
    }

    /// <summary>
    /// Generates a password reset token for the given email address.
    /// Returns the token string, or null if the email is not found.
    /// </summary>
    public async Task<string?> GeneratePasswordResetToken(string email, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByEmail(email, ct);
        if (user is null || !user.IsActive)
            return null;

        // Invalidate any existing tokens for this user
        await _resetTokenRepo.InvalidateAllForUser(user.Id, ct);

        var token = GenerateSecureToken();
        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.Add(ResetTokenLifetime),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        await _resetTokenRepo.Create(resetToken, ct);
        return token;
    }

    /// <summary>
    /// Validates a reset token without consuming it.
    /// Returns the associated user email if valid.
    /// </summary>
    public async Task<string?> ValidateResetToken(string token, CancellationToken ct = default)
    {
        var resetToken = await _resetTokenRepo.GetByToken(token, ct);
        if (resetToken is null || resetToken.IsUsed || resetToken.ExpiresAt <= DateTime.UtcNow)
            return null;

        return resetToken.User.Email;
    }

    /// <summary>
    /// Resets the password using a valid token.
    /// Returns true if the reset succeeded.
    /// </summary>
    public async Task<bool> ResetPasswordWithToken(string token, string newPassword, CancellationToken ct = default)
    {
        var resetToken = await _resetTokenRepo.GetByToken(token, ct);
        if (resetToken is null || resetToken.IsUsed || resetToken.ExpiresAt <= DateTime.UtcNow)
            return false;

        // Change password
        var user = resetToken.User;
        user.PasswordHash = HashPassword(newPassword);
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await _userRepo.Update(user, ct);

        // Mark token as used
        resetToken.IsUsed = true;
        resetToken.UsedAt = DateTime.UtcNow;
        await _resetTokenRepo.Update(resetToken, ct);

        return true;
    }

    public async Task<PortalUser> CreateUser(
        string username, string displayName, string email,
        string password, PortalRole role, CancellationToken ct = default)
    {
        if (await _userRepo.UsernameExists(username, ct))
            throw new InvalidOperationException($"Username '{username}' already exists");

        var user = new PortalUser
        {
            Username = username,
            DisplayName = displayName,
            Email = email,
            PasswordHash = HashPassword(password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        return await _userRepo.Create(user, ct);
    }

    public async Task ChangePassword(int userId, string newPassword, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct)
            ?? throw new InvalidOperationException("User not found");

        user.PasswordHash = HashPassword(newPassword);
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await _userRepo.Update(user, ct);
    }

    public ClaimsPrincipal BuildClaimsPrincipal(PortalUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("DisplayName", user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        if (user.TenantId.HasValue)
        {
            claims.Add(new Claim("TenantId", user.TenantId.Value.ToString()));
        }
        else
        {
            claims.Add(new Claim("IsPlatformAdmin", "true"));
            claims.Add(new Claim(ClaimTypes.Role, "PlatformAdmin"));
        }

        var identity = new ClaimsIdentity(claims, "FC.Admin.Auth");
        return new ClaimsPrincipal(identity);
    }

    public async Task<ClaimsPrincipal> BuildClaimsPrincipalWithPermissions(PortalUser user, CancellationToken ct = default)
    {
        var principal = BuildClaimsPrincipal(user);
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null)
        {
            return principal;
        }

        var roleName = user.TenantId.HasValue ? user.Role.ToString() : "PlatformAdmin";
        var permissions = await _permissionService.GetPermissions(user.TenantId, roleName, ct);
        foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            identity.AddClaim(new Claim("perm", permission));
        }

        if (user.TenantId.HasValue)
        {
            var entitlement = await _entitlementService.ResolveEntitlements(user.TenantId.Value, ct);
            foreach (var module in entitlement.ActiveModules.Select(x => x.ModuleCode).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                identity.AddClaim(new Claim("mod", module));
            }
        }

        return principal;
    }

    public async Task<AuthenticatedUser> BuildAuthenticatedUser(PortalUser user, CancellationToken ct = default)
    {
        if (!user.TenantId.HasValue)
        {
            return new AuthenticatedUser
            {
                UserId = user.Id,
                UserType = "PortalUser",
                TenantId = Guid.Empty,
                Email = user.Email,
                FullName = user.DisplayName,
                Role = "PlatformAdmin",
                Permissions = (await _permissionService.GetPermissions(null, "PlatformAdmin", ct)).ToList(),
                EntitledModules = new List<string>()
            };
        }

        var permissions = await _permissionService.GetPermissions(user.TenantId, user.Role.ToString(), ct);
        var entitlement = await _entitlementService.ResolveEntitlements(user.TenantId.Value, ct);

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

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100_000,
            numBytesRequested: 32);

        // Store as salt:hash in base64
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);

        var actualHash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: 100_000,
            numBytesRequested: 32);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private async Task RecordAttempt(string username, string? ipAddress, bool succeeded, string? reason, CancellationToken ct)
    {
        await _loginAttemptRepo.Create(new LoginAttempt
        {
            Username = username,
            IpAddress = ipAddress,
            Succeeded = succeeded,
            FailureReason = reason,
            AttemptedAt = DateTime.UtcNow
        }, ct);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
