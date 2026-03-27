using System.Collections.ObjectModel;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OtpNet;

namespace FC.Engine.Migrator;

public sealed class DemoCredentialSeedService
{
    private const string PlatformLoginUrl = "http://localhost:5200/login";
    private const string RegulatorLoginUrl = "http://localhost:5200/login";
    private const string InstitutionLoginUrl = "http://localhost:5300/login";

    private static readonly DemoPortalUserSpec[] PlatformUsers =
    [
        new("admin", "System Administrator", "admin@fcengine.local", PortalRole.Admin, RequiresMfa: false),
        new("platformapprover", "Platform Approver", "platform.approver@fcengine.local", PortalRole.Approver, RequiresMfa: false),
        new("platformviewer", "Platform Viewer", "platform.viewer@fcengine.local", PortalRole.Viewer, RequiresMfa: false)
    ];

    private static readonly DemoPortalUserSpec[] RegulatorUsers =
    [
        new("cbnadmin", "CBN Demo Admin", "cbn.admin@fcengine.local", PortalRole.Admin, RequiresMfa: false),
        new("cbnapprover", "CBN Demo Approver", "cbn.approver@fcengine.local", PortalRole.Approver, RequiresMfa: true),
        new("cbnviewer", "CBN Demo Viewer", "cbn.viewer@fcengine.local", PortalRole.Viewer, RequiresMfa: false)
    ];

    private static readonly DemoInstitutionUserSpec[] BdcUsers =
    [
        new("cashcodeadmin", "Cashcode Admin", "admin@cashcode.local", InstitutionRole.Admin, RequiresMfa: false),
        new("cashcodemaker", "Cashcode Maker", "maker@cashcode.local", InstitutionRole.Maker, RequiresMfa: false),
        new("cashcodechecker", "Cashcode Checker", "checker@cashcode.local", InstitutionRole.Checker, RequiresMfa: true),
        new("cashcodeviewer", "Cashcode Viewer", "viewer@cashcode.local", InstitutionRole.Viewer, RequiresMfa: false),
        new("cashcodeapprover", "Cashcode Approver", "approver@cashcode.local", InstitutionRole.Approver, RequiresMfa: true)
    ];

    private static readonly DemoInstitutionUserSpec[] DmbUsers =
    [
        new("accessdemo", "Access Demo Admin", "accessdemo@accessbank.local", InstitutionRole.Admin, RequiresMfa: false),
        new("accessmaker", "Access Demo Maker", "maker@accessbank.local", InstitutionRole.Maker, RequiresMfa: false),
        new("accesschecker", "Access Demo Checker", "checker@accessbank.local", InstitutionRole.Checker, RequiresMfa: true),
        new("accessviewer", "Access Demo Viewer", "viewer@accessbank.local", InstitutionRole.Viewer, RequiresMfa: false),
        new("accessapprover", "Access Demo Approver", "approver@accessbank.local", InstitutionRole.Approver, RequiresMfa: true)
    ];

    private readonly MetadataDbContext _db;
    private readonly AuthService _authService;
    private readonly InstitutionAuthService _institutionAuthService;
    private readonly IPortalUserRepository _portalUserRepository;
    private readonly IInstitutionUserRepository _institutionUserRepository;
    private readonly IMfaService _mfaService;
    private readonly ILogger<DemoCredentialSeedService> _logger;

    public DemoCredentialSeedService(
        MetadataDbContext db,
        AuthService authService,
        InstitutionAuthService institutionAuthService,
        IPortalUserRepository portalUserRepository,
        IInstitutionUserRepository institutionUserRepository,
        IMfaService mfaService,
        ILogger<DemoCredentialSeedService> logger)
    {
        _db = db;
        _authService = authService;
        _institutionAuthService = institutionAuthService;
        _portalUserRepository = portalUserRepository;
        _institutionUserRepository = institutionUserRepository;
        _mfaService = mfaService;
        _logger = logger;
    }

    public async Task<DemoCredentialSeedResult> SeedAsync(string sharedPassword, CancellationToken ct = default)
    {
        var result = new DemoCredentialSeedResult
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            SharedPassword = sharedPassword
        };

        foreach (var spec in PlatformUsers)
        {
            result.PlatformAccounts.Add(await EnsurePortalUserAsync(
                spec,
                sharedPassword,
                tenantId: null,
                audience: "Platform",
                loginUrl: PlatformLoginUrl,
                ct));
        }

