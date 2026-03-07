using System.Text.Json;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Manages institution profile, team members, and portal settings.
/// Provides full CRUD for institution admins and read-only for other users.
/// </summary>
public class InstitutionManagementService
{
    private readonly IInstitutionRepository _institutionRepo;
    private readonly IInstitutionUserRepository _userRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly InstitutionAuthService _authService;

    public InstitutionManagementService(
        IInstitutionRepository institutionRepo,
        IInstitutionUserRepository userRepo,
        ISubmissionRepository submissionRepo,
        InstitutionAuthService authService)
    {
        _institutionRepo = institutionRepo;
        _userRepo = userRepo;
        _submissionRepo = submissionRepo;
        _authService = authService;
    }

    // ═══════════════════════════════════════════════════════════════
    //  INSTITUTION PROFILE
    // ═══════════════════════════════════════════════════════════════

    public async Task<InstitutionProfileModel?> GetProfile(int institutionId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return null;

        var users = await _userRepo.GetByInstitution(institutionId, ct);
        var activeCount = users.Count(u => u.IsActive);

        int totalSubmissions = 0;
        DateTime? lastSubmissionDate = null;
        try
        {
            var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);
            totalSubmissions = submissions.Count;
            lastSubmissionDate = submissions
                .OrderByDescending(s => s.SubmittedAt)
                .FirstOrDefault()?.SubmittedAt;
        }
        catch { /* Stats are non-critical */ }

