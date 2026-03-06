using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Portal.Services;

public class OnboardingWizardService
{
    private const decimal VatRate = 0.075m;
    private readonly MetadataDbContext _db;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ITenantBrandingService _brandingService;
    private readonly InstitutionAuthService _institutionAuthService;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly IAuditLogger? _auditLogger;
    private readonly ILogger<OnboardingWizardService> _logger;

    public OnboardingWizardService(
        MetadataDbContext db,
        ITenantOnboardingService tenantOnboardingService,
        ISubscriptionService subscriptionService,
        ITenantBrandingService brandingService,
        InstitutionAuthService institutionAuthService,
        ILogger<OnboardingWizardService> logger,
        INotificationOrchestrator? notificationOrchestrator = null,
        IAuditLogger? auditLogger = null)
    {
        _db = db;
        _tenantOnboardingService = tenantOnboardingService;
        _subscriptionService = subscriptionService;
        _brandingService = brandingService;
        _institutionAuthService = institutionAuthService;
        _logger = logger;
        _notificationOrchestrator = notificationOrchestrator;
        _auditLogger = auditLogger;
    }

    public async Task<OnboardingLookupData> GetLookupData(CancellationToken ct = default)
    {
        var licences = await _db.LicenceTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new OnboardingLicenceTypeOption
            {
                Code = x.Code,
                Name = x.Name,
                Regulator = x.Regulator,
                Description = x.Description ?? string.Empty
            })
            .ToListAsync(ct);

        var plans = await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .Select(x => new OnboardingPlanOption
            {
                PlanCode = x.PlanCode,
                PlanName = x.PlanName,
                Description = x.Description,
                BasePriceMonthly = x.BasePriceMonthly,
                MaxModules = x.MaxModules,
                MaxEntities = x.MaxEntities,
                MaxUsersPerEntity = x.MaxUsersPerEntity,
                Features = x.Features
            })
            .ToListAsync(ct);