        var regulatorTenant = await ResolveTenantAsync("cbn", "Central Bank of Nigeria", ct);
        result.RegulatorGroups.Add(await EnsurePortalTenantUsersAsync(
            regulatorTenant,
            "Regulator workspace demo with policy simulation, stress testing, surveillance, and examination workflows.",
            RegulatorUsers,
            sharedPassword,
            ct));

        var bdcInstitution = await ResolveInstitutionAsync("CASHCODE", "CASHCODE BDC LTD", ct);
        result.InstitutionGroups.Add(await EnsureInstitutionUsersAsync(
            bdcInstitution,
            "BDC demo tenant with live BDC_CBN templates and samples.",
            BdcUsers,
            sharedPassword,
            ct));

        var dmbInstitution = await ResolveInstitutionAsync("ACCESSBA", "Access Bank Plc", ct);
        result.InstitutionGroups.Add(await EnsureInstitutionUsersAsync(
            dmbInstitution,
            "DMB demo tenant with seeded DMB_BASEL3 history and zero-warning DMB_OPR sample.",
            DmbUsers,
            sharedPassword,
            ct));

        _logger.LogInformation(
            "Seeded demo credential matrix: {PlatformCount} platform accounts, {InstitutionCount} institution accounts",
            result.PlatformAccounts.Count,
            result.InstitutionGroups.Sum(x => x.Accounts.Count));

