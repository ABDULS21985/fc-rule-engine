using System.Security.Cryptography;
using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace FC.Engine.Application.Services;

/// <summary>
/// Authentication and user management service for Financial Institution Portal users.
/// Uses the same password hashing algorithm as <see cref="AuthService"/>.
/// </summary>
public class InstitutionAuthService
{
    private readonly IInstitutionUserRepository _userRepo;
    private readonly IInstitutionRepository _institutionRepo;
    private readonly ITenantContext _tenantContext;

    // Lockout policy (matches AuthService)
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public InstitutionAuthService(
        IInstitutionUserRepository userRepo,
        IInstitutionRepository institutionRepo,
        ITenantContext tenantContext)
    {
        _userRepo = userRepo;
        _institutionRepo = institutionRepo;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Validates institution user login credentials with lockout logic.
    /// Returns (User, ErrorCode) where ErrorCode is null on success.
    /// </summary>
    public async Task<(InstitutionUser? User, string? ErrorCode)> ValidateLogin(
        string username, string password, CancellationToken ct = default)
    {
        var user = await _userRepo.GetByUsername(username, ct);

        if (user is null)
            return (null, "invalid");

        if (!user.IsActive)
            return (null, "inactive");

        // Check lockout
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
            return (null, "locked");

        // Clear expired lockout
        if (user.LockedUntil.HasValue && user.LockedUntil.Value <= DateTime.UtcNow)
        {
            user.LockedUntil = null;
            user.FailedLoginAttempts = 0;
        }

        // Verify password (same algorithm as AuthService)
        if (!VerifyPassword(password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
            }

            await _userRepo.Update(user, ct);

            return user.FailedLoginAttempts >= MaxFailedAttempts
                ? (null, "locked")
                : (null, "invalid");
        }

        // Success — reset lockout counters
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await _userRepo.Update(user, ct);

        return (user, null);
    }

    /// <summary>
    /// Creates a new institution user with hashed password.
    /// </summary>
    public async Task<InstitutionUser> CreateUser(
        int institutionId,
        string username,
        string email,
        string displayName,
        string password,
        InstitutionRole role,
        CancellationToken ct = default)
    {
        if (await _userRepo.UsernameExists(username, ct))
            throw new InvalidOperationException($"Username '{username}' is already taken.");

        if (await _userRepo.EmailExists(email, ct))
            throw new InvalidOperationException($"Email '{email}' is already registered.");

        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            var institution = await _institutionRepo.GetById(institutionId, ct)
                ?? throw new InvalidOperationException($"Institution {institutionId} not found.");
            tenantId = institution.TenantId;
        }

        var user = new InstitutionUser
        {
            TenantId = tenantId.Value,
            InstitutionId = institutionId,
            Username = username,
            Email = email,
            DisplayName = displayName,
            PasswordHash = HashPassword(password),
            Role = role,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepo.Create(user, ct);
        return user;
    }

    /// <summary>
    /// Changes a user's password (requires knowing the old password).
    /// </summary>
    public async Task<bool> ChangePassword(int userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null) return false;

        if (!VerifyPassword(oldPassword, user.PasswordHash))
            return false;

        user.PasswordHash = HashPassword(newPassword);
        user.MustChangePassword = false;
        await _userRepo.Update(user, ct);
        return true;
    }

    /// <summary>
    /// Resets a user's password (admin-only, no old password required).
    /// </summary>
    public async Task<bool> ResetPassword(int userId, string newPassword, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null) return false;

        user.PasswordHash = HashPassword(newPassword);
        user.MustChangePassword = true;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await _userRepo.Update(user, ct);
        return true;
    }

    /// <summary>
    /// Deactivates a user account.
    /// </summary>
    public async Task<bool> DeactivateUser(int userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null) return false;

        user.IsActive = false;
        await _userRepo.Update(user, ct);
        return true;
    }

    /// <summary>
    /// Records a successful login (timestamp and IP).
    /// </summary>
    public async Task RecordLogin(int userId, string? ipAddress, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null) return;

        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ipAddress;
        await _userRepo.Update(user, ct);
    }

    public ClaimsPrincipal BuildClaimsPrincipal(InstitutionUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("DisplayName", user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("InstitutionId", user.InstitutionId.ToString()),
            new("InstitutionName", user.Institution?.InstitutionName ?? "Unknown"),
            new("TenantId", user.TenantId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "FC.Portal.Auth");
        return new ClaimsPrincipal(identity);
    }

    // ── Password Hashing (identical to AuthService) ──

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
}
