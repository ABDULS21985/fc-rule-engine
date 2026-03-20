using System.Text.Json;
using System.Text.RegularExpressions;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.Security;
using Microsoft.Extensions.Configuration;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Manages institution profile, team members, and portal settings.
/// Provides full CRUD for institution admins and read-only for other users.
/// </summary>
public class InstitutionManagementService
{
    private static readonly Dictionary<string, string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/svg+xml"] = ".svg"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> InviteCapabilityPermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Submit Returns"] =
            [
                PermissionCatalog.TemplateRead,
                PermissionCatalog.SubmissionRead,
                PermissionCatalog.SubmissionCreate,
                PermissionCatalog.SubmissionEdit,
                PermissionCatalog.SubmissionValidate,
                PermissionCatalog.SubmissionSubmit,
                PermissionCatalog.SubmissionDirectSubmit,
                PermissionCatalog.SubmissionDirectStatus
            ],
            ["Approve Returns"] =
            [
                PermissionCatalog.TemplateRead,
                PermissionCatalog.SubmissionRead,
                PermissionCatalog.SubmissionReview,
                PermissionCatalog.SubmissionApprove,
                PermissionCatalog.SubmissionReject,
                PermissionCatalog.SubmissionDirectStatus
            ],
            ["View Reports"] =
            [
                PermissionCatalog.TemplateRead,
                PermissionCatalog.SubmissionRead,
                PermissionCatalog.ReportRead,
                PermissionCatalog.CalendarRead,
                PermissionCatalog.ComplianceHealthView
            ],
            ["Manage Team"] =
            [
                PermissionCatalog.UserRead,
                PermissionCatalog.UserCreate,
                PermissionCatalog.UserEdit,
                PermissionCatalog.UserDeactivate,
                PermissionCatalog.UserRoleAssign
            ]
        };

    private readonly IInstitutionRepository _institutionRepo;
    private readonly IInstitutionUserRepository _userRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ISubmissionApprovalRepository _approvalRepo;
    private readonly InstitutionAuthService _authService;
    private readonly IEmailSender _emailSender;
    private readonly IFileStorageService _fileStorage;
    private readonly ITenantBrandingService _brandingService;
    private readonly IConfiguration _configuration;

    public InstitutionManagementService(
        IInstitutionRepository institutionRepo,
        IInstitutionUserRepository userRepo,
        ISubmissionRepository submissionRepo,
        ISubmissionApprovalRepository approvalRepo,
        InstitutionAuthService authService,
        IEmailSender emailSender,
        IFileStorageService fileStorage,
        ITenantBrandingService brandingService,
        IConfiguration configuration)
    {
        _institutionRepo = institutionRepo;
        _userRepo = userRepo;
        _submissionRepo = submissionRepo;
        _approvalRepo = approvalRepo;
        _authService = authService;
        _emailSender = emailSender;
        _fileStorage = fileStorage;
        _brandingService = brandingService;
        _configuration = configuration;
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
        catch
        {
            // Submission statistics are non-critical to rendering the profile shell.
        }

        var settings = LoadSettingsEnvelope(inst);
        EnsureProfileDefaults(settings.Profile, inst);

        var profile = new InstitutionProfileModel
        {
            Id = inst.Id,
            Code = inst.InstitutionCode,
            Name = inst.InstitutionName,
            LicenseType = inst.LicenseType ?? string.Empty,
            SubscriptionTier = inst.SubscriptionTier,
            Address = inst.Address ?? string.Empty,
            PhoneNumber = inst.ContactPhone ?? string.Empty,
            Email = inst.ContactEmail ?? string.Empty,
            IsActive = inst.IsActive,
            CreatedAt = inst.CreatedAt,
            MaxUsersAllowed = inst.MaxUsersAllowed,
            TotalUsers = users.Count,
            ActiveUsers = activeCount,
            TotalSubmissions = totalSubmissions,
            LastSubmissionDate = lastSubmissionDate,
            MakerCheckerEnabled = inst.MakerCheckerEnabled,
            LogoUrl = settings.Profile.LogoUrl,
            Jurisdiction = "Nigeria",
            Regulator = ResolveRegulator(inst.LicenseType),
            ContactPersons = settings.Profile.ContactPersons
                .OrderByDescending(x => x.IsPrimary)
                .ThenBy(x => x.Id)
                .Select(Clone)
                .ToList(),
            RegulatoryIdentifiers = settings.Profile.RegulatoryIdentifiers
                .OrderBy(x => x.Label)
                .Select(Clone)
                .ToList(),
            RecentChanges = settings.Profile.RecentChanges
                .OrderByDescending(x => x.ChangedAt)
                .Take(20)
                .Select(Clone)
                .ToList()
        };

        profile.PendingChangesCount = profile.RecentChanges.Count(c => c.IsPending);
        return profile;
    }

    public async Task<bool> UpdateContactPerson(int institutionId, ContactPersonModel person, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        var settings = LoadSettingsEnvelope(inst);
        EnsureProfileDefaults(settings.Profile, inst);

        var contacts = settings.Profile.ContactPersons;
        var existing = contacts.FirstOrDefault(x => x.Id == person.Id)
            ?? contacts.FirstOrDefault(x => x.Role.Equals(person.Role, StringComparison.OrdinalIgnoreCase));

        var previousValue = existing is null ? null : FormatContact(existing);

        if (existing is null)
        {
            existing = new ContactPersonModel
            {
                Id = contacts.Count == 0 ? 1 : contacts.Max(x => x.Id) + 1,
                Role = string.IsNullOrWhiteSpace(person.Role) ? "Other" : person.Role.Trim()
            };
            contacts.Add(existing);
        }

        existing.Role = string.IsNullOrWhiteSpace(person.Role) ? existing.Role : person.Role.Trim();
        existing.Name = person.Name.Trim();
        existing.Email = person.Email.Trim();
        existing.Phone = person.Phone.Trim();
        existing.IsPrimary = person.IsPrimary;

        if (existing.IsPrimary)
        {
            foreach (var contact in contacts.Where(x => x.Id != existing.Id))
            {
                contact.IsPrimary = false;
            }
        }
        else if (!contacts.Any(x => x.IsPrimary))
        {
            existing.IsPrimary = true;
        }

        AddAuditEntry(
            settings.Profile.RecentChanges,
            existing.Role,
            previousValue,
            FormatContact(existing),
            changedBy: "Institution administrator");

        await SaveSettingsEnvelope(inst, settings, ct);
        return true;
    }

    public async Task<bool> UpdateRegulatoryIdentifier(int institutionId, string code, string value, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return false;
        }

        var normalizedValue = value.Trim();
        var settings = LoadSettingsEnvelope(inst);
        EnsureProfileDefaults(settings.Profile, inst);

        var identifiers = settings.Profile.RegulatoryIdentifiers;
        var identifier = identifiers.FirstOrDefault(x => x.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
        var previousValue = identifier?.Value;

        if (identifier is null)
        {
            identifier = new RegulatoryIdentifierModel
            {
                Code = normalizedCode,
                Label = normalizedCode
            };
            identifiers.Add(identifier);
        }

        identifier.Value = string.IsNullOrWhiteSpace(normalizedValue) ? null : normalizedValue;
        identifier.Status = string.IsNullOrWhiteSpace(normalizedValue)
            ? RegulatoryVerificationStatus.NotSet
            : RegulatoryVerificationStatus.Pending;
        identifier.VerifiedAt = null;
        identifier.VerifiedBy = null;
        identifier.IsPendingApproval = !string.IsNullOrWhiteSpace(normalizedValue);

        AddAuditEntry(
            settings.Profile.RecentChanges,
            identifier.Label,
            previousValue,
            identifier.Value ?? "(cleared)",
            changedBy: "Institution administrator",
            isPending: identifier.IsPendingApproval,
            expectedBy: identifier.IsPendingApproval ? "Pending regulator verification" : null);

        await SaveSettingsEnvelope(inst, settings, ct);
        return true;
    }

    public async Task<string?> UploadLogo(int institutionId, byte[] imageBytes, string contentType, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return null;

        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("Please choose an image before saving.");
        }

        if (!AllowedLogoContentTypes.TryGetValue(contentType, out var extension))
        {
            throw new InvalidOperationException("Logo must be PNG, JPEG, or SVG.");
        }

        await using var stream = new MemoryStream(imageBytes, writable: false);
        var path = $"institutions/{inst.TenantId}/profile/logo-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
        var url = await _fileStorage.UploadAsync(path, stream, contentType, ct);

        var settings = LoadSettingsEnvelope(inst);
        EnsureProfileDefaults(settings.Profile, inst);
        var previousValue = settings.Profile.LogoUrl;
        settings.Profile.LogoUrl = url;

        AddAuditEntry(
            settings.Profile.RecentChanges,
            "Institution logo",
            previousValue,
            url,
            changedBy: "Institution administrator");

        await SaveSettingsEnvelope(inst, settings, ct);
        return url;
    }

    public async Task<bool> UpdateContactDetails(
        int institutionId,
        string email,
        string phone,
        string address,
        CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        var settings = LoadSettingsEnvelope(inst);
        EnsureProfileDefaults(settings.Profile, inst);

        if (!string.Equals(inst.ContactEmail, email, StringComparison.OrdinalIgnoreCase))
        {
            AddAuditEntry(settings.Profile.RecentChanges, "Contact email", inst.ContactEmail, email, "Institution administrator");
        }

        if (!string.Equals(inst.ContactPhone, phone, StringComparison.Ordinal))
        {
            AddAuditEntry(settings.Profile.RecentChanges, "Contact phone", inst.ContactPhone, phone, "Institution administrator");
        }

        if (!string.Equals(inst.Address, address, StringComparison.Ordinal))
        {
            AddAuditEntry(settings.Profile.RecentChanges, "Address", inst.Address, address, "Institution administrator");
        }

        inst.ContactEmail = email;
        inst.ContactPhone = phone;
        inst.Address = address;

        inst.SettingsJson = InstitutionSettingsState.Serialize(settings);
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

    public async Task<List<TeamMemberActivityModel>> GetMemberActivity(int institutionId, int userId, CancellationToken ct = default)
    {
        var user = await _userRepo.GetById(userId, ct);
        if (user is null || user.InstitutionId != institutionId)
        {
            return new List<TeamMemberActivityModel>();
        }

        var activity = new List<TeamMemberActivityModel>();

        if (user.LastLoginAt.HasValue)
        {
            activity.Add(new TeamMemberActivityModel
            {
                Type = "login",
                Description = "Logged into the portal",
                OccurredAt = user.LastLoginAt.Value
            });
        }

        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);
        foreach (var submission in submissions
                     .Where(x => x.SubmittedByUserId == userId)
                     .OrderByDescending(x => x.SubmittedAt)
                     .Take(5))
        {
            activity.Add(new TeamMemberActivityModel
            {
                Type = submission.Status is SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings
                    ? "submit"
                    : submission.Status is SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected
                        ? "reject"
                        : "submit",
                Description = BuildSubmissionActivityDescription(submission),
                OccurredAt = submission.SubmittedAt ?? default
            });
        }

        var pendingApprovals = await _approvalRepo.GetPendingByInstitution(institutionId, ct);
        foreach (var approval in pendingApprovals
                     .Where(x => x.RequestedByUserId == userId)
                     .OrderByDescending(x => x.RequestedAt)
                     .Take(3))
        {
            activity.Add(new TeamMemberActivityModel
            {
                Type = "approve",
                Description = $"Submitted {approval.Submission?.ReturnCode ?? "return"} for checker review",
                OccurredAt = approval.RequestedAt
            });
        }

        activity.Add(new TeamMemberActivityModel
        {
            Type = "login",
            Description = "User account provisioned",
            OccurredAt = user.CreatedAt
        });

        return activity
            .OrderByDescending(x => x.OccurredAt)
            .Take(6)
            .ToList();
    }

    public async Task<(int current, int max)> GetUserCount(int institutionId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        var count = await _userRepo.GetCountByInstitution(institutionId, ct);
        return (count, inst?.MaxUsersAllowed ?? 10);
    }

    public async Task<AddMemberResult> AddMember(
        int institutionId,
        string displayName,
        string username,
        string email,
        string role,
        string temporaryPassword,
        CancellationToken ct = default)
    {
        if (await _userRepo.EmailExists(email, ct))
            return new AddMemberResult { Success = false, Error = "A user with this email already exists." };

        if (await _userRepo.UsernameExists(username, ct))
            return new AddMemberResult { Success = false, Error = "This username is already taken." };

        var (current, max) = await GetUserCount(institutionId, ct);
        if (current >= max)
        {
            return new AddMemberResult
            {
                Success = false,
                Error = $"User limit reached ({max} users). Contact your platform administrator to increase the limit."
            };
        }

        if (!Enum.TryParse<InstitutionRole>(role, out var parsedRole))
            return new AddMemberResult { Success = false, Error = $"Invalid role: {role}" };

        try
        {
            var user = await _authService.CreateUser(
                institutionId,
                username,
                email,
                displayName,
                temporaryPassword,
                parsedRole,
                ct);

            return new AddMemberResult
            {
                Success = true,
                UserId = user.Id,
                Message = $"User {username} has been added successfully."
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

        return LoadSettingsEnvelope(inst).PortalSettings;
    }

    public async Task<bool> SaveSettings(int institutionId, InstitutionPortalSettings settings, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        ValidatePortalSettings(settings);

        var envelope = LoadSettingsEnvelope(inst);
        envelope.PortalSettings = settings;
        await SaveSettingsEnvelope(inst, envelope, ct);
        return true;
    }

    private static void ValidatePortalSettings(InstitutionPortalSettings settings)
    {
        if (!new[] { 1, 3, 5, 7 }.Contains(settings.DeadlineReminderDays))
            throw new ArgumentOutOfRangeException(nameof(settings.DeadlineReminderDays),
                "Deadline reminder days must be 1, 3, 5, or 7.");

        if (!new[] { 2, 4, 8 }.Contains(settings.SessionTimeoutHours))
            throw new ArgumentOutOfRangeException(nameof(settings.SessionTimeoutHours),
                "Session timeout must be 2, 4, or 8 hours.");

        if (!new[] { "XmlUpload", "ManualEntry" }.Contains(settings.DefaultSubmissionFormat, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(settings.DefaultSubmissionFormat),
                "Default submission format must be 'XmlUpload' or 'ManualEntry'.");

        if (!new[] { "Africa/Lagos", "UTC", "Europe/London" }.Contains(settings.TimezoneId, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(settings.TimezoneId),
                "Timezone must be one of: Africa/Lagos, UTC, Europe/London.");
    }

    public async Task<bool> SetMakerChecker(int institutionId, bool enabled, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        inst.MakerCheckerEnabled = enabled;
        await _institutionRepo.Update(inst, ct);
        return true;
    }

    // ── Invitation methods ────────────────────────────────────────

    public async Task<bool> SendInvitation(int institutionId, string email, string role, List<string>? grantedPermissions = null, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct)
            ?? throw new InvalidOperationException("Institution was not found.");

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(normalizedEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            throw new InvalidOperationException("Please provide a valid email address.");
        }

        if (!Enum.TryParse<InstitutionRole>(role, true, out var parsedRole))
        {
            throw new InvalidOperationException("Please select a valid role.");
        }

        if (await _userRepo.EmailExists(normalizedEmail, ct))
        {
            throw new InvalidOperationException("A user with this email address already exists.");
        }

        var settings = LoadSettingsEnvelope(inst);
        EnsureProfileDefaults(settings.Profile, inst);

        if (settings.PendingInvitations.Any(x =>
                x.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)
                && x.ExpiresAt > DateTime.UtcNow))
        {
            throw new InvalidOperationException("There is already an active invitation for this email address.");
        }

        var invite = new PendingInviteModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = normalizedEmail,
            Role = parsedRole.ToString(),
            SentAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            HasPermissionOverrides = grantedPermissions is not null,
            GrantedPermissions = ResolveInvitePermissions(parsedRole, grantedPermissions)
        };
        invite.InvitationUrl = BuildInvitationUrl(institutionId, invite.Id);

        settings.PendingInvitations.Add(invite);
        await SaveSettingsEnvelope(inst, settings, ct);
        await SendInvitationEmail(inst, invite, ct);
        return true;
    }

    public async Task<List<PendingInviteModel>> GetPendingInvitations(int institutionId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return new List<PendingInviteModel>();

        var settings = LoadSettingsEnvelope(inst);
        return settings.PendingInvitations
            .OrderByDescending(x => x.SentAt)
            .Select(x =>
            {
                var clone = Clone(x);
                clone.InvitationUrl = BuildInvitationUrl(institutionId, clone.Id);
                return clone;
            })
            .ToList();
    }

    public async Task<bool> ResendInvitation(int institutionId, string inviteId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        var settings = LoadSettingsEnvelope(inst);
        var invite = settings.PendingInvitations.FirstOrDefault(x => x.Id == inviteId);
        if (invite is null) return false;

        invite.SentAt = DateTime.UtcNow;
        invite.ExpiresAt = DateTime.UtcNow.AddDays(7);
        invite.InvitationUrl = BuildInvitationUrl(institutionId, invite.Id);

        await SaveSettingsEnvelope(inst, settings, ct);
        await SendInvitationEmail(inst, invite, ct);
        return true;
    }

    public async Task<bool> RevokeInvitation(int institutionId, string inviteId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return false;

        var settings = LoadSettingsEnvelope(inst);
        var removed = settings.PendingInvitations.RemoveAll(x => x.Id == inviteId);
        if (removed == 0) return false;

        await SaveSettingsEnvelope(inst, settings, ct);
        return true;
    }

    public async Task<PendingInviteModel?> GetInvitation(int institutionId, string inviteId, CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null) return null;

        var settings = LoadSettingsEnvelope(inst);
        var invite = settings.PendingInvitations.FirstOrDefault(x => x.Id == inviteId);
        if (invite is null) return null;

        var clone = Clone(invite);
        clone.InvitationUrl = BuildInvitationUrl(institutionId, clone.Id);
        return clone;
    }

    public async Task<InviteAcceptanceResult> AcceptInvitation(
        int institutionId,
        string inviteId,
        string displayName,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var inst = await _institutionRepo.GetById(institutionId, ct);
        if (inst is null)
        {
            return new InviteAcceptanceResult { Success = false, Error = "Institution not found." };
        }

        var settings = LoadSettingsEnvelope(inst);
        var invite = settings.PendingInvitations.FirstOrDefault(x => x.Id == inviteId);
        if (invite is null)
        {
            return new InviteAcceptanceResult { Success = false, Error = "Invitation not found or already used." };
        }

        if (invite.ExpiresAt <= DateTime.UtcNow)
        {
            return new InviteAcceptanceResult { Success = false, Error = "This invitation has expired." };
        }

        var normalizedUsername = username.Trim();
        var normalizedDisplayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || string.IsNullOrWhiteSpace(normalizedUsername))
        {
            return new InviteAcceptanceResult { Success = false, Error = "Display name and username are required." };
        }

        if (await _userRepo.EmailExists(invite.Email, ct))
        {
            settings.PendingInvitations.RemoveAll(x => x.Id == invite.Id);
            await SaveSettingsEnvelope(inst, settings, ct);
            return new InviteAcceptanceResult { Success = false, Error = "This invitation has already been used." };
        }

        if (await _userRepo.UsernameExists(normalizedUsername, ct))
        {
            return new InviteAcceptanceResult { Success = false, Error = "That username is already taken." };
        }

        var parsedRole = Enum.TryParse<InstitutionRole>(invite.Role, true, out var role)
            ? role
            : InstitutionRole.Maker;

        try
        {
            var user = await _authService.CreateUser(
                institutionId,
                normalizedUsername,
                invite.Email,
                normalizedDisplayName,
                password,
                parsedRole,
                invite.HasPermissionOverrides ? invite.GrantedPermissions : null,
                ct);

            user.MustChangePassword = false;
            await _userRepo.Update(user, ct);

            settings.PendingInvitations.RemoveAll(x => x.Id == invite.Id);
            await SaveSettingsEnvelope(inst, settings, ct);

            return new InviteAcceptanceResult
            {
                Success = true,
                Username = normalizedUsername,
                LoginUrl = "/login"
            };
        }
        catch (Exception ex)
        {
            return new InviteAcceptanceResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private InstitutionSettingsEnvelope LoadSettingsEnvelope(Institution institution)
        => InstitutionSettingsState.Deserialize(institution.SettingsJson);

    private async Task SaveSettingsEnvelope(Institution institution, InstitutionSettingsEnvelope envelope, CancellationToken ct)
    {
        institution.SettingsJson = InstitutionSettingsState.Serialize(envelope);
        await _institutionRepo.Update(institution, ct);
    }

    private static void EnsureProfileDefaults(InstitutionProfileState profile, Institution institution)
    {
        profile.ContactPersons ??= new List<ContactPersonModel>();
        profile.RegulatoryIdentifiers ??= new List<RegulatoryIdentifierModel>();
        profile.RecentChanges ??= new List<ProfileAuditEntry>();

        if (profile.ContactPersons.Count == 0)
        {
            profile.ContactPersons =
            [
                new ContactPersonModel
                {
                    Id = 1,
                    Role = "Compliance Officer",
                    Email = institution.ContactEmail ?? string.Empty,
                    Phone = institution.ContactPhone ?? string.Empty,
                    IsPrimary = true
                },
                new ContactPersonModel
                {
                    Id = 2,
                    Role = "Chief Executive Officer"
                },
                new ContactPersonModel
                {
                    Id = 3,
                    Role = "Chief Financial Officer"
                }
            ];
        }

        if (profile.RegulatoryIdentifiers.Count == 0)
        {
            profile.RegulatoryIdentifiers =
            [
                new RegulatoryIdentifierModel { Code = "RC", Label = "RC Number", Status = RegulatoryVerificationStatus.NotSet },
                new RegulatoryIdentifierModel { Code = "CBN", Label = "CBN License Number", Status = RegulatoryVerificationStatus.NotSet },
                new RegulatoryIdentifierModel { Code = "PENCOM", Label = "PENCOM Code", Status = RegulatoryVerificationStatus.NotSet },
                new RegulatoryIdentifierModel { Code = "NAICOM", Label = "NAICOM Code", Status = RegulatoryVerificationStatus.NotSet }
            ];
        }
    }

    private async Task SendInvitationEmail(Institution institution, PendingInviteModel invite, CancellationToken ct)
    {
        var branding = await _brandingService.GetBrandingConfig(institution.TenantId, ct);
        var recipientName = invite.Email.Split('@')[0];
        var inviteUrl = invite.InvitationUrl ?? BuildInvitationUrl(institution.Id, invite.Id);

        await _emailSender.SendTemplatedAsync(
            NotificationEvents.UserInvited,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Title"] = "You have been invited to RegOS",
                ["Message"] = $"{institution.InstitutionName} invited you to join RegOS as {invite.Role}.",
                ["RecipientName"] = recipientName,
                ["InvitedBy"] = institution.InstitutionName,
                ["CompanyName"] = institution.InstitutionName,
                ["Role"] = invite.Role,
                ["SetupUrl"] = inviteUrl
            },
            invite.Email,
            recipientName,
            branding,
            institution.TenantId,
            ct);
    }

    private string BuildInvitationUrl(int institutionId, string inviteId)
    {
        var baseUrl = (_configuration["RegOS:PortalBaseUrl"]
                       ?? _configuration["RegOS:BaseUrl"]
                       ?? "http://localhost:5003").TrimEnd('/');
        return $"{baseUrl}/institution/invite/{institutionId}/{Uri.EscapeDataString(inviteId)}";
    }

    private static string BuildSubmissionActivityDescription(Submission submission)
    {
        var periodLabel = submission.ReturnPeriod is null
            ? "current period"
            : new DateTime(submission.ReturnPeriod.Year, submission.ReturnPeriod.Month, 1).ToString("MMM yyyy");

        return submission.Status switch
        {
            SubmissionStatus.PendingApproval => $"Submitted {submission.ReturnCode} for {periodLabel} and routed it for checker review",
            SubmissionStatus.ApprovalRejected => $"Received checker feedback on {submission.ReturnCode} for {periodLabel}",
            SubmissionStatus.Rejected => $"Submitted {submission.ReturnCode} for {periodLabel} but validation returned errors",
            SubmissionStatus.AcceptedWithWarnings => $"Submitted {submission.ReturnCode} for {periodLabel} with validation warnings",
            _ => $"Submitted {submission.ReturnCode} for {periodLabel}"
        };
    }

    private static void AddAuditEntry(
        List<ProfileAuditEntry> entries,
        string fieldName,
        string? oldValue,
        string? newValue,
        string changedBy,
        bool isPending = false,
        string? expectedBy = null)
    {
        entries.Insert(0, new ProfileAuditEntry
        {
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue ?? string.Empty,
            ChangedBy = changedBy,
            ChangedAt = DateTime.UtcNow,
            IsPending = isPending,
            ExpectedBy = expectedBy
        });

        if (entries.Count > 25)
        {
            entries.RemoveRange(25, entries.Count - 25);
        }
    }

    private static string ResolveRegulator(string? licenseType) => licenseType switch
    {
        "Pension" => "PENCOM",
        "Insurance" => "NAICOM",
        "Broker" or "Dealer" => "SEC",
        _ => "CBN"
    };

    private static string FormatContact(ContactPersonModel contact)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(contact.Name)) parts.Add(contact.Name);
        if (!string.IsNullOrWhiteSpace(contact.Email)) parts.Add(contact.Email);
        if (!string.IsNullOrWhiteSpace(contact.Phone)) parts.Add(contact.Phone);
        return string.Join(" · ", parts);
    }

    private static ContactPersonModel Clone(ContactPersonModel contact) => new()
    {
        Id = contact.Id,
        Role = contact.Role,
        Name = contact.Name,
        Email = contact.Email,
        Phone = contact.Phone,
        IsPrimary = contact.IsPrimary
    };

    private static RegulatoryIdentifierModel Clone(RegulatoryIdentifierModel identifier) => new()
    {
        Code = identifier.Code,
        Label = identifier.Label,
        Value = identifier.Value,
        Status = identifier.Status,
        VerifiedAt = identifier.VerifiedAt,
        VerifiedBy = identifier.VerifiedBy,
        IsPendingApproval = identifier.IsPendingApproval
    };

    private static ProfileAuditEntry Clone(ProfileAuditEntry entry) => new()
    {
        FieldName = entry.FieldName,
        OldValue = entry.OldValue,
        NewValue = entry.NewValue,
        ChangedBy = entry.ChangedBy,
        ChangedAt = entry.ChangedAt,
        IsPending = entry.IsPending,
        ExpectedBy = entry.ExpectedBy
    };

    private static PendingInviteModel Clone(PendingInviteModel invite) => new()
    {
        Id = invite.Id,
        Email = invite.Email,
        Role = invite.Role,
        SentAt = invite.SentAt,
        ExpiresAt = invite.ExpiresAt,
        InvitationUrl = invite.InvitationUrl,
        HasPermissionOverrides = invite.HasPermissionOverrides,
        GrantedPermissions = new List<string>(invite.GrantedPermissions)
    };

    private static List<string> ResolveInvitePermissions(InstitutionRole role, IEnumerable<string>? grantedPermissions)
    {
        if (grantedPermissions is null)
        {
            return [];
        }

        var selectedCapabilities = grantedPermissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resolved = PermissionCatalog.DefaultRolePermissions.TryGetValue(role.ToString(), out var defaults)
            ? defaults.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var capability in InviteCapabilityPermissions)
        {
            if (selectedCapabilities.Contains(capability.Key))
            {
                resolved.UnionWith(capability.Value);
            }
            else
            {
                resolved.ExceptWith(capability.Value);
            }
        }

        return resolved
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        return string.Concat(
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Take(2)
                .Select(w => w[0])
        ).ToUpperInvariant();
    }
}

// ── Data Models ──────────────────────────────────────────────────────

public class PendingInviteModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Maker";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public string? InvitationUrl { get; set; }
    public bool HasPermissionOverrides { get; set; }
    /// <summary>Resolved permission codes selected during invite.</summary>
    public List<string> GrantedPermissions { get; set; } = new();
}

public class InviteAcceptanceResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Username { get; set; }
    public string? LoginUrl { get; set; }
}

public class TeamMemberActivityModel
{
    public string Type { get; set; } = "login";
    public string Description { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

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
