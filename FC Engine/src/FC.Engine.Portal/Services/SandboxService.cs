using System.Text.Json;
using System.Security.Cryptography;
using Dapper;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Portal.Services;

public class SandboxService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ITenantBrandingService _brandingService;
    private readonly InstitutionAuthService _institutionAuthService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DynamicSqlBuilder _sqlBuilder;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly ILogger<SandboxService> _logger;

    public SandboxService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        ITenantOnboardingService tenantOnboardingService,
        ISubscriptionService subscriptionService,
        ITenantBrandingService brandingService,
        InstitutionAuthService institutionAuthService,
        IDbConnectionFactory connectionFactory,
        DynamicSqlBuilder sqlBuilder,
        ILogger<SandboxService> logger,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _dbFactory = dbFactory;
        _tenantOnboardingService = tenantOnboardingService;
        _subscriptionService = subscriptionService;
        _brandingService = brandingService;
        _institutionAuthService = institutionAuthService;
        _connectionFactory = connectionFactory;
        _sqlBuilder = sqlBuilder;
        _logger = logger;
        _notificationOrchestrator = notificationOrchestrator;
    }

    public async Task<SandboxContext?> GetContext(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (tenant is null)
        {
            return null;
        }

        var isSandbox = tenant.TenantType == TenantType.Sandbox
            || tenant.TenantSlug.EndsWith("-sandbox", StringComparison.OrdinalIgnoreCase);
        if (!isSandbox)
        {
            return new SandboxContext
            {
                IsSandbox = false,
                CurrentTenantId = tenantId,
                CurrentTenantName = tenant.TenantName
            };
        }

        var production = await ResolveProductionTenant(tenant, ct);
        return new SandboxContext
        {
            IsSandbox = true,
            CurrentTenantId = tenant.TenantId,
            CurrentTenantName = tenant.TenantName,
            ProductionTenantId = production?.TenantId,
            ProductionTenantName = production?.TenantName,
            ProductionLoginUrl = production is null ? "/login" : BuildPortalUrl(production.TenantSlug, "/login")
        };
    }

    public async Task<SandboxProvisionResult> CreateSandbox(Guid productionTenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var result = new SandboxProvisionResult();

        var productionTenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == productionTenantId, ct);
        if (productionTenant is null)
        {
            result.Errors.Add("Production tenant not found.");
            return result;
        }

        var sandboxSlug = BuildSandboxSlug(productionTenant.TenantSlug);
        var existingSandbox = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantSlug == sandboxSlug, ct);

        if (existingSandbox is null)
        {
            var licenceCodes = await db.TenantLicenceTypes
                .AsNoTracking()
                .Where(x => x.TenantId == productionTenantId && x.IsActive && x.LicenceType != null)
                .Select(x => x.LicenceType!.Code)
                .Distinct()
                .ToListAsync(ct);

            if (licenceCodes.Count == 0)
            {
                result.Errors.Add("No active licence types found for production tenant.");
                return result;
            }

            var activePlanCode = await db.Subscriptions
                .AsNoTracking()
                .Where(x => x.TenantId == productionTenantId && x.Status != SubscriptionStatus.Cancelled && x.Plan != null)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => x.Plan!.PlanCode)
                .FirstOrDefaultAsync(ct) ?? "STARTER";

            var productionInstitution = await db.Institutions
                .AsNoTracking()
                .Where(x => x.TenantId == productionTenantId)
                .OrderBy(x => x.EntityType)
                .ThenBy(x => x.Id)
                .FirstOrDefaultAsync(ct);

            if (productionInstitution is null)
            {
                result.Errors.Add("Production institution profile not found.");
                return result;
            }

            var productionAdmin = await db.InstitutionUsers
                .AsNoTracking()
                .Where(x => x.TenantId == productionTenantId && x.Role == InstitutionRole.Admin && x.IsActive)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(ct);

            var adminEmail = BuildSandboxEmailAlias(
                productionAdmin?.Email ?? productionTenant.ContactEmail ?? "admin@regos.app",
                sandboxSlug);

            var onboarding = await _tenantOnboardingService.OnboardTenant(new TenantOnboardingRequest
            {
                TenantName = $"{productionTenant.TenantName} [SANDBOX]",
                TenantSlug = sandboxSlug,
                TenantType = TenantType.Sandbox,
                ContactEmail = productionTenant.ContactEmail ?? adminEmail,
                ContactPhone = productionTenant.ContactPhone,
                Address = productionTenant.Address,
                RcNumber = productionTenant.RcNumber,
                TaxId = productionTenant.TaxId,
                LicenceTypeCodes = licenceCodes,
                SubscriptionPlanCode = activePlanCode,
                AdminEmail = adminEmail,
                AdminFullName = productionAdmin?.DisplayName ?? $"{productionTenant.TenantName} Sandbox Admin",
                InstitutionCode = BuildSandboxInstitutionCode(productionInstitution.InstitutionCode),
                InstitutionName = $"{productionInstitution.InstitutionName} Sandbox",
                InstitutionType = productionInstitution.LicenseType,
                JurisdictionCode = "NG"
            }, ct);

            if (!onboarding.Success)
            {
                result.Errors.AddRange(onboarding.Errors);
                return result;
            }

            result.SandboxTenantId = onboarding.TenantId;
            result.SandboxTenantSlug = onboarding.TenantSlug;
            result.AdminTemporaryPassword = onboarding.AdminTemporaryPassword;
        }
        else
        {
            result.SandboxTenantId = existingSandbox.TenantId;
            result.SandboxTenantSlug = existingSandbox.TenantSlug;
        }

        await CopyBranding(productionTenantId, result.SandboxTenantId, ct);
        await CopyModuleActivation(productionTenantId, result.SandboxTenantId, ct);
        await CopyInstitutionSettings(productionTenantId, result.SandboxTenantId, ct);
        await CopyUsers(productionTenantId, result.SandboxTenantId, ct);
        await CopyPeriods(productionTenantId, result.SandboxTenantId, ct);
        await ResetSandbox(result.SandboxTenantId, ct);

        result.Success = true;
        result.LoginUrl = BuildPortalUrl(result.SandboxTenantSlug, "/login");

        if (_notificationOrchestrator is not null)
        {
            try
            {
                await _notificationOrchestrator.Notify(new NotificationRequest
                {
                    TenantId = productionTenantId,
                    EventType = NotificationEvents.SystemAnnouncement,
                    Title = "Sandbox environment is ready",
                    Message = "Your training sandbox has been provisioned and loaded with sample data.",
                    Priority = NotificationPriority.Normal,
                    ActionUrl = result.LoginUrl
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send sandbox creation notification for tenant {TenantId}.", productionTenantId);
            }
        }

        return result;
    }

    public async Task ResetSandbox(Guid sandboxTenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sandbox = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == sandboxTenantId, ct);
        if (sandbox is null || sandbox.TenantType != TenantType.Sandbox)
        {
            throw new InvalidOperationException("Only sandbox tenants can be reset.");
        }

        var submissions = await db.Submissions
            .AsNoTracking()
            .Where(x => x.TenantId == sandboxTenantId)
            .Select(x => new { x.Id, x.ReturnCode })
            .ToListAsync(ct);

        var submissionIds = submissions.Select(x => x.Id).ToList();
        if (submissionIds.Count > 0)
        {
            var templateTables = await db.ReturnTemplates
                .AsNoTracking()
                .Where(x => submissions.Select(s => s.ReturnCode).Contains(x.ReturnCode))
                .ToDictionaryAsync(x => x.ReturnCode, x => x.PhysicalTableName, StringComparer.OrdinalIgnoreCase, ct);

            using var conn = await _connectionFactory.CreateConnectionAsync(sandboxTenantId, ct);
            foreach (var pair in templateTables)
            {
                var sql = $"DELETE FROM dbo.[{pair.Value}] WHERE submission_id IN @submissionIds AND TenantId = @tenantId";
                await conn.ExecuteAsync(new CommandDefinition(sql, new { submissionIds, tenantId = sandboxTenantId }, cancellationToken: ct));
            }

            var reportIds = await db.ValidationReports
                .Where(x => x.TenantId == sandboxTenantId)
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (reportIds.Count > 0)
            {
                await db.ValidationErrors
                    .Where(x => reportIds.Contains(x.ValidationReportId))
                    .ExecuteDeleteAsync(ct);
            }

            await db.ValidationReports
                .Where(x => x.TenantId == sandboxTenantId)
                .ExecuteDeleteAsync(ct);

            await db.Submissions
                .Where(x => x.TenantId == sandboxTenantId)
                .ExecuteDeleteAsync(ct);
        }

        await LoadSampleData(sandboxTenantId, ct);
    }

    public async Task LoadSampleData(Guid sandboxTenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == sandboxTenantId, ct);
        if (tenant is null || tenant.TenantType != TenantType.Sandbox)
        {
            throw new InvalidOperationException("Sample data can only be loaded for sandbox tenants.");
        }

        var institution = await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == sandboxTenantId)
            .OrderBy(x => x.EntityType)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (institution is null)
        {
            return;
        }

        var activeModuleIds = await db.SubscriptionModules
            .AsNoTracking()
            .Where(x => x.IsActive && x.Subscription != null && x.Subscription.TenantId == sandboxTenantId)
            .Select(x => x.ModuleId)
            .Distinct()
            .ToListAsync(ct);

        if (activeModuleIds.Count == 0)
        {
            return;
        }

        var periodsByModule = await db.ReturnPeriods
            .AsNoTracking()
            .Where(x => x.TenantId == sandboxTenantId && x.ModuleId != null && activeModuleIds.Contains(x.ModuleId.Value))
            .GroupBy(x => x.ModuleId!.Value)
            .ToDictionaryAsync(
                x => x.Key,
                x => x.OrderByDescending(p => p.ReportingDate).ToList(),
                ct);

        var templates = await db.ReturnTemplates
            .AsNoTracking()
            .Where(x => x.ModuleId != null && activeModuleIds.Contains(x.ModuleId.Value))
            .OrderBy(x => x.ModuleId)
            .ThenBy(x => x.ReturnCode)
            .ToListAsync(ct);

        var templateIds = templates.Select(x => x.Id).ToList();
        var versions = await db.TemplateVersions
            .AsNoTracking()
            .Where(x => templateIds.Contains(x.TemplateId))
            .Include(x => x.Fields)
            .Include(x => x.ItemCodes)
            .ToListAsync(ct);

        var latestVersionByTemplate = versions
            .GroupBy(x => x.TemplateId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(v => v.VersionNumber).First());

        var moduleLookup = await db.Modules
            .AsNoTracking()
            .Where(x => activeModuleIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        using var conn = await _connectionFactory.CreateConnectionAsync(sandboxTenantId, ct);

        foreach (var template in templates)
        {
            if (template.ModuleId is null
                || !periodsByModule.TryGetValue(template.ModuleId.Value, out var modulePeriods)
                || modulePeriods.Count == 0
                || !latestVersionByTemplate.TryGetValue(template.Id, out var version)
                || !moduleLookup.TryGetValue(template.ModuleId.Value, out var module))
            {
                continue;
            }

            var period = modulePeriods.First();
            var submission = new Submission
            {
                TenantId = sandboxTenantId,
                InstitutionId = institution.Id,
                ReturnPeriodId = period.Id,
                ReturnCode = template.ReturnCode,
                TemplateVersionId = version.Id,
                Status = SubmissionStatus.Accepted,
                SubmittedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                ApprovalRequired = false
            };

            db.Submissions.Add(submission);
            await db.SaveChangesAsync(ct);

            var sampleRows = BuildSampleRows(module, template.StructuralCategory, version.Fields, version.ItemCodes);
            foreach (var sampleRow in sampleRows)
            {
                var (sql, parameters) = _sqlBuilder.BuildInsert(
                    template.PhysicalTableName,
                    version.Fields,
                    sampleRow,
                    submission.Id,
                    sandboxTenantId);
                await conn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
            }

            submission.ParsedDataJson = JsonSerializer.Serialize(BuildSummaryJson(version.Fields, sampleRows));
            db.ValidationReports.Add(new ValidationReport
            {
                TenantId = sandboxTenantId,
                SubmissionId = submission.Id,
                CreatedAt = DateTime.UtcNow,
                FinalizedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task CopyBranding(Guid sourceTenantId, Guid targetTenantId, CancellationToken ct)
    {
        var source = await _brandingService.GetBrandingConfig(sourceTenantId, ct);
        await _brandingService.UpdateBrandingConfig(targetTenantId, source, ct);
    }

    private async Task CopyModuleActivation(Guid sourceTenantId, Guid targetTenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sourceModules = await db.SubscriptionModules
            .AsNoTracking()
            .Where(x => x.IsActive && x.Subscription != null && x.Subscription.TenantId == sourceTenantId && x.Module != null)
            .Select(x => x.Module!.ModuleCode)
            .Distinct()
            .ToListAsync(ct);

        foreach (var moduleCode in sourceModules)
        {
            try
            {
                await _subscriptionService.ActivateModule(targetTenantId, moduleCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping sandbox module activation for {ModuleCode}.", moduleCode);
            }
        }
    }

    private async Task CopyInstitutionSettings(Guid sourceTenantId, Guid targetTenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var source = await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == sourceTenantId)
            .OrderBy(x => x.EntityType)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var target = await db.Institutions
            .FirstOrDefaultAsync(x => x.TenantId == targetTenantId, ct);

        if (source is null || target is null)
        {
            return;
        }

        target.MakerCheckerEnabled = source.MakerCheckerEnabled;
        target.SettingsJson = source.SettingsJson;
        target.Address = source.Address;
        target.ContactEmail = source.ContactEmail;
        target.ContactPhone = source.ContactPhone;
        target.SubscriptionTier = source.SubscriptionTier;
        await db.SaveChangesAsync(ct);
    }

    private async Task CopyUsers(Guid sourceTenantId, Guid targetTenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sourceInstitution = await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == sourceTenantId)
            .OrderBy(x => x.EntityType)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var targetInstitution = await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == targetTenantId)
            .OrderBy(x => x.EntityType)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (sourceInstitution is null || targetInstitution is null)
        {
            return;
        }

        var existingTargetEmails = await db.InstitutionUsers
            .AsNoTracking()
            .Where(x => x.TenantId == targetTenantId)
            .Select(x => x.Email)
            .ToListAsync(ct);

        var usersToClone = await db.InstitutionUsers
            .AsNoTracking()
            .Where(x => x.TenantId == sourceTenantId && x.InstitutionId == sourceInstitution.Id && x.IsActive)
            .OrderBy(x => x.Id)
            .Take(20)
            .ToListAsync(ct);

        foreach (var sourceUser in usersToClone)
        {
            var aliasEmail = BuildSandboxEmailAlias(sourceUser.Email, targetTenantId.ToString("N")[..6]);
            if (existingTargetEmails.Contains(aliasEmail, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await _institutionAuthService.CreateUser(
                    targetInstitution.Id,
                    $"{sourceUser.Username}_sbx",
                    aliasEmail,
                    sourceUser.DisplayName,
                    GenerateTemporaryPassword(),
                    sourceUser.Role,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to clone sandbox user {Email}.", sourceUser.Email);
            }
        }
    }

    private async Task CopyPeriods(Guid sourceTenantId, Guid targetTenantId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var targetModuleIds = await db.SubscriptionModules
            .AsNoTracking()
            .Where(x => x.IsActive && x.Subscription != null && x.Subscription.TenantId == targetTenantId)
            .Select(x => x.ModuleId)
            .Distinct()
            .ToListAsync(ct);

        var sourcePeriods = await db.ReturnPeriods
            .AsNoTracking()
            .Where(x => x.TenantId == sourceTenantId
                        && x.ModuleId != null
                        && targetModuleIds.Contains(x.ModuleId.Value))
            .OrderByDescending(x => x.ReportingDate)
            .Take(500)
            .ToListAsync(ct);

        if (sourcePeriods.Count == 0)
        {
            return;
        }

        var targetKeys = await db.ReturnPeriods
            .AsNoTracking()
            .Where(x => x.TenantId == targetTenantId && x.ModuleId != null)
            .Select(x => new { x.ModuleId, x.Year, x.Month, x.Quarter, x.Frequency })
            .ToListAsync(ct);

        foreach (var sourcePeriod in sourcePeriods)
        {
            var exists = targetKeys.Any(x =>
                x.ModuleId == sourcePeriod.ModuleId
                && x.Year == sourcePeriod.Year
                && x.Month == sourcePeriod.Month
                && x.Quarter == sourcePeriod.Quarter
                && x.Frequency == sourcePeriod.Frequency);

            if (exists)
            {
                continue;
            }

            db.ReturnPeriods.Add(new ReturnPeriod
            {
                TenantId = targetTenantId,
                ModuleId = sourcePeriod.ModuleId,
                Year = sourcePeriod.Year,
                Month = sourcePeriod.Month,
                Quarter = sourcePeriod.Quarter,
                Frequency = sourcePeriod.Frequency,
                ReportingDate = sourcePeriod.ReportingDate,
                IsOpen = sourcePeriod.IsOpen,
                CreatedAt = DateTime.UtcNow,
                DeadlineDate = sourcePeriod.DeadlineDate,
                DeadlineOverrideDate = sourcePeriod.DeadlineOverrideDate,
                DeadlineOverrideBy = sourcePeriod.DeadlineOverrideBy,
                DeadlineOverrideReason = sourcePeriod.DeadlineOverrideReason,
                Status = sourcePeriod.Status,
                NotificationLevel = sourcePeriod.NotificationLevel
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<ReturnDataRow> BuildSampleRows(
        Module module,
        StructuralCategory category,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        IReadOnlyList<Domain.Metadata.TemplateItemCode> itemCodes)
    {
        var rows = new List<ReturnDataRow>();
        var sortedFields = fields.OrderBy(x => x.FieldOrder).ToList();

        if (category == StructuralCategory.FixedRow)
        {
            rows.Add(BuildRow(module, sortedFields, 0, null));
            return rows;
        }

        if (category == StructuralCategory.MultiRow)
        {
            for (var i = 0; i < 3; i++)
            {
                rows.Add(BuildRow(module, sortedFields, i, null));
            }

            return rows;
        }

        var itemCodeValues = itemCodes
            .OrderBy(x => x.SortOrder)
            .Take(3)
            .Select(x => x.ItemCode)
            .DefaultIfEmpty("ITEM001")
            .ToList();

        var index = 0;
        foreach (var itemCode in itemCodeValues)
        {
            rows.Add(BuildRow(module, sortedFields, index, itemCode));
            index++;
        }

        return rows;
    }

    private static ReturnDataRow BuildRow(
        Module module,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        int rowIndex,
        string? itemCode)
    {
        var row = new ReturnDataRow();
        foreach (var field in fields)
        {
            var value = BuildSampleValue(module, field, rowIndex, itemCode);
            if (value is not null)
            {
                row.SetValue(field.FieldName, value);
            }
        }

        return row;
    }

    private static object? BuildSampleValue(Module module, Domain.Metadata.TemplateField field, int rowIndex, string? itemCode)
    {
        var fieldName = field.FieldName.ToLowerInvariant();

        if (field.IsKeyField && !string.IsNullOrWhiteSpace(itemCode))
        {
            return itemCode;
        }

        if (fieldName is "serial_no" or "serialnumber")
        {
            return rowIndex + 1;
        }

        return field.DataType switch
        {
            FieldDataType.Text => $"{module.ModuleCode}_{field.FieldName}_{rowIndex + 1}"[..Math.Min(40, $"{module.ModuleCode}_{field.FieldName}_{rowIndex + 1}".Length)],
            FieldDataType.Integer => (rowIndex + 1) * 10,
            FieldDataType.Money => 1_000_000m + ((rowIndex + 1) * 250_000m),
            FieldDataType.Decimal => 50_000m + ((rowIndex + 1) * 7_500m),
            FieldDataType.Percentage => ResolvePercentage(fieldName),
            FieldDataType.Date => DateTime.UtcNow.Date,
            FieldDataType.Boolean => true,
            _ => field.DefaultValue
        };
    }

    private static decimal ResolvePercentage(string fieldName)
    {
        if (fieldName.Contains("car", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("capital_adequacy", StringComparison.OrdinalIgnoreCase))
        {
            return 15.2m;
        }

        if (fieldName.Contains("npl", StringComparison.OrdinalIgnoreCase))
        {
            return 3.8m;
        }

        return 8.5m;
    }

    private static Dictionary<string, object?> BuildSummaryJson(
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        IReadOnlyList<ReturnDataRow> rows)
    {
        var summary = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var firstRow = rows.FirstOrDefault();
        if (firstRow is null)
        {
            return summary;
        }

        foreach (var field in fields.Where(x => x.DataType is FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage))
        {
            summary[field.FieldName] = firstRow.GetValue(field.FieldName);
        }

        return summary;
    }

    private async Task<Tenant?> ResolveProductionTenant(Tenant sandboxTenant, CancellationToken ct)
    {
        if (!sandboxTenant.TenantSlug.EndsWith("-sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var productionSlug = sandboxTenant.TenantSlug[..^"-sandbox".Length];
        return await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantSlug == productionSlug, ct);
    }

    private static string BuildSandboxSlug(string productionSlug)
    {
        var baseSlug = productionSlug.EndsWith("-sandbox", StringComparison.OrdinalIgnoreCase)
            ? productionSlug
            : $"{productionSlug}-sandbox";
        return baseSlug.Length > 95 ? baseSlug[..95] : baseSlug;
    }

    private static string BuildSandboxInstitutionCode(string productionCode)
    {
        var baseCode = string.IsNullOrWhiteSpace(productionCode) ? "INST" : productionCode.Trim().ToUpperInvariant();
        return baseCode.Length > 8 ? baseCode[..8] : $"{baseCode}SBX";
    }

    private static string BuildSandboxEmailAlias(string email, string suffix)
    {
        var parts = email.Split('@');
        if (parts.Length != 2)
        {
            return $"sandbox_{suffix}@regos.app";
        }

        var local = parts[0];
        var domain = parts[1];
        return $"{local}+{suffix}@{domain}";
    }

    private static string GenerateTemporaryPassword()
    {
        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);
        var token = Convert.ToBase64String(random).Replace('+', 'Q').Replace('/', 'Z').Replace("=", string.Empty);
        return $"Sb!{token[..10]}7";
    }

    public static string BuildPortalUrl(string tenantSlug, string path)
        => $"https://{tenantSlug}.regos.app{path}";
}

public class SandboxContext
{
    public bool IsSandbox { get; set; }
    public Guid CurrentTenantId { get; set; }
    public string CurrentTenantName { get; set; } = string.Empty;
    public Guid? ProductionTenantId { get; set; }
    public string? ProductionTenantName { get; set; }
    public string? ProductionLoginUrl { get; set; }
}

public class SandboxProvisionResult
{
    public bool Success { get; set; }
    public Guid SandboxTenantId { get; set; }
    public string SandboxTenantSlug { get; set; } = string.Empty;
    public string? LoginUrl { get; set; }
    public string? AdminTemporaryPassword { get; set; }
    public List<string> Errors { get; set; } = new();
}