        return new OnboardingLookupData
        {
            LicenceTypes = licences,
            Plans = plans
        };
    }

    public async Task<IReadOnlyList<OnboardingModuleOption>> GetEligibleModules(
        IReadOnlyCollection<string> licenceTypeCodes,
        string? planCode,
        CancellationToken ct = default)
    {
        var normalizedCodes = licenceTypeCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedCodes.Count == 0)
        {
            return Array.Empty<OnboardingModuleOption>();
        }

        var plan = await ResolvePlan(planCode, ct);
        if (plan is null)
        {
            return Array.Empty<OnboardingModuleOption>();
        }

        var matrixRows = await _db.LicenceModuleMatrix
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Include(x => x.Module)
            .Where(x => x.LicenceType != null
                        && x.Module != null
                        && x.LicenceType.IsActive
                        && x.Module.IsActive
                        && normalizedCodes.Contains(x.LicenceType.Code))
            .ToListAsync(ct);

        var moduleIds = matrixRows
            .Select(x => x.ModuleId)
            .Distinct()
            .ToList();

        var planPricing = await _db.PlanModulePricing
            .AsNoTracking()
            .Where(x => x.PlanId == plan.Id && moduleIds.Contains(x.ModuleId))
            .ToDictionaryAsync(x => x.ModuleId, x => x, ct);

        var options = matrixRows
            .GroupBy(x => x.ModuleId)
            .Select(group =>
            {
                var module = group.First().Module!;
                var pricing = planPricing.TryGetValue(module.Id, out var row) ? row : null;

                return new OnboardingModuleOption
                {
                    ModuleCode = module.ModuleCode,
                    ModuleName = module.ModuleName,
                    RegulatorCode = module.RegulatorCode,
                    Description = module.Description,
                    IsRequired = group.Any(x => x.IsRequired),
                    IsOptional = group.Any(x => x.IsOptional),
                    IsAvailableOnPlan = pricing is not null,
                    IsIncludedInBase = pricing?.IsIncludedInBase ?? false,
                    PriceMonthly = pricing is null || pricing.IsIncludedInBase ? 0m : pricing.PriceMonthly
                };
            })
            .OrderBy(x => x.ModuleCode)
            .ToList();

        return options;
    }

    public async Task<OnboardingCostEstimate> EstimateMonthlyCost(
        string? planCode,
        IReadOnlyCollection<string> licenceTypeCodes,
        IReadOnlyCollection<string> selectedModuleCodes,
        CancellationToken ct = default)
    {
        var plan = await ResolvePlan(planCode, ct);
        if (plan is null)
        {
            return new OnboardingCostEstimate();
        }

        var moduleOptions = await GetEligibleModules(licenceTypeCodes, plan.PlanCode, ct);
        var selected = selectedModuleCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var picked = moduleOptions
            .Where(x => x.IsRequired || selected.Contains(x.ModuleCode))
            .Select(x => new OnboardingCostLine
            {
                Code = x.ModuleCode,
                Label = x.ModuleName,
                Amount = x.PriceMonthly,
                IncludedInBase = x.IsIncludedInBase
            })
            .ToList();

        var moduleSubtotal = picked.Sum(x => x.Amount);
        var subtotal = plan.BasePriceMonthly + moduleSubtotal;
        var vat = decimal.Round(subtotal * VatRate, 2);
        var total = subtotal + vat;

        return new OnboardingCostEstimate
        {
            PlanCode = plan.PlanCode,
            PlanName = plan.PlanName,
            BasePriceMonthly = plan.BasePriceMonthly,
            ModuleLines = picked,
            Subtotal = subtotal,
            VatAmount = vat,
            Total = total
        };
    }

    public async Task<OnboardingProvisionResult> CompleteOnboarding(
        OnboardingWizardRequest request,
        CancellationToken ct = default)
    {
        var result = new OnboardingProvisionResult();
        var timer = Stopwatch.StartNew();

        try
        {
            ValidateRequest(request);

            var selectedPlan = await ResolvePlan(request.PlanCode, ct);
            if (selectedPlan is null)
            {
                result.Errors.Add($"Unknown plan code '{request.PlanCode}'.");
                return result;
            }

            var firstLicenceCode = request.LicenceTypeCodes
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
            if (firstLicenceCode is null)
            {
                result.Errors.Add("At least one licence type must be selected.");
                return result;
            }

            var institutionCode = BuildInstitutionCode(request.Profile.InstitutionName);
            var onboardingRequest = new TenantOnboardingRequest
            {
                TenantName = request.Profile.InstitutionName.Trim(),
                TenantSlug = string.IsNullOrWhiteSpace(request.Profile.TenantSlug)
                    ? null
                    : request.Profile.TenantSlug.Trim(),
                TenantType = TenantType.Institution,
                ContactEmail = request.Profile.ContactEmail.Trim(),
                ContactPhone = request.Profile.ContactPhone?.Trim(),
                Address = request.Profile.Address?.Trim(),
                RcNumber = request.Profile.RcNumber?.Trim(),
                TaxId = request.Profile.Tin?.Trim(),
                LicenceTypeCodes = request.LicenceTypeCodes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SubscriptionPlanCode = selectedPlan.PlanCode,
                AdminEmail = request.AdminUser.Email.Trim(),
                AdminFullName = request.AdminUser.FullName.Trim(),
                AdminPhone = request.AdminUser.Phone?.Trim(),
                InstitutionCode = institutionCode,
                InstitutionName = request.Profile.InstitutionName.Trim(),
                InstitutionType = firstLicenceCode,
                JurisdictionCode = "NG"
            };

            var onboarded = await _tenantOnboardingService.OnboardTenant(onboardingRequest, ct);
            if (!onboarded.Success)
            {
                result.Errors.AddRange(onboarded.Errors);
                return result;
            }

            result.TenantId = onboarded.TenantId;
            result.TenantSlug = onboarded.TenantSlug;
            result.InstitutionId = onboarded.InstitutionId;
            result.AdminTemporaryPassword = onboarded.AdminTemporaryPassword;

            await ActivateSelectedModules(onboarded.TenantId, request, result, ct);
            await ApplyEntityStructure(onboarded.TenantId, onboarded.InstitutionId, request, ct);
            var invitedUserIds = await CreateAdditionalUsers(onboarded.InstitutionId, request, result, ct);
            await ApplyWorkflowConfiguration(onboarded.InstitutionId, request, ct);
            await ApplyBranding(onboarded.TenantId, request, ct);
            await EnsurePeriodsForActivatedModules(onboarded.TenantId, ct);
            await NotifyInvitedUsers(onboarded.TenantId, invitedUserIds, ct);

            if (_auditLogger is not null)
            {
                await _auditLogger.Log(
                    entityType: "Tenant",
                    entityId: 0,
                    action: "ONBOARDING_COMPLETED",
                    oldValues: null,
                    newValues: new
                    {
                        onboarded.TenantId,
                        request.PlanCode,
                        request.LicenceTypeCodes,
                        Modules = result.ActivatedModules
                    },
                    performedBy: request.AdminUser.Email,
                    ct: ct);
            }

            timer.Stop();
            result.Success = true;
            result.ProvisioningDurationMs = timer.ElapsedMilliseconds;
            result.CompletedInUnderSixtySeconds = timer.Elapsed < TimeSpan.FromSeconds(60);
            if (!result.CompletedInUnderSixtySeconds)
            {
                result.Warnings.Add($"Provisioning completed in {timer.Elapsed.TotalSeconds:F1}s.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Onboarding wizard provisioning failed.");
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public async Task<OnboardingChecklistSnapshot?> GetChecklist(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);

        if (tenant is null)
        {
            return null;
        }

        var institution = await _db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var branding = tenant.GetBrandingConfig();
        var usersCount = await _db.InstitutionUsers
            .AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.IsActive, ct);

        var hasSubmission = await _db.Submissions
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId, ct);

        var hasNotificationsConfig = await _db.NotificationPreferences
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId, ct);

        var hasCalendar = await _db.ReturnPeriods
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId, ct);

        var hasGuidedTourAudit = await _db.AuditLog
            .AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Action == "GUIDED_TOUR_COMPLETED", ct);

        var items = new List<OnboardingChecklistItemState>
        {
            new()
            {
                Code = "upload_logo",
                Title = "Upload logo",
                Description = "Upload your institution logo for portal branding.",
                ActionLabel = "Open Branding",
                ActionUrl = "/settings/branding",
                IsComplete = !string.IsNullOrWhiteSpace(branding.LogoUrl)
            },
            new()
            {
                Code = "configure_branding",
                Title = "Configure branding",
                Description = "Set colours and company profile branding.",
                ActionLabel = "Configure",
                ActionUrl = "/settings/branding",
                IsComplete = !string.IsNullOrWhiteSpace(tenant.BrandingConfig)
            },
            new()
            {
                Code = "first_return",
                Title = "Create first return (dry run)",
                Description = "Run one submission through validation.",
                ActionLabel = "Start Return",
                ActionUrl = "/submit",
                IsComplete = hasSubmission
            },
            new()
            {
                Code = "complete_profile",
                Title = "Complete profile",
                Description = "Fill institution contact, RC and TIN details.",
                ActionLabel = "Open Profile",
                ActionUrl = "/institution",
                IsComplete = institution is not null
                    && !string.IsNullOrWhiteSpace(institution.ContactEmail)
                    && !string.IsNullOrWhiteSpace(institution.ContactPhone)
                    && !string.IsNullOrWhiteSpace(tenant.RcNumber)
                    && !string.IsNullOrWhiteSpace(tenant.TaxId)
            },
            new()
            {
                Code = "notification_preferences",
                Title = "Set notification preferences",
                Description = "Choose reminder channels and escalation behaviour.",
                ActionLabel = "Configure Notifications",
                ActionUrl = "/settings/notifications",
                IsComplete = hasNotificationsConfig
            },
            new()
            {
                Code = "review_calendar",
                Title = "Review filing calendar",
                Description = "Check period deadlines and module schedules.",
                ActionLabel = "Open Calendar",
                ActionUrl = "/calendar",
                IsComplete = hasCalendar
            },
            new()
            {
                Code = "invite_team",
                Title = "Invite team members",
                Description = "Add Maker, Checker, and Approver users.",
                ActionLabel = "Manage Team",
                ActionUrl = "/institution/team",
                IsComplete = usersCount > 1
            },
            new()
            {
                Code = "guided_tour",
                Title = "Complete guided tour",
                Description = "Walk through dashboard and submission flow.",
                ActionLabel = "Start Tour",
                ActionUrl = "/onboarding/tour/maker",
                IsComplete = hasGuidedTourAudit
            }
        };

        var completionPercent = items.Count == 0
            ? 0
            : (int)Math.Round(items.Count(x => x.IsComplete) * 100m / items.Count, MidpointRounding.AwayFromZero);

        return new OnboardingChecklistSnapshot
        {
            TenantId = tenant.TenantId,
            TenantName = tenant.TenantName,
            CompletionPercent = completionPercent,
            Items = items
        };
    }

    public async Task MarkGuidedTourCompleted(
        Guid tenantId,
        string role,
        string performedBy,
        CancellationToken ct = default)
    {
        if (_auditLogger is null)
        {
            return;
        }

        await _auditLogger.Log(
            entityType: "Onboarding",
            entityId: 0,
            action: "GUIDED_TOUR_COMPLETED",
            oldValues: null,
            newValues: new
            {
                TenantId = tenantId,
                Role = role,
                CompletedAtUtc = DateTime.UtcNow
            },
            performedBy: string.IsNullOrWhiteSpace(performedBy) ? "system" : performedBy,
            ct: ct);
    }

    private async Task ActivateSelectedModules(
        Guid tenantId,
        OnboardingWizardRequest request,
        OnboardingProvisionResult result,
        CancellationToken ct)
    {
        var selected = request.ModuleCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selected.Count == 0)
        {
            return;
        }

        foreach (var moduleCode in selected)
        {
            try
            {
                await _subscriptionService.ActivateModule(tenantId, moduleCode, ct);
                result.ActivatedModules.Add(moduleCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Module activation skipped during onboarding for tenant {TenantId}: {ModuleCode}", tenantId, moduleCode);
                result.Warnings.Add($"{moduleCode}: {ex.Message}");
            }
        }
    }

    private async Task ApplyEntityStructure(
        Guid tenantId,
        int headOfficeInstitutionId,
        OnboardingWizardRequest request,
        CancellationToken ct)
    {
        var headOffice = await _db.Institutions
            .FirstOrDefaultAsync(x => x.Id == headOfficeInstitutionId && x.TenantId == tenantId, ct);

        if (headOffice is null)
        {
            return;
        }

        var branchNames = request.Branches
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingBranchNames = await _db.Institutions
            .Where(x => x.TenantId == tenantId && x.ParentInstitutionId == headOfficeInstitutionId)
            .Select(x => x.InstitutionName)
            .ToListAsync(ct);

        foreach (var branchName in branchNames.Where(x => !existingBranchNames.Contains(x, StringComparer.OrdinalIgnoreCase)))
        {
            var branchCode = BuildBranchCode(headOffice.InstitutionCode, branchName);
            _db.Institutions.Add(new Institution
            {
                TenantId = tenantId,
                JurisdictionId = headOffice.JurisdictionId,
                ParentInstitutionId = headOfficeInstitutionId,
                InstitutionCode = branchCode,
                InstitutionName = branchName,
                LicenseType = headOffice.LicenseType,
                EntityType = EntityType.Branch,
                BranchCode = branchCode,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                MaxUsersAllowed = headOffice.MaxUsersAllowed
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<int>> CreateAdditionalUsers(
        int institutionId,
        OnboardingWizardRequest request,
        OnboardingProvisionResult result,
        CancellationToken ct)
    {
        var invitedUserIds = new List<int>();

        foreach (var invite in request.AdditionalUsers.Where(x => !string.IsNullOrWhiteSpace(x.Email)))
        {
            try
            {
                if (!Enum.TryParse<InstitutionRole>(invite.Role, true, out var parsedRole))
                {
                    parsedRole = InstitutionRole.Maker;
                }

                var username = BuildUsername(invite.Email);
                var tempPassword = GenerateTemporaryPassword();
                var created = await _institutionAuthService.CreateUser(
                    institutionId,
                    username,
                    invite.Email.Trim(),
                    string.IsNullOrWhiteSpace(invite.FullName) ? username : invite.FullName.Trim(),
                    tempPassword,
                    parsedRole,
                    ct);

                invitedUserIds.Add(created.Id);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"User invite failed ({invite.Email}): {ex.Message}");
            }
        }

        return invitedUserIds;
    }

    private async Task ApplyWorkflowConfiguration(int institutionId, OnboardingWizardRequest request, CancellationToken ct)
    {
        var institution = await _db.Institutions
            .FirstOrDefaultAsync(x => x.Id == institutionId, ct);
        if (institution is null)
        {
            return;
        }

        institution.MakerCheckerEnabled = request.Workflow.EnableMakerChecker;

        var workflowSettings = new
        {
            request.Workflow.EnableMakerChecker,
            request.Workflow.ApprovalThreshold,
            request.Workflow.AllowDelegation
        };

        institution.SettingsJson = JsonSerializer.Serialize(workflowSettings);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ApplyBranding(Guid tenantId, OnboardingWizardRequest request, CancellationToken ct)
    {
        var config = await _brandingService.GetBrandingConfig(tenantId, ct);
        var primaryFallback = config.PrimaryColor ?? "#006B3F";
        var secondaryFallback = config.SecondaryColor ?? "#C8A415";
        var accentFallback = config.AccentColor ?? "#1A73E8";
        config.CompanyName = request.Profile.InstitutionName.Trim();
        config.PrimaryColor = NormalizeColor(request.Branding.PrimaryColor, primaryFallback);
        config.SecondaryColor = NormalizeColor(request.Branding.SecondaryColor, secondaryFallback);
        config.AccentColor = NormalizeColor(request.Branding.AccentColor, accentFallback);

        await _brandingService.UpdateBrandingConfig(tenantId, config, ct);

        if (request.Branding.LogoBytes is { Length: > 0 }
            && !string.IsNullOrWhiteSpace(request.Branding.LogoFileName)
            && !string.IsNullOrWhiteSpace(request.Branding.LogoContentType))
        {
            await using var stream = new MemoryStream(request.Branding.LogoBytes);
            await _brandingService.UploadLogo(
                tenantId,
                stream,
                request.Branding.LogoFileName,
                request.Branding.LogoContentType,
                ct);
        }
    }

    private async Task EnsurePeriodsForActivatedModules(Guid tenantId, CancellationToken ct)
    {
        var activeModuleIds = await _db.SubscriptionModules
            .AsNoTracking()
            .Where(x => x.Subscription != null
                        && x.Subscription.TenantId == tenantId
                        && x.IsActive)
            .Select(x => x.ModuleId)
            .Distinct()
            .ToListAsync(ct);

        if (activeModuleIds.Count == 0)
        {
            return;
        }

        var existingModuleIds = await _db.ReturnPeriods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ModuleId != null)
            .Select(x => x.ModuleId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var missingModuleIds = activeModuleIds
            .Except(existingModuleIds)
            .ToList();

        if (missingModuleIds.Count == 0)
        {
            return;
        }

        var modules = await _db.Modules
            .AsNoTracking()
            .Where(x => missingModuleIds.Contains(x.Id))
            .ToListAsync(ct);

        var today = DateTime.UtcNow.Date;
        foreach (var module in modules)
        {
            for (var offset = 0; offset < 12; offset++)
            {
                var date = new DateTime(today.Year, today.Month, 1).AddMonths(offset);
                var quarter = ((date.Month - 1) / 3) + 1;
                var frequency = NormalizeFrequency(module.DefaultFrequency);
                var deadline = ComputeDeadline(date, frequency, module.DeadlineOffsetDays);

                _db.ReturnPeriods.Add(new ReturnPeriod
                {
                    TenantId = tenantId,
                    ModuleId = module.Id,
                    Year = date.Year,
                    Month = date.Month,
                    Quarter = frequency == "Quarterly" ? quarter : null,
                    Frequency = frequency,
                    ReportingDate = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)),
                    DeadlineDate = deadline,
                    IsOpen = true,
                    Status = "Open",
                    NotificationLevel = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task NotifyInvitedUsers(Guid tenantId, IReadOnlyCollection<int> invitedUserIds, CancellationToken ct)
    {
        if (_notificationOrchestrator is null || invitedUserIds.Count == 0)
        {
            return;
        }

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = tenantId,
            EventType = NotificationEvents.UserInvited,
            Title = "Welcome to RegOS",
            Message = "Your account has been created. Sign in and complete your profile.",
            Priority = NotificationPriority.Normal,
            RecipientUserIds = invitedUserIds.ToList(),
            ActionUrl = "/login"
        }, ct);
    }

    private async Task<SubscriptionPlan?> ResolvePlan(string? planCode, CancellationToken ct)
    {
        var effectivePlan = string.IsNullOrWhiteSpace(planCode)
            ? "STARTER"
            : planCode.Trim().ToUpperInvariant();

        return await _db.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive && x.PlanCode == effectivePlan, ct);
    }

    private static void ValidateRequest(OnboardingWizardRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Profile.InstitutionName))
        {
            throw new InvalidOperationException("Institution name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Profile.ContactEmail))
        {
            throw new InvalidOperationException("Contact email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.AdminUser.FullName) || string.IsNullOrWhiteSpace(request.AdminUser.Email))
        {
            throw new InvalidOperationException("First admin details are required.");
        }
    }

    private static string BuildInstitutionCode(string institutionName)
    {
        var baseCode = new string(institutionName
            .Where(char.IsLetterOrDigit)
            .Take(8)
            .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(baseCode))
        {
            baseCode = "INST";
        }

        return baseCode.Length >= 6
            ? baseCode
            : (baseCode + RandomNumberGenerator.GetInt32(100, 999)).ToUpperInvariant();
    }

    private static string BuildBranchCode(string institutionCode, string branchName)
    {
        var branch = new string(branchName
            .Where(char.IsLetterOrDigit)
            .Take(4)
            .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(branch))
        {
            branch = "BR";
        }

        var prefix = institutionCode.Length > 6
            ? institutionCode[..6]
            : institutionCode;

        return $"{prefix}-{branch}";
    }

    private static string BuildUsername(string email)
    {
        var localPart = email.Split('@')[0];
        var safe = new string(localPart.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "user";
        }

        var suffix = RandomNumberGenerator.GetInt32(100, 999);
        return $"{safe.ToLowerInvariant()}{suffix}";
    }

    private static string NormalizeColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        var trimmed = color.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#')
        {
            return fallback;
        }

        for (var i = 1; i < trimmed.Length; i++)
        {
            if (!Uri.IsHexDigit(trimmed[i]))
            {
                return fallback;
            }
        }

        return trimmed.ToUpperInvariant();
    }

    private static string GenerateTemporaryPassword()
    {
        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);
        var token = Convert.ToBase64String(random).Replace('+', 'A').Replace('/', 'B').Replace("=", string.Empty);
        return $"Rg!{token[..10]}9";
    }

    private static string NormalizeFrequency(string? frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency))
        {
            return "Monthly";
        }

        return frequency.Trim().ToLowerInvariant() switch
        {
            "monthly" => "Monthly",
            "quarterly" => "Quarterly",
            "semiannual" => "SemiAnnual",
            "annual" => "Annual",
            _ => "Monthly"
        };
    }

    private static DateTime ComputeDeadline(DateTime periodMonthStart, string frequency, int? overrideOffsetDays)
    {
        if (overrideOffsetDays.HasValue && overrideOffsetDays.Value > 0)
        {
            return new DateTime(periodMonthStart.Year, periodMonthStart.Month, DateTime.DaysInMonth(periodMonthStart.Year, periodMonthStart.Month))
                .AddDays(overrideOffsetDays.Value);
        }

        var offset = frequency switch
        {
            "Monthly" => 30,
            "Quarterly" => 45,
            "SemiAnnual" => 60,
            "Annual" => 90,
            _ => 30
        };

        return new DateTime(periodMonthStart.Year, periodMonthStart.Month, DateTime.DaysInMonth(periodMonthStart.Year, periodMonthStart.Month))
            .AddDays(offset);
    }
}

public class OnboardingLookupData
{
    public List<OnboardingLicenceTypeOption> LicenceTypes { get; set; } = new();
    public List<OnboardingPlanOption> Plans { get; set; } = new();
}

public class OnboardingLicenceTypeOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Regulator { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class OnboardingPlanOption
{
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BasePriceMonthly { get; set; }
    public int MaxModules { get; set; }
    public int MaxUsersPerEntity { get; set; }
    public int MaxEntities { get; set; }
    public string? Features { get; set; }
}

public class OnboardingModuleOption
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsOptional { get; set; }
    public bool IsAvailableOnPlan { get; set; }
    public bool IsIncludedInBase { get; set; }
    public decimal PriceMonthly { get; set; }
}

public class OnboardingCostEstimate
{
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal BasePriceMonthly { get; set; }
    public List<OnboardingCostLine> ModuleLines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
}

public class OnboardingCostLine
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IncludedInBase { get; set; }
}

