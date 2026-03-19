using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public class TenantManagementService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ITenantOnboardingService _onboardingService;
    private readonly IEntitlementService _entitlementService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly SubscriptionModuleEntitlementBootstrapService _subscriptionModuleEntitlementBootstrapService;
    private readonly IAuditLogger _auditLogger;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantBrandingService _tenantBrandingService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantManagementService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        ITenantOnboardingService onboardingService,
        IEntitlementService entitlementService,
        ISubscriptionService subscriptionService,
        SubscriptionModuleEntitlementBootstrapService subscriptionModuleEntitlementBootstrapService,
        IAuditLogger auditLogger,
        ITenantContext tenantContext,
        ITenantBrandingService tenantBrandingService,
        IHttpContextAccessor httpContextAccessor)
    {
        _dbFactory = dbFactory;
        _onboardingService = onboardingService;
        _entitlementService = entitlementService;
        _subscriptionService = subscriptionService;
        _subscriptionModuleEntitlementBootstrapService = subscriptionModuleEntitlementBootstrapService;
        _auditLogger = auditLogger;
        _tenantContext = tenantContext;
        _tenantBrandingService = tenantBrandingService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<List<Tenant>> GetAllTenantsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants
            .OrderBy(t => t.TenantName)
            .ToListAsync(ct);
    }

    public async Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants.FindAsync(new object[] { tenantId }, ct);
    }

    public async Task<TenantDashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants.ToListAsync(ct);
        return new TenantDashboardStats
        {
            TotalTenants = tenants.Count,
            ActiveTenants = tenants.Count(t => t.Status == TenantStatus.Active),
            PendingTenants = tenants.Count(t => t.Status == TenantStatus.PendingActivation),
            SuspendedTenants = tenants.Count(t => t.Status == TenantStatus.Suspended)
        };
    }

    public async Task<TenantOnboardingResult> OnboardTenantAsync(TenantOnboardingRequest request, CancellationToken ct = default)
    {
        return await _onboardingService.OnboardTenant(request, ct);
    }

    public async Task<PlatformTenantProvisionResult> ProvisionTenantAsync(
        PlatformTenantProvisionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var licenceCode = request.LicenceCode.Trim().ToUpperInvariant();
        var moduleCodes = request.ModuleCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var planCode = await DeterminePlanCodeAsync(licenceCode, moduleCodes, ct);
        var onboardingRequest = new TenantOnboardingRequest
        {
            TenantName = request.TenantName.Trim(),
            TenantSlug = string.IsNullOrWhiteSpace(request.TenantSlug)
                ? null
                : request.TenantSlug.Trim(),
            TenantType = TenantType.Institution,
            ContactEmail = request.ContactEmail.Trim(),
            LicenceTypeCodes = [licenceCode],
            SubscriptionPlanCode = planCode,
            AdminEmail = request.AdminEmail.Trim(),
            AdminFullName = request.AdminFullName.Trim(),
            InstitutionCode = BuildInstitutionCode(request.TenantName),
            InstitutionName = request.TenantName.Trim(),
            InstitutionType = licenceCode,
            JurisdictionCode = "NG"
        };

        var onboarding = await _onboardingService.OnboardTenant(onboardingRequest, ct);
        var result = new PlatformTenantProvisionResult
        {
            Success = onboarding.Success,
            TenantId = onboarding.TenantId,
            TenantSlug = onboarding.TenantSlug,
            InstitutionId = onboarding.InstitutionId,
            PlanCode = planCode,
            ActivatedModules = onboarding.ActivatedModules.ToList(),
            Errors = onboarding.Errors.ToList()
        };

        if (!onboarding.Success)
        {
            return result;
        }

        await ApplyAdminPasswordAsync(onboarding.TenantId, request.AdminEmail, request.TempPassword, ct);
        await ApplyBrandingAsync(onboarding.TenantId, request, ct);

        foreach (var moduleCode in moduleCodes.Except(result.ActivatedModules, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await _subscriptionService.ActivateModule(onboarding.TenantId, moduleCode, ct);
                result.ActivatedModules.Add(moduleCode);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{moduleCode}: {ex.Message}");
            }
        }

        await EnsurePeriodsForActivatedModules(onboarding.TenantId, ct);

        var actor = ResolvePlatformActor();
        await _auditLogger.Log(
            "Tenant",
            0,
            "TenantProvisioned",
            null,
            new
            {
                onboarding.TenantId,
                onboarding.InstitutionId,
                request.LicenceCode,
                PlanCode = planCode,
                ActivatedModules = result.ActivatedModules,
                Warnings = result.Warnings
            },
            actor,
            ct);

        return result;
    }

    public async Task ActivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        if (tenant.Status == TenantStatus.PendingActivation)
        {
            tenant.Activate();
        }
        else if (tenant.Status == TenantStatus.Suspended)
        {
            tenant.Reactivate();
        }
        else if (tenant.Status != TenantStatus.Active)
        {
            throw new InvalidOperationException($"Tenant cannot be activated from status {tenant.Status}.");
        }

        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantActivated", tenantId, ct);
    }

    public async Task SuspendTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Suspend("Admin action");
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantSuspended", tenantId, ct);
    }

    public async Task ReactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Reactivate();
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantReactivated", tenantId, ct);
    }

    public async Task DeactivateTenantAsync(Guid tenantId, string? actor = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Deactivate();
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantDeactivated", tenantId, actor, ct);
    }

    public async Task<List<TenantLicenceType>> GetTenantLicencesAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TenantLicenceTypes
            .Include(tlt => tlt.LicenceType)
            .Where(tlt => tlt.TenantId == tenantId)
            .OrderByDescending(tlt => tlt.IsActive)
            .ThenBy(tlt => tlt.LicenceType!.DisplayOrder)
            .ThenBy(tlt => tlt.LicenceType!.Code)
            .ToListAsync(ct);
    }

    public Task<TenantLicenceImpactPreview> PreviewAssignLicenceAsync(Guid tenantId, int licenceTypeId, CancellationToken ct = default)
        => BuildLicenceImpactPreviewAsync(tenantId, licenceTypeId, activate: true, ct);

    public Task<TenantLicenceImpactPreview> PreviewRemoveLicenceAsync(Guid tenantId, int licenceTypeId, CancellationToken ct = default)
        => BuildLicenceImpactPreviewAsync(tenantId, licenceTypeId, activate: false, ct);

    public async Task<TenantLicenceChangeResult> AssignLicenceAsync(
        Guid tenantId,
        int licenceTypeId,
        string? registrationNumber = null,
        DateTime? effectiveDate = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        _ = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var licenceType = await db.LicenceTypes
            .FirstOrDefaultAsync(x => x.Id == licenceTypeId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Licence type not found or inactive");

        var tenantLicence = await db.TenantLicenceTypes
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LicenceTypeId == licenceTypeId, ct);

        var changed = false;
        if (tenantLicence is null)
        {
            tenantLicence = new TenantLicenceType
            {
                TenantId = tenantId,
                LicenceTypeId = licenceTypeId,
                RegistrationNumber = registrationNumber,
                EffectiveDate = effectiveDate ?? DateTime.UtcNow.Date,
                IsActive = true
            };
            db.TenantLicenceTypes.Add(tenantLicence);
            changed = true;
        }
        else
        {
            if (!tenantLicence.IsActive)
            {
                tenantLicence.IsActive = true;
                tenantLicence.ExpiryDate = null;
                changed = true;
            }

            if (!string.Equals(tenantLicence.RegistrationNumber, registrationNumber, StringComparison.Ordinal))
            {
                tenantLicence.RegistrationNumber = registrationNumber;
                changed = true;
            }

            var resolvedEffectiveDate = effectiveDate ?? tenantLicence.EffectiveDate;
            if (tenantLicence.EffectiveDate != resolvedEffectiveDate)
            {
                tenantLicence.EffectiveDate = resolvedEffectiveDate;
                changed = true;
            }
        }

        var reconciliation = new SubscriptionModuleEntitlementBootstrapResult();
        if (changed)
        {
            await db.SaveChangesAsync(ct);
            reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
            await _entitlementService.InvalidateCache(tenantId);
            await LogPlatformAction("TenantLicenceAssigned", tenantId, ct);
        }

        tenantLicence.LicenceType = licenceType;
        return new TenantLicenceChangeResult
        {
            TenantLicence = tenantLicence,
            Reconciliation = reconciliation
        };
    }

    public async Task<TenantLicenceChangeResult> RemoveLicenceAsync(Guid tenantId, int licenceTypeId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenantLicence = await db.TenantLicenceTypes
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LicenceTypeId == licenceTypeId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Active tenant licence not found");

        tenantLicence.IsActive = false;
        tenantLicence.ExpiryDate = tenantLicence.ExpiryDate ?? DateTime.UtcNow.Date;
        await db.SaveChangesAsync(ct);

        var reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
        await _entitlementService.InvalidateCache(tenantId);
        await LogPlatformAction("TenantLicenceRemoved", tenantId, ct);

        return new TenantLicenceChangeResult
        {
            TenantLicence = tenantLicence,
            Reconciliation = reconciliation
        };
    }

    public async Task<SubscriptionModuleEntitlementBootstrapResult> ReconcileTenantModulesAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        _ = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
        await _entitlementService.InvalidateCache(tenantId);
        await LogPlatformAction("TenantModulesReconciled", tenantId, ct);
        return reconciliation;
    }

    public async Task<TenantModuleReconciliationBatchResult> ReconcileTenantModulesAsync(
        IEnumerable<Guid> tenantIds,
        CancellationToken ct = default)
    {
        var requestedTenantIds = tenantIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedTenantIds.Count == 0)
        {
            return new TenantModuleReconciliationBatchResult();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existingTenantIds = await db.Tenants
            .AsNoTracking()
            .Where(x => requestedTenantIds.Contains(x.TenantId))
            .Select(x => x.TenantId)
            .ToListAsync(ct);

        var aggregate = new SubscriptionModuleEntitlementBootstrapResult();
        var processed = 0;

        foreach (var tenantId in existingTenantIds)
        {
            var result = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
            await _entitlementService.InvalidateCache(tenantId);
            await LogPlatformAction("TenantModulesReconciled", tenantId, ct);

            aggregate = new SubscriptionModuleEntitlementBootstrapResult
            {
                ModulesCreated = aggregate.ModulesCreated + result.ModulesCreated,
                ModulesReactivated = aggregate.ModulesReactivated + result.ModulesReactivated,
                ModulesUpdated = aggregate.ModulesUpdated + result.ModulesUpdated,
                ModulesDeactivated = aggregate.ModulesDeactivated + result.ModulesDeactivated,
                TenantsTouched = aggregate.TenantsTouched + result.TenantsTouched
            };

            processed++;
        }

        return new TenantModuleReconciliationBatchResult
        {
            RequestedTenants = requestedTenantIds.Count,
            ProcessedTenants = processed,
            Reconciliation = aggregate
        };
    }

    public async Task<List<LicenceType>> GetAllLicenceTypesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.LicenceTypes
            .Where(lt => lt.IsActive)
            .OrderBy(lt => lt.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<List<Module>> GetAllModulesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Modules
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<string?> GetTenantName(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TenantName)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<TenantLicenceImpactPreview> BuildLicenceImpactPreviewAsync(
        Guid tenantId,
        int licenceTypeId,
        bool activate,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        _ = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var licenceType = await db.LicenceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == licenceTypeId, ct)
            ?? throw new InvalidOperationException("Licence type not found");

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Expired)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Active subscription not found");

        var currentLicenceIds = await db.TenantLicenceTypes
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .Select(x => x.LicenceTypeId)
            .ToListAsync(ct);

        var targetLicenceIds = currentLicenceIds
            .ToHashSet();

        if (activate)
        {
            targetLicenceIds.Add(licenceTypeId);
        }
        else
        {
            targetLicenceIds.Remove(licenceTypeId);
        }

        var pricingRows = await db.PlanModulePricing
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.PlanId == subscription.PlanId && x.Module != null && x.Module.IsActive)
            .ToListAsync(ct);

        var eligibleModuleIds = await (
            from licenceModule in db.LicenceModuleMatrix.AsNoTracking()
            join pricing in db.PlanModulePricing.AsNoTracking() on licenceModule.ModuleId equals pricing.ModuleId
            join module in db.Modules.AsNoTracking() on licenceModule.ModuleId equals module.Id
            where targetLicenceIds.Contains(licenceModule.LicenceTypeId)
                  && pricing.PlanId == subscription.PlanId
                  && module.IsActive
            select licenceModule.ModuleId)
            .Distinct()
            .ToListAsync(ct);

        var includedPricingRows = pricingRows
            .Where(x => x.IsIncludedInBase && targetLicenceIds.Count > 0)
            .Where(x => eligibleModuleIds.Contains(x.ModuleId))
            .ToList();

        var subscriptionModules = await db.SubscriptionModules
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.SubscriptionId == subscription.Id)
            .ToListAsync(ct);

        var moduleRows = new List<TenantLicenceImpactModuleRow>();
        foreach (var pricing in includedPricingRows)
        {
            var existing = subscriptionModules.FirstOrDefault(x => x.ModuleId == pricing.ModuleId);
            if (existing is null)
            {
                moduleRows.Add(new TenantLicenceImpactModuleRow
                {
                    ModuleCode = pricing.Module!.ModuleCode,
                    ModuleName = pricing.Module.ModuleName,
                    Action = "Activate",
                    PriceMonthly = pricing.PriceMonthly,
                    PriceAnnual = pricing.PriceAnnual,
                    Reason = "Included in base for the current plan and licence mix."
                });
                continue;
            }

            if (!existing.IsActive)
            {
                moduleRows.Add(new TenantLicenceImpactModuleRow
                {
                    ModuleCode = existing.Module?.ModuleCode ?? pricing.Module!.ModuleCode,
                    ModuleName = existing.Module?.ModuleName ?? pricing.Module!.ModuleName,
                    Action = "Reactivate",
                    PriceMonthly = pricing.PriceMonthly,
                    PriceAnnual = pricing.PriceAnnual,
                    Reason = "Module becomes included-in-base again under the target licence mix."
                });
                continue;
            }

            if (existing.PriceMonthly != pricing.PriceMonthly || existing.PriceAnnual != pricing.PriceAnnual)
            {
                moduleRows.Add(new TenantLicenceImpactModuleRow
                {
                    ModuleCode = existing.Module?.ModuleCode ?? pricing.Module!.ModuleCode,
                    ModuleName = existing.Module?.ModuleName ?? pricing.Module!.ModuleName,
                    Action = "Reprice",
                    PriceMonthly = pricing.PriceMonthly,
                    PriceAnnual = pricing.PriceAnnual,
                    Reason = "Plan pricing for the module will be refreshed during reconciliation."
                });
            }
        }

        foreach (var activeModule in subscriptionModules.Where(x => x.IsActive))
        {
            if (eligibleModuleIds.Contains(activeModule.ModuleId))
            {
                continue;
            }

            moduleRows.Add(new TenantLicenceImpactModuleRow
            {
                ModuleCode = activeModule.Module?.ModuleCode ?? $"M-{activeModule.ModuleId}",
                ModuleName = activeModule.Module?.ModuleName ?? "Unknown Module",
                Action = "Deactivate",
                PriceMonthly = activeModule.PriceMonthly,
                PriceAnnual = activeModule.PriceAnnual,
                Reason = "Module would no longer be licence-eligible after this change."
            });
        }

        moduleRows = moduleRows
            .OrderByDescending(x => x.Action == "Deactivate")
            .ThenBy(x => x.ModuleCode)
            .ToList();

        return new TenantLicenceImpactPreview
        {
            TenantId = tenantId,
            LicenceTypeId = licenceTypeId,
            LicenceCode = licenceType.Code,
            LicenceName = licenceType.Name,
            Operation = activate ? "Assign" : "Remove",
            ModulesToActivate = moduleRows.Count(x => x.Action == "Activate"),
            ModulesToReactivate = moduleRows.Count(x => x.Action == "Reactivate"),
            ModulesToReprice = moduleRows.Count(x => x.Action == "Reprice"),
            ModulesToDeactivate = moduleRows.Count(x => x.Action == "Deactivate"),
            Modules = moduleRows
        };
    }

    private Task LogPlatformAction(string action, Guid tenantId, CancellationToken ct)
        => LogPlatformAction(action, tenantId, null, ct);

    private async Task LogPlatformAction(string action, Guid tenantId, string? explicitActor, CancellationToken ct)
    {
        await _auditLogger.Log(
            "Tenant",
            tenantId.ToString(),
            action,
            null,
            new
            {
                IsPlatformAdmin = _tenantContext.IsPlatformAdmin,
                ImpersonatedTenantId = _tenantContext.ImpersonatingTenantId,
                TenantId = tenantId
            },
            explicitActor ?? ResolvePlatformActor(),
            explicitTenantId: tenantId,
            ct);
    }

    private async Task<string> DeterminePlanCodeAsync(string licenceCode, IReadOnlyCollection<string> selectedModuleCodes, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var requiredModuleCodes = await (
            from matrix in db.LicenceModuleMatrix.AsNoTracking()
            join licence in db.LicenceTypes.AsNoTracking() on matrix.LicenceTypeId equals licence.Id
            join module in db.Modules.AsNoTracking() on matrix.ModuleId equals module.Id
            where licence.Code == licenceCode && matrix.IsRequired && module.IsActive
            select module.ModuleCode)
            .ToListAsync(ct);

        var totalModuleCount = requiredModuleCodes
            .Concat(selectedModuleCodes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return totalModuleCount switch
        {
            <= 1 => "STARTER",
            <= 5 => "PROFESSIONAL",
            _ => "ENTERPRISE"
        };
    }

    private async Task ApplyAdminPasswordAsync(Guid tenantId, string adminEmail, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var adminUser = await db.InstitutionUsers
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId
                  && x.Email == adminEmail
                  && x.Role == InstitutionRole.Admin,
                ct);

        if (adminUser is null)
        {
            throw new InvalidOperationException("The initial tenant administrator could not be located after provisioning.");
        }

        adminUser.PasswordHash = FC.Engine.Application.Services.InstitutionAuthService.HashPassword(password);
        adminUser.MustChangePassword = true;
        adminUser.FailedLoginAttempts = 0;
        adminUser.LockedUntil = null;

        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyBrandingAsync(Guid tenantId, PlatformTenantProvisionRequest request, CancellationToken ct)
    {
        var config = await _tenantBrandingService.GetBrandingConfig(tenantId, ct);
        config.CompanyName = string.IsNullOrWhiteSpace(request.BrandName)
            ? request.TenantName.Trim()
            : request.BrandName.Trim();

        if (!string.IsNullOrWhiteSpace(request.AccentColor))
        {
            config.PrimaryColor = request.AccentColor.Trim();
            config.AccentColor = request.AccentColor.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.LogoUrl))
        {
            config.LogoUrl = request.LogoUrl.Trim();
            config.LogoSmallUrl = request.LogoUrl.Trim();
        }

        await _tenantBrandingService.UpdateBrandingConfig(tenantId, config, ct);
    }

    private async Task EnsurePeriodsForActivatedModules(Guid tenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var activeModuleIds = await db.SubscriptionModules
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

        var existingModuleIds = await db.ReturnPeriods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ModuleId.HasValue)
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

        var modules = await db.Modules
            .AsNoTracking()
            .Where(x => missingModuleIds.Contains(x.Id))
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(ct);

        var today = DateTime.UtcNow.Date;
        foreach (var module in modules)
        {
            for (var offset = 0; offset < 12; offset++)
            {
                var periodStart = new DateTime(today.Year, today.Month, 1).AddMonths(offset);
                var frequency = NormalizeFrequency(module.DefaultFrequency);
                var deadline = ComputeDeadline(periodStart, frequency, module.DeadlineOffsetDays);

                db.ReturnPeriods.Add(new ReturnPeriod
                {
                    TenantId = tenantId,
                    ModuleId = module.Id,
                    Year = periodStart.Year,
                    Month = periodStart.Month,
                    Quarter = frequency == "Quarterly" ? ((periodStart.Month - 1) / 3) + 1 : null,
                    Frequency = frequency,
                    ReportingDate = new DateTime(periodStart.Year, periodStart.Month, DateTime.DaysInMonth(periodStart.Year, periodStart.Month)),
                    DeadlineDate = deadline,
                    IsOpen = true,
                    Status = "Open",
                    NotificationLevel = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private string ResolvePlatformActor()
    {
        return FC.Engine.Admin.Utilities.UserIdentityResolver.TryResolveActor(_httpContextAccessor.HttpContext?.User, out var actor)
            ? actor
            : "system-service";
    }

    private static string BuildInstitutionCode(string tenantName)
    {
        var baseCode = new string(tenantName
            .Where(char.IsLetterOrDigit)
            .Take(8)
            .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(baseCode))
        {
            baseCode = "TENANT";
        }

        return baseCode.Length >= 6
            ? baseCode
            : $"{baseCode}{Random.Shared.Next(100, 999)}";
    }

    private static string NormalizeFrequency(string? frequency)
    {
        return frequency?.Trim().ToUpperInvariant() switch
        {
            "QUARTERLY" => "Quarterly",
            "SEMIANNUAL" or "SEMI-ANNUAL" => "SemiAnnual",
            "ANNUAL" or "YEARLY" => "Annual",
            _ => "Monthly"
        };
    }

    private static DateTime ComputeDeadline(DateTime periodStart, string frequency, int? deadlineOffsetDays)
    {
        var periodEnd = frequency switch
        {
            "Quarterly" => periodStart.AddMonths(3).AddDays(-1),
            "SemiAnnual" => periodStart.AddMonths(6).AddDays(-1),
            "Annual" => periodStart.AddYears(1).AddDays(-1),
            _ => periodStart.AddMonths(1).AddDays(-1)
        };

        var offset = deadlineOffsetDays ?? frequency switch
        {
            "Quarterly" => 45,
            "SemiAnnual" => 60,
            "Annual" => 90,
            _ => 30
        };

        return periodEnd.AddDays(offset);
    }
}

public class TenantDashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int PendingTenants { get; set; }
    public int SuspendedTenants { get; set; }
}

public class TenantLicenceChangeResult
{
    public TenantLicenceType TenantLicence { get; set; } = new();
    public SubscriptionModuleEntitlementBootstrapResult Reconciliation { get; set; } = new();
}

public class TenantLicenceImpactPreview
{
    public Guid TenantId { get; set; }
    public int LicenceTypeId { get; set; }
    public string LicenceCode { get; set; } = string.Empty;
    public string LicenceName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public int ModulesToActivate { get; set; }
    public int ModulesToReactivate { get; set; }
    public int ModulesToReprice { get; set; }
    public int ModulesToDeactivate { get; set; }
    public List<TenantLicenceImpactModuleRow> Modules { get; set; } = new();
}

public class TenantLicenceImpactModuleRow
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceAnnual { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class PlatformTenantProvisionRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string? TenantSlug { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string LicenceCode { get; set; } = string.Empty;
    public List<string> ModuleCodes { get; set; } = new();
    public string AdminFullName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string TempPassword { get; set; } = string.Empty;
    public string? BrandName { get; set; }
    public string? LogoUrl { get; set; }
    public string? AccentColor { get; set; }
}

public class PlatformTenantProvisionResult
{
    public bool Success { get; set; }
    public Guid TenantId { get; set; }
    public string TenantSlug { get; set; } = string.Empty;
    public int InstitutionId { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public List<string> ActivatedModules { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class TenantModuleReconciliationBatchResult
{
    public int RequestedTenants { get; set; }
    public int ProcessedTenants { get; set; }
    public SubscriptionModuleEntitlementBootstrapResult Reconciliation { get; set; } = new();
}