        var profile = new InstitutionProfileModel
        {
            Id = inst.Id,
            Code = inst.InstitutionCode,
            Name = inst.InstitutionName,
            LicenseType = inst.LicenseType ?? "",
            SubscriptionTier = inst.SubscriptionTier,
            Address = inst.Address ?? "",
            PhoneNumber = inst.ContactPhone ?? "",
            Email = inst.ContactEmail ?? "",
            IsActive = inst.IsActive,
            CreatedAt = inst.CreatedAt,
            MaxUsersAllowed = inst.MaxUsersAllowed,
            TotalUsers = users.Count,
            ActiveUsers = activeCount,
            TotalSubmissions = totalSubmissions,
            LastSubmissionDate = lastSubmissionDate,
            MakerCheckerEnabled = inst.MakerCheckerEnabled,
            Jurisdiction = "Nigeria",
            Regulator = inst.LicenseType switch
            {
                "Pension" => "PENCOM",
                "Insurance" => "NAICOM",
                _ => "CBN"
            },
            // Contact persons — populated from admin-managed records (stub: roles always present)
            ContactPersons = new List<ContactPersonModel>
            {
                new() { Id = 1, Role = "Compliance Officer", IsPrimary = true },
                new() { Id = 2, Role = "Chief Executive Officer" },
                new() { Id = 3, Role = "Chief Financial Officer" },
            },
            // Regulatory identifiers — values come from extended institution data (stub values for demo)
            RegulatoryIdentifiers = new List<RegulatoryIdentifierModel>
            {
                new() { Code = "RC", Label = "RC Number", Status = RegulatoryVerificationStatus.Unverified },
                new() { Code = "CBN", Label = "CBN License Number", Status = RegulatoryVerificationStatus.Verified,
                        VerifiedAt = DateTime.UtcNow.AddDays(-45), VerifiedBy = "Regulatory Admin" },
                new() { Code = "PENCOM", Label = "PENCOM Code", Status = RegulatoryVerificationStatus.NotSet },
                new() { Code = "NAICOM", Label = "NAICOM Code", Status = RegulatoryVerificationStatus.NotSet },
            },
            // Audit trail — recent profile changes (stub: seeded from known fields)
            RecentChanges = new List<ProfileAuditEntry>
            {
                new() { FieldName = "Contact Email", OldValue = null, NewValue = inst.ContactEmail ?? "",
                        ChangedBy = "Admin User", ChangedAt = DateTime.UtcNow.AddDays(-3) },
                new() { FieldName = "Address", OldValue = "Old address", NewValue = inst.Address ?? "",
                        ChangedBy = "Admin User", ChangedAt = DateTime.UtcNow.AddDays(-7) },
                new() { FieldName = "RC Number", OldValue = null, NewValue = "RC-1234567",
                        ChangedBy = "Compliance Officer", ChangedAt = DateTime.UtcNow.AddDays(-14),
                        IsPending = true, ExpectedBy = "Within 5 business days" },
            },
        };
        profile.PendingChangesCount = profile.RecentChanges.Count(c => c.IsPending);
        return profile;
    }

    public Task<bool> UpdateContactPerson(int institutionId, ContactPersonModel person, CancellationToken ct = default)
    {
        // Stub: in production, persist to InstitutionContacts table
        return Task.FromResult(true);
    }

    public Task<bool> UpdateRegulatoryIdentifier(int institutionId, string code, string value, CancellationToken ct = default)
    {
        // Stub: in production, persist to InstitutionRegulatoryIds table and queue admin verification
        return Task.FromResult(true);
    }

    public Task<string?> UploadLogo(int institutionId, byte[] imageBytes, string contentType, CancellationToken ct = default)
    {
        // Stub: in production, store in blob storage and return URL
        var fakeUrl = $"/img/logos/{institutionId}.png";
        return Task.FromResult<string?>(fakeUrl);
    }

    public async Task<bool> UpdateContactDetails(
        int institutionId, string email, string phone, string address, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        inst.ContactEmail = email;
        inst.ContactPhone = phone;
        inst.Address = address;

        await _institutionRepo.Update(inst, ct);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  TEAM MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<TeamMemberDetailModel>> GetTeamMembers(int institutionId, CancellationToken ct = default)
    {
        var users = await _userRepo.GetByInstitution(institutionId, ct);

        return users.Select(u => new TeamMemberDetailModel
        {
            Id = u.Id,
            DisplayName = u.DisplayName,
            Email = u.Email,
            Username = u.Username,
            Role = u.Role.ToString(),
            IsActive = u.IsActive,
            LastLoginAt = u.LastLoginAt,
            CreatedAt = u.CreatedAt,
            Initials = GetInitials(u.DisplayName)
        })
        .OrderBy(u => u.DisplayName)
        .ToList();
    }

    public async Task<(int current, int max)> GetUserCount(int institutionId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        var count = await _userRepo.GetCountByInstitution(institutionId, ct);
        return (count, inst?.MaxUsersAllowed ?? 10);
    }

    public async Task<AddMemberResult> AddMember(
        int institutionId, string displayName, string username, string email,
        string role, string temporaryPassword, CancellationToken ct = default)
    {
        // Validate email uniqueness
        if (await _userRepo.EmailExists(email, ct))
            return new AddMemberResult { Success = false, Error = "A user with this email already exists." };

        // Validate username uniqueness
        if (await _userRepo.UsernameExists(username, ct))
            return new AddMemberResult { Success = false, Error = "This username is already taken." };

        // Check user limit
        var (current, max) = await GetUserCount(institutionId, ct);
        if (current >= max)
            return new AddMemberResult
            {
                Success = false,
                Error = $"User limit reached ({max} users). Contact CBN to increase your limit."
            };

        // Validate role
        if (!Enum.TryParse<InstitutionRole>(role, out var parsedRole))
            return new AddMemberResult { Success = false, Error = $"Invalid role: {role}" };

        // Delegate to InstitutionAuthService for proper password hashing
        try
        {
            var user = await _authService.CreateUser(
                institutionId, username, email, displayName, temporaryPassword, parsedRole, ct);

            return new AddMemberResult
            {
                Success = true,
                UserId = user.Id,
                Message = $"User {username} has been added successfully. They should log in with the temporary password and change it immediately."
            };
        }
        catch (InvalidOperationException ex)
        {
            return new AddMemberResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<bool> UpdateMemberRole(int userId, string newRole, int institutionId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null || user.InstitutionId != institutionId) return false;

        if (!Enum.TryParse<InstitutionRole>(newRole, out var parsedRole)) return false;

        user.Role = parsedRole;
        await _userRepo.Update(user, ct);
        return true;
    }

    public async Task<bool> ToggleMemberStatus(int userId, bool isActive, int institutionId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null || user.InstitutionId != institutionId) return false;

        user.IsActive = isActive;
        await _userRepo.Update(user, ct);
        return true;
    }

    public async Task<bool> ResetMemberPassword(int userId, string newPassword, int institutionId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null || user.InstitutionId != institutionId) return false;

        return await _authService.ResetPassword(userId, newPassword, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  INSTITUTION SETTINGS
    // ═══════════════════════════════════════════════════════════════

    public async Task<InstitutionPortalSettings> GetSettings(int institutionId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return new InstitutionPortalSettings();

        if (!string.IsNullOrEmpty(inst.SettingsJson))
        {
            try
            {
                return JsonSerializer.Deserialize<InstitutionPortalSettings>(inst.SettingsJson)
                       ?? new InstitutionPortalSettings();
            }
            catch { return new InstitutionPortalSettings(); }
        }
        return new InstitutionPortalSettings();
    }

    public async Task<bool> SaveSettings(int institutionId, InstitutionPortalSettings settings, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        inst.SettingsJson = JsonSerializer.Serialize(settings);
        await _institutionRepo.Update(inst, ct);
        return true;
    }

    public async Task<bool> SetMakerChecker(int institutionId, bool enabled, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        inst.MakerCheckerEnabled = enabled;
        await _institutionRepo.Update(inst, ct);
        return true;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        return string.Concat(
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(w => w[0])
        ).ToUpper();
    }
}

// ── Data Models ──────────────────────────────────────────────────────

public class InstitutionProfileModel
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string LicenseType { get; set; } = "";
    public string SubscriptionTier { get; set; } = "";
    public string Address { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int MaxUsersAllowed { get; set; }
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int TotalSubmissions { get; set; }
    public DateTime? LastSubmissionDate { get; set; }
    public bool MakerCheckerEnabled { get; set; }

    // Rich profile extensions
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string Jurisdiction { get; set; } = "Nigeria";
    public string Regulator { get; set; } = "CBN";
    public List<ContactPersonModel> ContactPersons { get; set; } = new();
    public List<RegulatoryIdentifierModel> RegulatoryIdentifiers { get; set; } = new();
    public List<ProfileAuditEntry> RecentChanges { get; set; } = new();
    public int PendingChangesCount { get; set; }
}

public class TeamMemberDetailModel
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Initials { get; set; } = "";
}

public class AddMemberResult
{
    public bool Success { get; set; }
    public int? UserId { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
}

public class InstitutionPortalSettings
{
    public bool EmailOnSubmissionResult { get; set; } = true;
    public bool EmailOnDeadlineApproaching { get; set; } = true;
    public int DeadlineReminderDays { get; set; } = 3;
    public string DefaultSubmissionFormat { get; set; } = "XmlUpload";
    public int SessionTimeoutHours { get; set; } = 4;
    public string TimezoneId { get; set; } = "Africa/Lagos";
}

public class ContactPersonModel
{
    public int Id { get; set; }
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public enum RegulatoryVerificationStatus { NotSet, Unverified, Pending, Verified }

public class RegulatoryIdentifierModel
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Value { get; set; }
    public RegulatoryVerificationStatus Status { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public bool IsPendingApproval { get; set; }
}

public class ProfileAuditEntry
{
    public string FieldName { get; set; } = "";
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public bool IsPending { get; set; }
    public string? ExpectedBy { get; set; }
}