public class OnboardingWizardRequest
{
    public InstitutionProfileStep Profile { get; set; } = new();
    public List<string> LicenceTypeCodes { get; set; } = new();
    public string PlanCode { get; set; } = "STARTER";
    public List<string> ModuleCodes { get; set; } = new();
    public List<string> Branches { get; set; } = new();
    public FirstAdminStep AdminUser { get; set; } = new();
    public List<AdditionalUserInvite> AdditionalUsers { get; set; } = new();
    public WorkflowStep Workflow { get; set; } = new();
    public BrandingStep Branding { get; set; } = new();
}

public class InstitutionProfileStep
{
    public string InstitutionName { get; set; } = string.Empty;
    public string? TenantSlug { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? RcNumber { get; set; }
    public string? Tin { get; set; }
    public string? Address { get; set; }
}

public class FirstAdminStep
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class AdditionalUserInvite
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = nameof(InstitutionRole.Maker);
}

public class WorkflowStep
{
    public bool EnableMakerChecker { get; set; } = true;
    public decimal ApprovalThreshold { get; set; }
    public bool AllowDelegation { get; set; }
}

public class BrandingStep
{
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public byte[]? LogoBytes { get; set; }
    public string? LogoFileName { get; set; }
    public string? LogoContentType { get; set; }
}

public class OnboardingProvisionResult
{
    public bool Success { get; set; }
    public Guid TenantId { get; set; }
    public string TenantSlug { get; set; } = string.Empty;
    public int InstitutionId { get; set; }
    public string AdminTemporaryPassword { get; set; } = string.Empty;
    public bool CompletedInUnderSixtySeconds { get; set; }
    public long ProvisioningDurationMs { get; set; }
    public List<string> ActivatedModules { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class OnboardingChecklistSnapshot
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public int CompletionPercent { get; set; }
    public List<OnboardingChecklistItemState> Items { get; set; } = new();
}

public class OnboardingChecklistItemState
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
}