        return result;
    }

    private async Task<DemoCredentialAccount> EnsurePortalUserAsync(
        DemoPortalUserSpec spec,
        string password,
        Guid? tenantId,
        string audience,
        string loginUrl,
        CancellationToken ct)
    {
        var user = await _portalUserRepository.GetByUsername(spec.Username, ct);
        if (user is null)
        {
            user = await _authService.CreateUser(
                spec.Username,
                spec.DisplayName,
                spec.Email,
                password,
                spec.Role,
                tenantId,
                ct);
        }
        else
        {
            await _authService.ChangePassword(user.Id, password, ct);
            user = await _portalUserRepository.GetByUsername(spec.Username, ct)
                   ?? throw new InvalidOperationException($"Portal user {spec.Username} disappeared after password reset.");
        }

        user.TenantId = tenantId;
        user.DisplayName = spec.DisplayName;
        user.Email = spec.Email;
        user.Role = spec.Role;
        user.IsActive = true;
        user.DeletedAt = null;
        user.DeletionReason = null;
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        await _portalUserRepository.Update(user, ct);

        var mfa = await ConfigureMfaAsync(user.Id, "PortalUser", user.Email, spec.RequiresMfa, ct);

        return new DemoCredentialAccount
        {
            Audience = audience,
            LoginUrl = loginUrl,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            Password = password,
            MfaRequired = spec.RequiresMfa,
            TotpSecret = mfa?.TotpSecret,
            BackupCodes = mfa?.BackupCodes ?? []
        };
    }

    private async Task<DemoPortalGroup> EnsurePortalTenantUsersAsync(
        Tenant tenant,
        string notes,
        IReadOnlyList<DemoPortalUserSpec> specs,
        string password,
        CancellationToken ct)
    {
        var group = new DemoPortalGroup
        {
            Audience = "Regulator",
            LoginUrl = RegulatorLoginUrl,
            TenantName = tenant.TenantName,
            TenantSlug = tenant.TenantSlug,
            Notes = notes
        };

        foreach (var spec in specs)
        {
            group.Accounts.Add(await EnsurePortalUserAsync(
                spec,
                password,
                tenant.TenantId,
                tenant.TenantSlug.ToUpperInvariant(),
                RegulatorLoginUrl,
                ct));
        }

        return group;
    }

    private async Task<DemoCredentialGroup> EnsureInstitutionUsersAsync(
        Institution institution,
        string notes,
        IReadOnlyList<DemoInstitutionUserSpec> specs,
        string password,
        CancellationToken ct)
    {
        var group = new DemoCredentialGroup
        {
            Audience = institution.LicenseType ?? "Institution",
            LoginUrl = InstitutionLoginUrl,
            InstitutionCode = institution.InstitutionCode,
            InstitutionName = institution.InstitutionName,
            LicenseType = institution.LicenseType ?? "Unknown",
            Notes = notes
        };

        foreach (var spec in specs)
        {
            group.Accounts.Add(await EnsureInstitutionUserAsync(institution, spec, password, ct));
        }

        return group;
    }

    private async Task<DemoCredentialAccount> EnsureInstitutionUserAsync(
        Institution institution,
        DemoInstitutionUserSpec spec,
        string password,
        CancellationToken ct)
    {
        var user = await _institutionUserRepository.GetByUsername(spec.Username, ct);
        if (user is null)
        {
            user = await _institutionAuthService.CreateUser(
                institution.Id,
                spec.Username,
                spec.Email,
                spec.DisplayName,
                password,
                spec.Role,
                ct);
        }
        else
        {
            if (user.InstitutionId != institution.Id)
            {
                throw new InvalidOperationException(
                    $"Institution user {spec.Username} already belongs to institution {user.InstitutionId}, expected {institution.Id}.");
            }

            await _institutionAuthService.ResetPassword(user.Id, password, ct);
            user = await _institutionUserRepository.GetById(user.Id, ct)
                   ?? throw new InvalidOperationException($"Institution user {spec.Username} disappeared after password reset.");
        }

        user.TenantId = institution.TenantId;
        user.InstitutionId = institution.Id;
        user.DisplayName = spec.DisplayName;
        user.Email = spec.Email;
        user.Role = spec.Role;
        user.PermissionOverridesJson = null;
        user.IsActive = true;
        user.MustChangePassword = false;
        user.PreferredLanguage = "en";
        user.DeletedAt = null;
        user.DeletionReason = null;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await _institutionUserRepository.Update(user, ct);

        var mfa = await ConfigureMfaAsync(user.Id, "InstitutionUser", user.Email, spec.RequiresMfa, ct);

        return new DemoCredentialAccount
        {
            Audience = institution.InstitutionCode,
            LoginUrl = InstitutionLoginUrl,
            InstitutionCode = institution.InstitutionCode,
            InstitutionName = institution.InstitutionName,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            Password = password,
            MfaRequired = spec.RequiresMfa,
            TotpSecret = mfa?.TotpSecret,
            BackupCodes = mfa?.BackupCodes ?? []
        };
    }

    private async Task<DemoMfaMaterial?> ConfigureMfaAsync(
        int userId,
        string userType,
        string email,
        bool required,
        CancellationToken ct)
    {
        if (!required)
        {
            await _mfaService.Disable(userId, userType);
            return null;
        }

        var setup = await _mfaService.InitiateSetup(userId, userType, email);
        var code = new Totp(Base32Encoding.ToBytes(setup.SecretKey)).ComputeTotp(DateTime.UtcNow);
        var activation = await _mfaService.ActivateWithVerification(userId, userType, code);
        if (!activation.Success)
        {
            throw new InvalidOperationException($"Unable to activate MFA for {userType}:{userId}.");
        }

        return new DemoMfaMaterial(setup.SecretKey, new ReadOnlyCollection<string>(activation.BackupCodes));
    }

    private async Task<Institution> ResolveInstitutionAsync(string institutionCode, string institutionName, CancellationToken ct)
    {
        var institution = await _db.Institutions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.InstitutionCode == institutionCode || x.InstitutionName == institutionName,
                ct);

        return institution ?? throw new InvalidOperationException(
            $"Institution {institutionCode} / {institutionName} was not found.");
    }

    private async Task<Tenant> ResolveTenantAsync(string tenantSlug, string tenantName, CancellationToken ct)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantSlug == tenantSlug || x.TenantName == tenantName,
                ct);

        return tenant ?? throw new InvalidOperationException(
            $"Tenant {tenantSlug} / {tenantName} was not found.");
    }
}

public sealed class DemoCredentialSeedResult
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string SharedPassword { get; set; } = string.Empty;
    public List<DemoCredentialAccount> PlatformAccounts { get; } = [];
    public List<DemoPortalGroup> RegulatorGroups { get; } = [];
    public List<DemoCredentialGroup> InstitutionGroups { get; } = [];
}

public sealed class DemoPortalGroup
{
    public string Audience { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<DemoCredentialAccount> Accounts { get; } = [];
}

public sealed class DemoCredentialGroup
{
    public string Audience { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<DemoCredentialAccount> Accounts { get; } = [];
}

public sealed class DemoCredentialAccount
{
    public string Audience { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool MfaRequired { get; set; }
    public string? TotpSecret { get; set; }
    public IReadOnlyList<string> BackupCodes { get; set; } = [];
}

sealed record DemoPortalUserSpec(
    string Username,
    string DisplayName,
    string Email,
    PortalRole Role,
    bool RequiresMfa);

sealed record DemoInstitutionUserSpec(
    string Username,
    string DisplayName,
    string Email,
    InstitutionRole Role,
    bool RequiresMfa);

sealed record DemoMfaMaterial(
    string TotpSecret,
    IReadOnlyList<string> BackupCodes);
