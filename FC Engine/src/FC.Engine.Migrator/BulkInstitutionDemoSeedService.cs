using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Dapper;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Persistence;
using FC.Engine.Infrastructure.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Migrator;

public sealed class BulkInstitutionDemoSeedService
{
    private const string InstitutionLoginUrl = "http://localhost:5300/login";
    private const string EnterprisePlanCode = "ENTERPRISE";
    private static readonly Regex FuncExpressionRegex = new(
        @"^FUNC:(?<name>[A-Z0-9_]+)\((?<args>.*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<SubmissionStatus> SeededStatuses =
    [
        SubmissionStatus.Accepted,
        SubmissionStatus.AcceptedWithWarnings,
        SubmissionStatus.Historical,
        SubmissionStatus.RegulatorAcknowledged,
        SubmissionStatus.RegulatorAccepted,
        SubmissionStatus.RegulatorQueriesRaised
    ];

    private static readonly HashSet<string> CoordinatedFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "BDC_FIN:fx_trading_income_basis",
        "BDC_CAP:total_assets",
        "BDC_FIN:total_assets"
    };

    private readonly MetadataDbContext _db;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DynamicSqlBuilder _sqlBuilder;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly IGenericXmlParser _xmlParser;
    private readonly InstitutionAuthService _institutionAuthService;
    private readonly IInstitutionUserRepository _institutionUserRepository;
    private readonly IMfaService _mfaService;
    private readonly DemoCredentialSeedService _demoCredentialSeedService;
    private readonly ExpressionParser _expressionParser = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _physicalTextLengthCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<BulkInstitutionDemoSeedService> _logger;

    public BulkInstitutionDemoSeedService(
        MetadataDbContext db,
        ITenantOnboardingService tenantOnboardingService,
        ISubscriptionService subscriptionService,
        ITemplateMetadataCache templateCache,
        ValidationOrchestrator validationOrchestrator,
        IDbConnectionFactory connectionFactory,
        DynamicSqlBuilder sqlBuilder,
        IXsdGenerator xsdGenerator,
        IGenericXmlParser xmlParser,
        InstitutionAuthService institutionAuthService,
        IInstitutionUserRepository institutionUserRepository,
        IMfaService mfaService,
        DemoCredentialSeedService demoCredentialSeedService,
        ILogger<BulkInstitutionDemoSeedService> logger)
    {
        _db = db;
        _tenantOnboardingService = tenantOnboardingService;
        _subscriptionService = subscriptionService;
        _templateCache = templateCache;
        _validationOrchestrator = validationOrchestrator;
        _connectionFactory = connectionFactory;
        _sqlBuilder = sqlBuilder;
        _xsdGenerator = xsdGenerator;
        _xmlParser = xmlParser;
        _institutionAuthService = institutionAuthService;
        _institutionUserRepository = institutionUserRepository;
        _mfaService = mfaService;
        _demoCredentialSeedService = demoCredentialSeedService;
        _logger = logger;
    }

    public async Task<BulkInstitutionDemoSeedResult> SeedAsync(
        string templatesDirectory,
        string sharedPassword,
        int bdcCount = 80,
        int dmbCount = 28,
        int fcCount = 0,
        int mfbCount = 0,
        int overlayInstitutionCountPerType = 0,
        CancellationToken ct = default)
    {
        var credentials = await _demoCredentialSeedService.SeedAsync(sharedPassword, ct);

        var allTemplates = await _templateCache.GetAllPublishedTemplates(ct);
        var bdcTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "BDC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var dmbTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "DMB", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var fcTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "FC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var mfbTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "MFB", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var nfiuTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "NFIU", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var capitalTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "CAPITAL", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var modelTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "MODEL", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();
        var opsTemplates = allTemplates
            .Where(x => string.Equals(x.InstitutionType, "OPS", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();

        EnsureTemplateExpectations("BDC", bdcTemplates, expectedCount: 12);
        EnsureTemplateExpectations("DMB", dmbTemplates, expectedCount: 15);
        if (fcCount > 0)
        {
            EnsureTemplateExpectations("FC", fcTemplates, expectedCount: 100);
        }
        if (mfbCount > 0 || overlayInstitutionCountPerType > 0)
        {
            EnsureTemplateExpectations("MFB", mfbTemplates, expectedCount: 12);
        }
        if (overlayInstitutionCountPerType > 0)
        {
            EnsureTemplateExpectations("NFIU", nfiuTemplates, expectedCount: 12);
            EnsureTemplateExpectations("CAPITAL", capitalTemplates, expectedCount: 6);
            EnsureTemplateExpectations("MODEL", modelTemplates, expectedCount: 9);
            EnsureTemplateExpectations("OPS", opsTemplates, expectedCount: 10);
        }

        var bdcModule = await ResolveSingleModuleAsync(bdcTemplates, "BDC_CBN", ct);
        var dmbModule = await ResolveSingleModuleAsync(dmbTemplates, "DMB_BASEL3", ct);
        Module? fcModule = null;
        Module? mfbModule = null;
        Module? nfiuModule = null;
        Module? capitalModule = null;
        Module? modelModule = null;
        Module? opsModule = null;
        if (fcCount > 0)
        {
            fcModule = await ResolveSingleModuleAsync(fcTemplates, "FC_RETURNS", ct);
        }
        if (mfbCount > 0 || overlayInstitutionCountPerType > 0)
        {
            mfbModule = await ResolveSingleModuleAsync(mfbTemplates, "MFB_PAR", ct);
        }
        if (overlayInstitutionCountPerType > 0)
        {
            nfiuModule = await ResolveSingleModuleAsync(nfiuTemplates, "NFIU_AML", ct);
            capitalModule = await ResolveSingleModuleAsync(capitalTemplates, "CAPITAL_SUPERVISION", ct);
            modelModule = await ResolveSingleModuleAsync(modelTemplates, "MODEL_RISK", ct);
            opsModule = await ResolveSingleModuleAsync(opsTemplates, "OPS_RESILIENCE", ct);
        }

        var bdcSamples = await LoadSampleXmlMapAsync(bdcTemplates, templatesDirectory, ct);
        var dmbSamples = await LoadSampleXmlMapAsync(dmbTemplates, templatesDirectory, ct);
        var fcSamples = fcCount > 0
            ? await LoadSampleXmlMapAsync(fcTemplates, templatesDirectory, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var mfbSamples = mfbCount > 0 || overlayInstitutionCountPerType > 0
            ? await LoadSampleXmlMapAsync(mfbTemplates, templatesDirectory, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nfiuSamples = overlayInstitutionCountPerType > 0
            ? await LoadSampleXmlMapAsync(nfiuTemplates, templatesDirectory, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var capitalSamples = overlayInstitutionCountPerType > 0
            ? await LoadSampleXmlMapAsync(capitalTemplates, templatesDirectory, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var modelSamples = overlayInstitutionCountPerType > 0
            ? await LoadSampleXmlMapAsync(modelTemplates, templatesDirectory, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var opsSamples = overlayInstitutionCountPerType > 0
            ? await LoadSampleXmlMapAsync(opsTemplates, templatesDirectory, ct)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bdcPeriodSpecs = BuildMonthlyPeriodSpecs(DateTime.UtcNow, count: 3);
        var dmbPeriodSpecs = BuildQuarterlyPeriodSpecs(DateTime.UtcNow, count: 3);
        var mfbPeriodSpecs = BuildMonthlyPeriodSpecs(DateTime.UtcNow, count: 3);
        var fcMonthlyPeriodSpecs = BuildMonthlyPeriodSpecs(DateTime.UtcNow, count: 3);
        var fcQuarterlyPeriodSpecs = BuildQuarterlyPeriodSpecs(DateTime.UtcNow, count: 3);
        var fcSemiAnnualPeriodSpecs = BuildSemiAnnualPeriodSpecs(DateTime.UtcNow, count: 3);
        var fcComputedPeriodSpecs = BuildComputedPeriodSpecs(DateTime.UtcNow, count: 3);
        var overlayMonthlyPeriodSpecs = BuildMonthlyPeriodSpecs(DateTime.UtcNow, count: 3);
        var overlayQuarterlyPeriodSpecs = BuildQuarterlyPeriodSpecs(DateTime.UtcNow, count: 3);

        var result = new BulkInstitutionDemoSeedResult
        {
            BdcInstitutionsProcessed = bdcCount,
            DmbInstitutionsProcessed = dmbCount,
            FcInstitutionsProcessed = fcCount,
            MfbInstitutionsProcessed = mfbCount,
            OverlayInstitutionsProcessed = overlayInstitutionCountPerType * 4,
            Credentials = credentials
        };

        var bdcPrototypeCache = new Dictionary<string, ValidatedSubmissionPrototype>(StringComparer.OrdinalIgnoreCase);
        var dmbPrototypeCache = new Dictionary<string, ValidatedSubmissionPrototype>(StringComparer.OrdinalIgnoreCase);
        var fcPrototypeCache = new Dictionary<string, ValidatedSubmissionPrototype>(StringComparer.OrdinalIgnoreCase);
        var mfbPrototypeCache = new Dictionary<string, ValidatedSubmissionPrototype>(StringComparer.OrdinalIgnoreCase);
        var overlayPrototypeCache = new Dictionary<string, ValidatedSubmissionPrototype>(StringComparer.OrdinalIgnoreCase);

        var bdcSpecs = BuildInstitutionSpecs("BDC", bdcCount);
        var dmbSpecs = BuildInstitutionSpecs("DMB", dmbCount);
        var fcSpecs = BuildInstitutionSpecs("FC", fcCount);
        var mfbSpecs = BuildInstitutionSpecs("MFB", mfbCount);

        var bdcBatches = new[]
        {
            new TemplateSeedBatch(bdcTemplates, bdcModule, bdcSamples, bdcPeriodSpecs)
        };
        var dmbBatches = new[]
        {
            new TemplateSeedBatch(dmbTemplates, dmbModule, dmbSamples, dmbPeriodSpecs)
        };
        var fcBatches = fcModule is null
            ? []
            : BuildFrequencyBatches(
                fcTemplates,
                fcModule,
                fcSamples,
                fcMonthlyPeriodSpecs,
                fcQuarterlyPeriodSpecs,
                fcSemiAnnualPeriodSpecs,
                fcComputedPeriodSpecs);
        var mfbBatches = mfbModule is null
            ? []
            : BuildFrequencyBatches(
                mfbTemplates,
                mfbModule,
                mfbSamples,
                mfbPeriodSpecs,
                quarterlyPeriodSpecs: [],
                semiAnnualPeriodSpecs: [],
                computedPeriodSpecs: []);
        var overlayBatches = overlayInstitutionCountPerType <= 0
            ? []
            : BuildFrequencyBatches(
                    nfiuTemplates,
                    nfiuModule!,
                    nfiuSamples,
                    overlayMonthlyPeriodSpecs,
                    overlayQuarterlyPeriodSpecs,
                    semiAnnualPeriodSpecs: [],
                    computedPeriodSpecs: [])
                .Concat(BuildFrequencyBatches(
                    capitalTemplates,
                    capitalModule!,
                    capitalSamples,
                    monthlyPeriodSpecs: [],
                    overlayQuarterlyPeriodSpecs,
                    semiAnnualPeriodSpecs: [],
                    computedPeriodSpecs: []))
                .Concat(BuildFrequencyBatches(
                    modelTemplates,
                    modelModule!,
                    modelSamples,
                    overlayMonthlyPeriodSpecs,
                    overlayQuarterlyPeriodSpecs,
                    semiAnnualPeriodSpecs: [],
                    computedPeriodSpecs: []))
                .Concat(BuildFrequencyBatches(
                    opsTemplates,
                    opsModule!,
                    opsSamples,
                    overlayMonthlyPeriodSpecs,
                    overlayQuarterlyPeriodSpecs,
                    semiAnnualPeriodSpecs: [],
                    computedPeriodSpecs: []))
                .ToArray();

        await ProcessInstitutionSetAsync(
            bdcSpecs,
            bdcBatches,
            bdcPrototypeCache,
            sharedPassword,
            result,
            ct);

        await ProcessInstitutionSetAsync(
            dmbSpecs,
            dmbBatches,
            dmbPrototypeCache,
            sharedPassword,
            result,
            ct);

        await ProcessInstitutionSetAsync(
            fcSpecs,
            fcBatches,
            fcPrototypeCache,
            sharedPassword,
            result,
            ct);

        await ProcessInstitutionSetAsync(
            mfbSpecs,
            mfbBatches,
            mfbPrototypeCache,
            sharedPassword,
            result,
            ct);

        if (overlayBatches.Length > 0)
        {
            var overlaySpecs = BuildInstitutionSpecs("BDC", overlayInstitutionCountPerType)
                .Concat(BuildInstitutionSpecs("DMB", overlayInstitutionCountPerType))
                .Concat(BuildInstitutionSpecs("FC", overlayInstitutionCountPerType))
                .Concat(BuildInstitutionSpecs("MFB", overlayInstitutionCountPerType))
                .ToList();

            await ProcessInstitutionSetAsync(
                overlaySpecs,
                overlayBatches,
                overlayPrototypeCache,
                sharedPassword,
                result,
                ct);
        }

        result.Credentials.GeneratedAtUtc = DateTimeOffset.UtcNow;
        result.Credentials.SharedPassword = sharedPassword;
        return result;
    }

    private async Task<InstitutionSeedExecutionResult> ProcessInstitutionAsync(
        InstitutionSeedSpec spec,
        int institutionSequence,
        IReadOnlyList<TemplateSeedBatch> batches,
        IDictionary<string, ValidatedSubmissionPrototype> prototypeCache,
        string sharedPassword,
        CancellationToken ct)
    {
        if (batches.Count == 0)
        {
            throw new InvalidOperationException($"No template seed batches were supplied for {spec.InstitutionCode}.");
        }

        var ensured = await EnsureInstitutionAsync(spec, batches[0].Module.ModuleCode, sharedPassword, ct);
        var ensuredPeriods = new List<(TemplateSeedBatch Batch, EnsuredPeriodsResult Result)>(batches.Count);
        foreach (var batch in batches)
        {
            ensuredPeriods.Add((
                batch,
                await EnsurePeriodsAsync(ensured.Institution.TenantId, batch.Module, batch.PeriodSpecs, ct)));
        }

        var allTemplates = batches
            .SelectMany(x => x.Templates)
            .DistinctBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allReturnPeriodIds = ensuredPeriods
            .SelectMany(x => x.Result.Periods)
            .Select(x => x.Id)
            .Distinct()
            .ToList();

        await CleanupInstitutionSeedDataAsync(
            ensured.Institution,
            allTemplates,
            allReturnPeriodIds,
            ct);
        var existingSubmissionKeys = await LoadExistingSubmissionKeysAsync(
            ensured.Institution,
            allTemplates.Select(x => x.ReturnCode).ToList(),
            allReturnPeriodIds,
            ct);

        var submissionsCreated = 0;
        var createdSubmissions = new List<SeededSubmissionArtifact>();
        foreach (var batchPeriods in ensuredPeriods)
        {
            var periodsByKey = batchPeriods.Result.Periods.ToDictionary(
                x => BuildPeriodLookupKey(x.Year, x.Month, x.Frequency),
                StringComparer.OrdinalIgnoreCase);

            foreach (var periodSpec in batchPeriods.Batch.PeriodSpecs)
            {
                var period = periodsByKey[BuildPeriodLookupKey(periodSpec.Year, periodSpec.Month, periodSpec.Frequency)];
                for (var templateIndex = 0; templateIndex < batchPeriods.Batch.Templates.Count; templateIndex++)
                {
                    var template = batchPeriods.Batch.Templates[templateIndex];
                    var submissionKey = $"{template.ReturnCode}:{period.Id}";
                    if (existingSubmissionKeys.Contains(submissionKey))
                    {
                        continue;
                    }

                    var disposition = ResolveSubmissionDisposition(
                        spec,
                        periodSpec,
                        templateIndex,
                        batchPeriods.Batch.Templates.Count,
                        institutionSequence);
                    if (disposition == SubmissionSeedDisposition.Missing)
                    {
                        continue;
                    }

                    var prototypeKey = $"{template.ReturnCode}:{period.ReportingDate:yyyyMMdd}";
                    if (!prototypeCache.TryGetValue(prototypeKey, out var prototype))
                    {
                        batchPeriods.Batch.SampleXmlMap.TryGetValue(template.ReturnCode, out var sampleXml);
                        prototype = await BuildValidatedPrototypeAsync(
                            template,
                            sampleXml,
                            ensured.Institution,
                            spec,
                            period,
                            ct);
                        prototypeCache[prototypeKey] = prototype;
                    }

                    var record = CloneRecord(prototype.Record);
                    NormalizeRecord(template, record, ensured.Institution, spec, period.ReportingDate.Date, institutionSequence);
                    var xml = BuildXml(template, record, ensured.Institution.InstitutionCode, period.ReportingDate.Date);

                    var submittedAt = periodSpec.SubmittedAt.AddMinutes(institutionSequence);
                    var submissionArtifact = await CreateSubmissionAsync(
                        template,
                        prototype,
                        record,
                        xml,
                        ensured.Institution,
                        ensured.AdminUser.Id,
                        period.Id,
                        submittedAt,
                        periodSpec,
                        disposition,
                        ct);

                    submissionsCreated++;
                    createdSubmissions.Add(submissionArtifact);
                    existingSubmissionKeys.Add(submissionKey);
                }
            }
        }

        if (createdSubmissions.Count > 0)
        {
            await UpdateInstitutionSubmissionStampAsync(
                ensured.Institution.Id,
                createdSubmissions.Max(x => x.SubmittedAt),
                ct);
        }

        if (ShouldRefreshInstitutionSignalData(batches))
        {
            await RefreshInstitutionSignalDataAsync(
                ensured.Institution,
                spec,
                batches,
                createdSubmissions,
                ct);
        }

        var credentialGroup = BuildCredentialGroup(
            ensured.Institution,
            ensured.AdminUser,
            allTemplates.Count,
            batches.SelectMany(x => x.PeriodSpecs)
                .Select(x => BuildPeriodLookupKey(x.Year, x.Month, x.Frequency))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            sharedPassword);
        return new InstitutionSeedExecutionResult(
            ensured.InstitutionCreated,
            ensuredPeriods.Sum(x => x.Result.CreatedCount),
            submissionsCreated,
            credentialGroup);
    }

    private async Task ProcessInstitutionSetAsync(
        IReadOnlyList<InstitutionSeedSpec> specs,
        IReadOnlyList<TemplateSeedBatch> batches,
        IDictionary<string, ValidatedSubmissionPrototype> prototypeCache,
        string sharedPassword,
        BulkInstitutionDemoSeedResult aggregateResult,
        CancellationToken ct)
    {
        if (specs.Count == 0 || batches.Count == 0)
        {
            return;
        }

        for (var index = 0; index < specs.Count; index++)
        {
            var seedResult = await ProcessInstitutionAsync(
                specs[index],
                index,
                batches,
                prototypeCache,
                sharedPassword,
                ct);

            aggregateResult.InstitutionsCreated += seedResult.InstitutionCreated ? 1 : 0;
            aggregateResult.PeriodsCreated += seedResult.PeriodsCreated;
            aggregateResult.SubmissionsCreated += seedResult.SubmissionsCreated;
            UpsertCredentialGroup(aggregateResult.Credentials, seedResult.CredentialGroup);
        }
    }

    private async Task<EnsuredInstitutionResult> EnsureInstitutionAsync(
        InstitutionSeedSpec spec,
        string moduleCode,
        string sharedPassword,
        CancellationToken ct)
    {
        var institution = await _db.Institutions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.InstitutionCode == spec.InstitutionCode, ct);

        var institutionCreated = false;
        if (institution is null)
        {
            var onboarding = await _tenantOnboardingService.OnboardTenant(new TenantOnboardingRequest
            {
                TenantName = spec.InstitutionName,
                TenantSlug = spec.TenantSlug,
                TenantType = TenantType.Institution,
                ContactEmail = spec.ContactEmail,
                ContactPhone = spec.ContactPhone,
                Address = spec.Address,
                RcNumber = $"{spec.InstitutionCode}-RC",
                TaxId = $"{spec.InstitutionCode}-TIN",
                LicenceTypeCodes = [spec.LicenceTypeCode],
                SubscriptionPlanCode = EnterprisePlanCode,
                AdminEmail = spec.AdminEmail,
                AdminFullName = spec.AdminDisplayName,
                AdminPhone = spec.ContactPhone,
                InstitutionCode = spec.InstitutionCode,
                InstitutionName = spec.InstitutionName,
                InstitutionType = spec.LicenceTypeCode,
                JurisdictionCode = "NG"
            }, ct);

            if (!onboarding.Success)
            {
                throw new InvalidOperationException(
                    $"Unable to onboard {spec.InstitutionCode}: {string.Join("; ", onboarding.Errors)}");
            }

            await ClearTenantSessionContextAsync(ct);
            _db.ChangeTracker.Clear();

            institution = await _db.Institutions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == onboarding.InstitutionId, ct)
                ?? throw new InvalidOperationException($"Onboarded institution {spec.InstitutionCode} was not found.");
            institutionCreated = true;
        }

        await UpsertInstitutionProfileAsync(institution.Id, spec, ct);
        await EnsureEnterpriseSubscriptionAndModuleAsync(institution.TenantId, moduleCode, ct);

        var adminUser = await EnsureInstitutionAdminAsync(institution, spec, sharedPassword, ct);
        institution = await _db.Institutions
            .AsNoTracking()
            .FirstAsync(x => x.Id == institution.Id, ct);

        return new EnsuredInstitutionResult(institution, adminUser, institutionCreated);
    }

    private async Task UpsertInstitutionProfileAsync(int institutionId, InstitutionSeedSpec spec, CancellationToken ct)
    {
        var tracked = await _db.Institutions.FirstAsync(x => x.Id == institutionId, ct);
        tracked.IsActive = true;
        tracked.InstitutionName = spec.InstitutionName;
        tracked.ContactEmail = spec.ContactEmail;
        tracked.ContactPhone = spec.ContactPhone;
        tracked.Address = spec.Address;
        tracked.Location = $"{spec.City}, {spec.State}";
        tracked.LicenseType = spec.LicenceTypeCode;
        tracked.SubscriptionTier = EnterprisePlanCode;
        tracked.MakerCheckerEnabled = false;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task EnsureEnterpriseSubscriptionAndModuleAsync(Guid tenantId, string moduleCode, CancellationToken ct)
    {
        var hasActiveSubscription = await _db.Subscriptions
            .AsNoTracking()
            .AnyAsync(
                x => x.TenantId == tenantId
                     && x.Status != SubscriptionStatus.Cancelled
                     && x.Status != SubscriptionStatus.Expired,
                ct);

        if (!hasActiveSubscription)
        {
            await _subscriptionService.CreateSubscription(tenantId, EnterprisePlanCode, BillingFrequency.Monthly, ct);
        }

        try
        {
            await _subscriptionService.ActivateModule(tenantId, moduleCode, ct);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("already active", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not eligible", StringComparison.OrdinalIgnoreCase) == false
               && ex.Message.Contains("not available", StringComparison.OrdinalIgnoreCase) == false)
        {
            if (!ex.Message.Contains("already active", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }
        }
    }

    private async Task<InstitutionUser> EnsureInstitutionAdminAsync(
        Institution institution,
        InstitutionSeedSpec spec,
        string sharedPassword,
        CancellationToken ct)
    {
        var user = await _institutionUserRepository.GetByUsername(spec.AdminUsername, ct);
        if (user is null)
        {
            user = await _institutionAuthService.CreateUser(
                institution.Id,
                spec.AdminUsername,
                spec.AdminEmail,
                spec.AdminDisplayName,
                sharedPassword,
                InstitutionRole.Admin,
                ct);
        }
        else if (user.InstitutionId != institution.Id)
        {
            throw new InvalidOperationException(
                $"Username {spec.AdminUsername} belongs to institution {user.InstitutionId}, expected {institution.Id}.");
        }
        else
        {
            await _institutionAuthService.ResetPassword(user.Id, sharedPassword, ct);
            user = await _institutionUserRepository.GetById(user.Id, ct)
                   ?? throw new InvalidOperationException($"Institution user {spec.AdminUsername} disappeared after password reset.");
        }

        user.TenantId = institution.TenantId;
        user.InstitutionId = institution.Id;
        user.Email = spec.AdminEmail;
        user.DisplayName = spec.AdminDisplayName;
        user.Role = InstitutionRole.Admin;
        user.IsActive = true;
        user.MustChangePassword = false;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        user.PermissionOverridesJson = null;
        user.PreferredLanguage = "en";
        await _institutionUserRepository.Update(user, ct);
        await _mfaService.Disable(user.Id, "InstitutionUser");

        return user;
    }

    private static void UpsertCredentialGroup(DemoCredentialSeedResult credentials, DemoCredentialGroup group)
    {
        var existingIndex = credentials.InstitutionGroups.FindIndex(x =>
            string.Equals(x.InstitutionCode, group.InstitutionCode, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            var existing = credentials.InstitutionGroups[existingIndex];
            foreach (var account in group.Accounts)
            {
                if (existing.Accounts.Any(x =>
                        string.Equals(x.Username, account.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                existing.Accounts.Add(account);
            }

            existing.Notes = string.Equals(existing.Notes, group.Notes, StringComparison.OrdinalIgnoreCase)
                ? existing.Notes
                : "Bulk demo tenant seeded across multiple RegOS demo packs, covering core filings and overlay workflows.";
            return;
        }

        credentials.InstitutionGroups.Add(group);
    }

    private async Task<EnsuredPeriodsResult> EnsurePeriodsAsync(
        Guid tenantId,
        Module module,
        IReadOnlyList<DemoPeriodSpec> specs,
        CancellationToken ct)
    {
        var periods = await _db.ReturnPeriods
            .Where(x => x.TenantId == tenantId && x.ModuleId == module.Id)
            .ToListAsync(ct);

        var createdCount = 0;
        foreach (var spec in specs)
        {
            var existing = periods.FirstOrDefault(
                x => x.Year == spec.Year
                     && x.Month == spec.Month
                     && string.Equals(x.Frequency, spec.Frequency, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new ReturnPeriod
                {
                    TenantId = tenantId,
                    ModuleId = module.Id,
                    Year = spec.Year,
                    Month = spec.Month,
                    Quarter = spec.Quarter,
                    Frequency = spec.Frequency,
                    ReportingDate = spec.ReportingDate,
                    DeadlineDate = spec.ReportingDate.AddDays(ResolveDeadlineOffsetDays(module, spec.Frequency)),
                    CreatedAt = DateTime.UtcNow,
                    IsOpen = spec.IsCurrent,
                    Status = spec.IsCurrent ? "Open" : "Completed",
                    NotificationLevel = 0
                };

                _db.ReturnPeriods.Add(existing);
                periods.Add(existing);
                createdCount++;
                continue;
            }

            existing.ModuleId = module.Id;
            existing.Quarter = spec.Quarter;
            existing.Frequency = spec.Frequency;
            existing.ReportingDate = spec.ReportingDate;
            existing.DeadlineDate = spec.ReportingDate.AddDays(ResolveDeadlineOffsetDays(module, spec.Frequency));
            existing.IsOpen = spec.IsCurrent;
            existing.Status = spec.IsCurrent ? "Open" : "Completed";
            existing.NotificationLevel = 0;
        }

        await _db.SaveChangesAsync(ct);

        var materialized = periods
            .Where(x => specs.Any(
                spec => spec.Year == x.Year
                        && spec.Month == x.Month
                        && string.Equals(spec.Frequency, x.Frequency, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.ReportingDate)
            .ToList();

        _db.ChangeTracker.Clear();
        return new EnsuredPeriodsResult(materialized, createdCount);
    }

    private async Task<HashSet<string>> LoadExistingSubmissionKeysAsync(
        Institution institution,
        IReadOnlyList<string> returnCodes,
        IReadOnlyList<int> returnPeriodIds,
        CancellationToken ct)
    {
        if (returnCodes.Count == 0 || returnPeriodIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var existing = await _db.Submissions
            .AsNoTracking()
            .Where(x => x.TenantId == institution.TenantId && x.InstitutionId == institution.Id)
            .Where(x => returnCodes.Contains(x.ReturnCode) && returnPeriodIds.Contains(x.ReturnPeriodId))
            .Where(x => SeededStatuses.Contains(x.Status))
            .Select(x => $"{x.ReturnCode}:{x.ReturnPeriodId}")
            .ToListAsync(ct);

        return existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task CleanupInstitutionSeedDataAsync(
        Institution institution,
        IReadOnlyList<CachedTemplate> templates,
        IReadOnlyList<int> returnPeriodIds,
        CancellationToken ct)
    {
        if (returnPeriodIds.Count == 0)
        {
            return;
        }

        var submissions = await _db.Submissions
            .AsNoTracking()
            .Where(x => x.TenantId == institution.TenantId && x.InstitutionId == institution.Id)
            .Where(x => returnPeriodIds.Contains(x.ReturnPeriodId))
            .Select(x => new { x.Id, x.ReturnCode })
            .ToListAsync(ct);

        if (submissions.Count == 0)
        {
            return;
        }

        var templateMap = templates.ToDictionary(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase);
        using var connection = await _connectionFactory.CreateConnectionAsync(institution.TenantId, ct);

        foreach (var group in submissions.GroupBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase))
        {
            if (!templateMap.TryGetValue(group.Key, out var template))
            {
                continue;
            }

            var submissionIds = group.Select(x => x.Id).ToArray();
            await connection.ExecuteAsync(
                new CommandDefinition(
                    $"DELETE FROM dbo.[{template.PhysicalTableName}] WHERE submission_id IN @SubmissionIds AND TenantId = @TenantId;",
                    new { SubmissionIds = submissionIds, TenantId = institution.TenantId },
                    cancellationToken: ct));
        }

        var submissionIdList = submissions.Select(x => x.Id).ToList();
        var validationReportIds = _db.ValidationReports
            .Where(x => submissionIdList.Contains(x.SubmissionId))
            .Select(x => x.Id);

        await _db.ValidationErrors
            .Where(x => validationReportIds.Contains(x.ValidationReportId))
            .ExecuteDeleteAsync(ct);

        await _db.ValidationReports
            .Where(x => submissionIdList.Contains(x.SubmissionId))
            .ExecuteDeleteAsync(ct);

        await _db.SubmissionApprovals
            .Where(x => submissionIdList.Contains(x.SubmissionId))
            .ExecuteDeleteAsync(ct);

        await _db.ReturnPeriods
            .Where(x => x.AutoCreatedReturnId.HasValue && submissionIdList.Contains(x.AutoCreatedReturnId.Value))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.AutoCreatedReturnId, (int?)null),
                ct);

        await _db.Submissions
            .Where(x => submissionIdList.Contains(x.Id))
            .ExecuteDeleteAsync(ct);

        _db.ChangeTracker.Clear();
    }

    private async Task<ValidatedSubmissionPrototype> BuildValidatedPrototypeAsync(
        CachedTemplate template,
        string? sampleXml,
        Institution institution,
        InstitutionSeedSpec spec,
        ReturnPeriod period,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sampleXml))
        {
            var record = await ParseXmlAsync(template.ReturnCode, sampleXml, ct);
            NormalizeRecord(template, record, institution, spec, period.ReportingDate.Date, institutionSequence: 0);

            var xml = BuildXml(template, record, institution.InstitutionCode, period.ReportingDate.Date);
            var xsdErrors = await ValidateXsdAsync(template.ReturnCode, xml, ct);
            if (xsdErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Sample XML for {template.ReturnCode} failed XSD validation: {string.Join("; ", xsdErrors.Select(x => x.Message))}");
            }

            var report = await ValidateRecordAsync(record, template.ReturnCode, institution.Id, period.Id, ct);
            if (report.HasErrors)
            {
                throw new InvalidOperationException(
                    $"Sample XML for {template.ReturnCode} failed business validation: {FormatValidationErrors(report.Errors)}");
            }

            return new ValidatedSubmissionPrototype(
                CloneRecord(record),
                report.Errors.Select(CloneValidationError).ToList());
        }

        return await BuildGeneratedPrototypeAsync(template, institution, spec, period, ct);
    }

    private async Task<ValidatedSubmissionPrototype> BuildGeneratedPrototypeAsync(
        CachedTemplate template,
        Institution institution,
        InstitutionSeedSpec spec,
        ReturnPeriod period,
        CancellationToken ct)
    {
        var record = BuildGeneratedRecord(template, institution, spec, period.ReportingDate.Date);

        ValidationReport? finalReport = null;
        ReturnDataRecord? finalRecord = null;
        string? finalXml = null;

        for (var iteration = 0; iteration < 12; iteration++)
        {
            ApplyFormulaTargets(template, record);
            CoerceRecordValues(template, record, period.ReportingDate.Date);
            NormalizeRecord(template, record, institution, spec, period.ReportingDate.Date, institutionSequence: 0);

            var xml = BuildXml(template, record, institution.InstitutionCode, period.ReportingDate.Date);
            var xsdErrors = await ValidateXsdAsync(template.ReturnCode, xml, ct);
            if (xsdErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Generated XML for {template.ReturnCode} failed XSD validation: {string.Join("; ", xsdErrors.Select(x => x.Message))}");
            }

            var parsedRecord = await ParseXmlAsync(template.ReturnCode, xml, ct);
            var report = await ValidateRecordAsync(parsedRecord, template.ReturnCode, institution.Id, period.Id, ct);
            if (!report.HasErrors)
            {
                finalXml = xml;
                finalRecord = parsedRecord;
                finalReport = report;
                break;
            }

            RepairValidationErrors(template, record, report.Errors, period.ReportingDate.Date);
        }

        if (finalXml is null || finalRecord is null || finalReport is null)
        {
            var report = await ValidateRecordAsync(record, template.ReturnCode, institution.Id, period.Id, ct);
            throw new InvalidOperationException(
                $"Unable to generate a valid sample for {template.ReturnCode}: {FormatValidationErrors(report.Errors)}");
        }

        return new ValidatedSubmissionPrototype(
            CloneRecord(finalRecord),
            finalReport.Errors.Select(CloneValidationError).ToList());
    }

    private ReturnDataRecord BuildGeneratedRecord(
        CachedTemplate template,
        Institution institution,
        InstitutionSeedSpec spec,
        DateTime reportingDate)
    {
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        var record = new ReturnDataRecord(template.ReturnCode, template.CurrentVersion.Id, category);

        if (category == StructuralCategory.FixedRow)
        {
            record.AddRow(BuildGeneratedRow(template, institution, spec, reportingDate, rowIndex: 0, itemCode: null));
            return record;
        }

        if (category == StructuralCategory.ItemCoded && template.CurrentVersion.ItemCodes.Count > 0)
        {
            foreach (var itemCode in template.CurrentVersion.ItemCodes.OrderBy(x => x.SortOrder))
            {
                record.AddRow(BuildGeneratedRow(
                    template,
                    institution,
                    spec,
                    reportingDate,
                    rowIndex: record.Rows.Count,
                    itemCode));
            }

            return record;
        }

        const int defaultRowCount = 3;
        for (var rowIndex = 0; rowIndex < defaultRowCount; rowIndex++)
        {
            record.AddRow(BuildGeneratedRow(template, institution, spec, reportingDate, rowIndex, itemCode: null));
        }

        return record;
    }

    private ReturnDataRow BuildGeneratedRow(
        CachedTemplate template,
        Institution institution,
        InstitutionSeedSpec spec,
        DateTime reportingDate,
        int rowIndex,
        TemplateItemCode? itemCode)
    {
        var row = new ReturnDataRow();
        foreach (var field in template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder))
        {
            var value = BuildInitialValue(template.ReturnCode, field, reportingDate, rowIndex, institution, spec, itemCode);
            if (value is not null)
            {
                row.SetValue(field.FieldName, value);
            }
        }

        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        if (category == StructuralCategory.MultiRow)
        {
            row.RowKey = (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
        }
        else if (category == StructuralCategory.ItemCoded)
        {
            row.RowKey = ResolveItemCodeRaw(itemCode, rowIndex);
        }

        return row;
    }

    private object? BuildInitialValue(
        string returnCode,
        TemplateField field,
        DateTime reportingDate,
        int rowIndex,
        Institution institution,
        InstitutionSeedSpec spec,
        TemplateItemCode? itemCode)
    {
        var fieldName = field.FieldName.Trim();
        var normalized = fieldName.ToLowerInvariant();

        if (normalized is "serial_no" or "serialno" or "s_no")
        {
            return rowIndex + 1;
        }

        if (normalized is "item_code" or "itemcode")
        {
            return ResolveItemCodeValue(field, itemCode, rowIndex);
        }

        if (normalized.Contains("item_description", StringComparison.OrdinalIgnoreCase) && itemCode is not null)
        {
            return CoerceText(field, itemCode.ItemDescription);
        }

        if (normalized is "reporting_year")
        {
            return reportingDate.Year;
        }

        if (normalized is "reporting_month")
        {
            return reportingDate.Month;
        }

        if (normalized is "reporting_quarter")
        {
            return ((reportingDate.Month - 1) / 3) + 1;
        }

        if (normalized is "return_code" or "returncode")
        {
            return returnCode;
        }

        if (normalized is "institution_code" or "institutioncode")
        {
            return CoerceText(field, institution.InstitutionCode);
        }

        if (normalized.Contains("institution", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("name", StringComparison.OrdinalIgnoreCase))
        {
            return CoerceText(field, institution.InstitutionName);
        }

        if (string.Equals(returnCode, "BDC_CAP", StringComparison.OrdinalIgnoreCase))
        {
            var value = BuildBdcCapitalValue(field, normalized, institution.InstitutionCode);
            if (value is not null)
            {
                return value;
            }
        }

        if (string.Equals(returnCode, "BDC_FIN", StringComparison.OrdinalIgnoreCase))
        {
            var value = BuildBdcFinancialValue(normalized);
            if (value is not null)
            {
                return value;
            }
        }

        if (string.Equals(returnCode, "BDC_FXV", StringComparison.OrdinalIgnoreCase))
        {
            var value = BuildBdcFxValue(normalized);
            if (value is not null)
            {
                return value;
            }
        }

        if (string.Equals(returnCode, "BDC_BRN", StringComparison.OrdinalIgnoreCase))
        {
            var value = BuildBdcBranchValue(normalized, rowIndex);
            if (value is not null)
            {
                return value;
            }
        }

        if (string.Equals(returnCode, "MFB_CAP", StringComparison.OrdinalIgnoreCase))
        {
            var value = BuildMfbCapitalValue(field, normalized, institution.InstitutionCode);
            if (value is not null)
            {
                return value;
            }
        }

        if (IsFinanceCompanyReturnCode(returnCode))
        {
            if (field.DataType == FieldDataType.Integer)
            {
                return 0;
            }

            if (field.DataType is FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage)
            {
                return BuildFinanceCompanyNumericBaseline(returnCode, normalized, rowIndex);
            }
        }

        var allowed = ParseAllowedValues(field.AllowedValues);
        if (allowed.Count > 0)
        {
            return ConvertStringValue(field, allowed[0]);
        }

        if (!string.IsNullOrWhiteSpace(field.DefaultValue))
        {
            return ConvertStringValue(field, field.DefaultValue);
        }

        return field.DataType switch
            {
            FieldDataType.Integer => BuildInitialIntegerValue(field, reportingDate, rowIndex),
            FieldDataType.Money => BuildInitialDecimalValue(returnCode, field, rowIndex, scale: 1_000m),
            FieldDataType.Decimal => BuildInitialDecimalValue(returnCode, field, rowIndex, scale: 100m),
            FieldDataType.Percentage => BuildInitialPercentageValue(field, rowIndex),
            FieldDataType.Date => reportingDate.Date,
            FieldDataType.Boolean => true,
            FieldDataType.Text => BuildInitialTextValue(field, returnCode, rowIndex, institution, spec, itemCode),
            _ => null
        };
    }

    private static int BuildInitialIntegerValue(TemplateField field, DateTime reportingDate, int sampleIndex)
    {
        var normalized = field.FieldName.ToLowerInvariant();
        if (normalized.Contains("count", StringComparison.OrdinalIgnoreCase))
        {
            return 3 + sampleIndex;
        }

        if (normalized.Contains("number", StringComparison.OrdinalIgnoreCase))
        {
            return 10 + sampleIndex;
        }

        if (normalized.Contains("year", StringComparison.OrdinalIgnoreCase))
        {
            return reportingDate.Year;
        }

        if (normalized.Contains("month", StringComparison.OrdinalIgnoreCase))
        {
            return reportingDate.Month;
        }

        if (normalized.Contains("quarter", StringComparison.OrdinalIgnoreCase))
        {
            return ((reportingDate.Month - 1) / 3) + 1;
        }

        return 10 + sampleIndex;
    }

    private static decimal BuildInitialDecimalValue(string returnCode, TemplateField field, int sampleIndex, decimal scale)
    {
        var normalized = field.FieldName.ToLowerInvariant();

        if (normalized.Contains("minimum", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("limit", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("threshold", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveThresholdValue(normalized);
        }

        if (normalized.Contains("assets", StringComparison.OrdinalIgnoreCase))
        {
            return 4_500_000m + (sampleIndex * 50_000m);
        }

        if (normalized.Contains("liabilities", StringComparison.OrdinalIgnoreCase))
        {
            return 3_200_000m + (sampleIndex * 45_000m);
        }

        if (normalized.Contains("capital", StringComparison.OrdinalIgnoreCase))
        {
            return 850_000m + (sampleIndex * 25_000m);
        }

        if (normalized.Contains("loan", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("credit", StringComparison.OrdinalIgnoreCase))
        {
            return 1_600_000m + (sampleIndex * 30_000m);
        }

        if (normalized.Contains("deposit", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("borrowing", StringComparison.OrdinalIgnoreCase))
        {
            return 1_250_000m + (sampleIndex * 28_000m);
        }

        if (normalized.Contains("income", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("revenue", StringComparison.OrdinalIgnoreCase))
        {
            return 275_000m + (sampleIndex * 9_500m);
        }

        if (normalized.Contains("expense", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("loss", StringComparison.OrdinalIgnoreCase))
        {
            return 95_000m + (sampleIndex * 4_250m);
        }

        return scale + (sampleIndex * (scale / 5m));
    }

    // Finance-company demo data defaults to zero so cross-sheet rules only light up where we
    // intentionally seed a non-zero anchor and its downstream schedule breakdowns.
    private static decimal BuildFinanceCompanyNumericBaseline(
        string returnCode,
        string normalizedFieldName,
        int sampleIndex)
    {
        if (string.Equals(returnCode, "MFCR 1000", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFieldName switch
            {
                "other_comprehensive_income" or "total_comprehensive_income" => 181_000m,
                "other_comprehensive_income_ytd" or "total_comprehensive_income_ytd" => 181_000m,
                _ => 0m
            };
        }

        if (string.Equals(returnCode, "MFCR 300", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFieldName switch
            {
                "paid_up_capital" or "other_assets" => 1_000m,
                _ => 0m
            };
        }

        if (string.Equals(returnCode, "MFCR 356", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFieldName switch
            {
                "impairment_naira" or "total" => sampleIndex == 0 ? 1_000m : 0m,
                _ => 0m
            };
        }

        if (string.Equals(returnCode, "MFCR 357", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFieldName switch
            {
                "amount_naira" or "total" => sampleIndex == 0 ? 1_000m : 0m,
                _ => 0m
            };
        }

        return 0m;
    }

    private static decimal BuildInitialPercentageValue(TemplateField field, int sampleIndex)
    {
        var normalized = field.FieldName.ToLowerInvariant();

        if (normalized.Contains("ratio", StringComparison.OrdinalIgnoreCase))
        {
            return 18m + sampleIndex;
        }

        if (normalized.Contains("rate", StringComparison.OrdinalIgnoreCase))
        {
            return 9.5m + sampleIndex;
        }

        if (normalized.Contains("car", StringComparison.OrdinalIgnoreCase))
        {
            return 15m;
        }

        return 10m + sampleIndex;
    }

    private static string BuildInitialTextValue(
        TemplateField field,
        string returnCode,
        int sampleIndex,
        Institution institution,
        InstitutionSeedSpec spec,
        TemplateItemCode? itemCode)
    {
        var normalized = field.FieldName.ToLowerInvariant();
        var raw = normalized switch
        {
            var s when s.Contains("institution", StringComparison.OrdinalIgnoreCase) && s.Contains("code", StringComparison.OrdinalIgnoreCase) => institution.InstitutionCode,
            var s when s.Contains("institution", StringComparison.OrdinalIgnoreCase) && s.Contains("name", StringComparison.OrdinalIgnoreCase) => institution.InstitutionName,
            var s when s.Contains("name", StringComparison.OrdinalIgnoreCase) => institution.InstitutionName,
            var s when s.Contains("email", StringComparison.OrdinalIgnoreCase) => spec.ContactEmail,
            var s when s.Contains("phone", StringComparison.OrdinalIgnoreCase) => spec.ContactPhone,
            var s when s.Contains("address", StringComparison.OrdinalIgnoreCase) => spec.Address,
            var s when s.Contains("currency", StringComparison.OrdinalIgnoreCase) => "NGN",
            var s when s.Contains("country", StringComparison.OrdinalIgnoreCase) => "NG",
            var s when s.Contains("city", StringComparison.OrdinalIgnoreCase) => spec.City,
            var s when s.Contains("state", StringComparison.OrdinalIgnoreCase) => spec.State,
            var s when s.Contains("branch", StringComparison.OrdinalIgnoreCase) => $"{institution.InstitutionCode} Branch {sampleIndex + 1}",
            var s when s.Contains("item_description", StringComparison.OrdinalIgnoreCase) && itemCode is not null => itemCode.ItemDescription,
            _ => $"{returnCode}_{field.FieldName}_{sampleIndex + 1}"
        };

        var maxLength = field.MaxLength.GetValueOrDefault(40);
        return raw.Length <= maxLength ? raw : raw[..maxLength];
    }

    private static bool IsFinanceCompanyReturnCode(string returnCode)
        => returnCode.StartsWith("MFCR", StringComparison.OrdinalIgnoreCase)
           || returnCode.StartsWith("FC ", StringComparison.OrdinalIgnoreCase)
           || returnCode.Equals("CONSOL", StringComparison.OrdinalIgnoreCase)
           || returnCode.Equals("REPORTS", StringComparison.OrdinalIgnoreCase)
           || returnCode.Equals("SHEET3", StringComparison.OrdinalIgnoreCase);

    private static object? BuildBdcCapitalValue(
        TemplateField field,
        string normalizedFieldName,
        string institutionCode)
    {
        var categoryCode = ResolveInstitutionCategoryCode(institutionCode, maxCategory: 2);
        var minimumCapital = categoryCode == 2 ? 2_000_000m : 35_000_000m;
        var shareholdersFunds = decimal.Round(minimumCapital * 1.37m, 2, MidpointRounding.AwayFromZero);

        return normalizedFieldName switch
        {
            "licence_category_code" or "license_category_code" => categoryCode,
            "category_a_minimum_capital" => 35_000_000m,
            "category_b_minimum_capital" => 2_000_000m,
            "paid_up_capital" => decimal.Round(minimumCapital * 1.12m, 2, MidpointRounding.AwayFromZero),
            "retained_earnings" => decimal.Round(minimumCapital * 0.12m, 2, MidpointRounding.AwayFromZero),
            "statutory_reserves" => decimal.Round(minimumCapital * 0.08m, 2, MidpointRounding.AwayFromZero),
            "other_reserves" => decimal.Round(minimumCapital * 0.05m, 2, MidpointRounding.AwayFromZero),
            "total_assets" => decimal.Round(minimumCapital * 9.8m, 2, MidpointRounding.AwayFromZero),
            "total_liabilities" => decimal.Round((minimumCapital * 9.8m) - shareholdersFunds, 2, MidpointRounding.AwayFromZero),
            "capital_buffer" => decimal.Round(shareholdersFunds - minimumCapital, 2, MidpointRounding.AwayFromZero),
            "capital_requirement_met" or "capital_requirement_met_flag" => 1,
            _ => null
        };
    }

    private static object? BuildMfbCapitalValue(
        TemplateField field,
        string normalizedFieldName,
        string institutionCode)
    {
        var categoryCode = ResolveInstitutionCategoryCode(institutionCode, maxCategory: 3);
        var minimumCapital = categoryCode switch
        {
            2 => 200_000_000m,
            3 => 5_000_000_000m,
            _ => 50_000_000m
        };
        var qualifyingCapital = decimal.Round(minimumCapital * 1.22m, 2, MidpointRounding.AwayFromZero);

        return normalizedFieldName switch
        {
            "mfb_category_code" => categoryCode,
            "unit_minimum_capital" => 50_000_000m,
            "state_minimum_capital" => 200_000_000m,
            "national_minimum_capital" => 5_000_000_000m,
            "paid_up_capital" => decimal.Round(minimumCapital * 1.05m, 2, MidpointRounding.AwayFromZero),
            "share_premium" => decimal.Round(minimumCapital * 0.05m, 2, MidpointRounding.AwayFromZero),
            "retained_earnings" => decimal.Round(minimumCapital * 0.08m, 2, MidpointRounding.AwayFromZero),
            "statutory_reserves" => decimal.Round(minimumCapital * 0.04m, 2, MidpointRounding.AwayFromZero),
            "tier1_capital" => decimal.Round(minimumCapital * 1.14m, 2, MidpointRounding.AwayFromZero),
            "tier2_capital" => decimal.Round(minimumCapital * 0.08m, 2, MidpointRounding.AwayFromZero),
            "total_qualifying_capital" => qualifyingCapital,
            "total_risk_weighted_assets" => decimal.Round(minimumCapital * 4.75m, 2, MidpointRounding.AwayFromZero),
            "capital_requirement_met" or "capital_requirement_met_flag" => 1,
            _ => null
        };
    }

    private static object? BuildBdcFinancialValue(string normalizedFieldName)
        => normalizedFieldName switch
        {
            "fx_trading_income" => 800_000m,
            "commission_income" => 150_000m,
            "other_income" => 50_000m,
            "total_income" => 1_000_000m,
            "staff_costs" => 250_000m,
            "rent" => 100_000m,
            "utilities" => 50_000m,
            "depreciation" => 25_000m,
            "other_expenses" => 75_000m,
            "total_expenses" => 500_000m,
            "profit_before_tax" => 500_000m,
            "tax_provision" => 75_000m,
            "profit_after_tax" => 425_000m,
            "cash_and_bank" => 2_000_000m,
            "receivables" => 300_000m,
            "fixed_assets" => 450_000m,
            "other_assets" => 250_000m,
            "total_assets" => 3_000_000m,
            "payables" => 600_000m,
            "other_liabilities" => 400_000m,
            "total_liabilities" => 1_000_000m,
            "shareholders_funds" or "shareholders_fund" => 2_000_000m,
            _ => null
        };

    private static object? BuildBdcFxValue(string normalizedFieldName)
        => normalizedFieldName switch
        {
            "usd_buying_volume" => 100_000m,
            "usd_buying_rate_avg" => 1_495m,
            "usd_selling_volume" => 90_000m,
            "usd_selling_rate_avg" => 1_505m,
            "gbp_buying_volume" => 50_000m,
            "gbp_buying_rate_avg" => 1_890m,
            "gbp_selling_volume" => 40_000m,
            "gbp_selling_rate_avg" => 1_910m,
            "eur_buying_volume" => 75_000m,
            "eur_buying_rate_avg" => 1_640m,
            "eur_selling_volume" => 70_000m,
            "eur_selling_rate_avg" => 1_660m,
            "cny_buying_volume" => 25_000m,
            "cny_buying_rate_avg" => 205m,
            "cny_selling_volume" => 20_000m,
            "cny_selling_rate_avg" => 215m,
            "other_buying_volume" => 10_000m,
            "other_buying_rate_avg" => 980m,
            "other_selling_volume" => 8_000m,
            "other_selling_rate_avg" => 1_020m,
            _ => null
        };

    private static object? BuildBdcBranchValue(string normalizedFieldName, int rowIndex)
        => normalizedFieldName switch
        {
            "branch_fx_volume" => rowIndex switch
            {
                0 => 70_000m,
                1 => 62_000m,
                _ => 48_000m
            },
            "active_branches" => 4,
            "inactive_branches" => 1,
            "head_office_branch_fx_volume" => 80_000m,
            "other_branches_fx_volume" => 180_000m,
            _ => null
        };

    private static string ResolveItemCodeRaw(TemplateItemCode? itemCode, int rowIndex)
        => string.IsNullOrWhiteSpace(itemCode?.ItemCode)
            ? (rowIndex + 1).ToString(CultureInfo.InvariantCulture)
            : itemCode.ItemCode.Trim();

    private static object ResolveItemCodeValue(TemplateField field, TemplateItemCode? itemCode, int rowIndex)
    {
        var raw = ResolveItemCodeRaw(itemCode, rowIndex);
        return ConvertStringValue(field, raw)
               ?? (field.DataType == FieldDataType.Integer ? rowIndex + 1 : raw);
    }

    private void ApplyFormulaTargets(CachedTemplate template, ReturnDataRecord record)
    {
        var fields = template.CurrentVersion.Fields.ToDictionary(x => x.FieldName, StringComparer.OrdinalIgnoreCase);
        foreach (var row in record.Rows)
        {
            foreach (var formula in template.CurrentVersion.IntraSheetFormulas.OrderBy(x => x.SortOrder))
            {
                ApplyFormulaTarget(formula, row, fields);
            }
        }
    }

    private void ApplyFormulaTarget(
        IntraSheetFormula formula,
        ReturnDataRow row,
        IReadOnlyDictionary<string, TemplateField> fieldMap)
    {
        if (!fieldMap.TryGetValue(formula.TargetFieldName, out var targetField))
        {
            return;
        }

        var operands = ParseOperandFields(formula.OperandFields);
        var currentValue = row.GetDecimal(formula.TargetFieldName) ?? 0m;

        decimal nextValue = formula.FormulaType switch
        {
            FormulaType.Sum => operands.Sum(x => row.GetDecimal(x) ?? 0m),
            FormulaType.Difference => (row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? 0m)
                - (row.GetDecimal(operands.ElementAtOrDefault(1) ?? string.Empty) ?? 0m),
            FormulaType.Equals => row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue,
            FormulaType.Ratio => ComputeRatioValue(row, operands, currentValue),
            FormulaType.GreaterThan => ComputeGreaterThanValue(row, operands, currentValue, targetField),
            FormulaType.GreaterThanOrEqual => ComputeGreaterThanOrEqualValue(row, operands, currentValue),
            FormulaType.LessThan => ComputeLessThanValue(row, operands, currentValue, targetField),
            FormulaType.LessThanOrEqual => ComputeLessThanOrEqualValue(row, operands, currentValue),
            FormulaType.Between => ComputeBetweenValue(row, operands, currentValue),
            FormulaType.Custom => ComputeCustomValue(formula, row, currentValue),
            FormulaType.Required => currentValue,
            _ => currentValue
        };

        row.SetValue(targetField.FieldName, ConvertNumericValue(targetField, nextValue));
    }

    private decimal ComputeCustomValue(IntraSheetFormula formula, ReturnDataRow row, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(formula.CustomExpression))
        {
            return fallback;
        }

        var expression = formula.CustomExpression.Trim();
        var funcMatch = FuncExpressionRegex.Match(expression);
        if (funcMatch.Success)
        {
            var functionName = funcMatch.Groups["name"].Value.Trim();
            var argsText = funcMatch.Groups["args"].Value.Trim();
            var args = string.IsNullOrWhiteSpace(argsText)
                ? Array.Empty<string>()
                : argsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return EvaluateCustomFunction(functionName, args, row);
        }

        var equalsIndex = expression.IndexOf('=');
        if (equalsIndex > 0)
        {
            var left = expression[..equalsIndex].Trim();
            var right = expression[(equalsIndex + 1)..].Trim();
            if (left.Equals(formula.TargetFieldName, StringComparison.OrdinalIgnoreCase))
            {
                var variables = row.AllFields
                    .Select(x => new { x.Key, Value = row.GetDecimal(x.Key) })
                    .Where(x => x.Value.HasValue)
                    .ToDictionary(x => x.Key, x => x.Value!.Value, StringComparer.OrdinalIgnoreCase);
                return _expressionParser.Evaluate(right, variables).LeftValue;
            }
        }

        return fallback;
    }

    private static decimal ComputeRatioValue(ReturnDataRow row, IReadOnlyList<string> operands, decimal fallback)
    {
        if (operands.Count < 2)
        {
            return fallback;
        }

        var numerator = row.GetDecimal(operands[0]) ?? 0m;
        var denominator = row.GetDecimal(operands[1]) ?? 0m;
        if (denominator == 0)
        {
            denominator = 1m;
            row.SetValue(operands[1], denominator);
        }

        return numerator / denominator;
    }

    private static decimal ComputeGreaterThanValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue,
        TemplateField targetField)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        var candidate = Math.Max(currentValue, operand);
        return candidate + ResolveStep(targetField);
    }

    private static decimal ComputeGreaterThanOrEqualValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        return Math.Max(currentValue, operand);
    }

    private static decimal ComputeLessThanValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue,
        TemplateField targetField)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        var candidate = Math.Min(currentValue, operand);
        return candidate - ResolveStep(targetField);
    }

    private static decimal ComputeLessThanOrEqualValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        return Math.Min(currentValue, operand);
    }

    private static decimal ComputeBetweenValue(ReturnDataRow row, IReadOnlyList<string> operands, decimal fallback)
    {
        if (operands.Count < 2)
        {
            return fallback;
        }

        var lower = row.GetDecimal(operands[0]) ?? fallback;
        var upper = row.GetDecimal(operands[1]) ?? fallback;
        if (upper < lower)
        {
            (lower, upper) = (upper, lower);
        }

        return lower + ((upper - lower) / 2m);
    }

    private static decimal ResolveStep(TemplateField field)
        => field.DataType switch
        {
            FieldDataType.Integer => 1m,
            FieldDataType.Percentage => 0.5m,
            _ => 1m
        };

    private static decimal ResolveThresholdValue(string normalizedFieldName)
    {
        if (normalizedFieldName.Contains("minimum", StringComparison.OrdinalIgnoreCase))
        {
            return 10m;
        }

        if (normalizedFieldName.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return 30m;
        }

        if (normalizedFieldName.Contains("threshold", StringComparison.OrdinalIgnoreCase))
        {
            return 25m;
        }

        return 10m;
    }

    private void CoerceRecordValues(CachedTemplate template, ReturnDataRecord record, DateTime reportingDate)
    {
        foreach (var row in record.Rows)
        {
            foreach (var field in template.CurrentVersion.Fields)
            {
                var value = row.GetValue(field.FieldName);
                if (value is null)
                {
                    continue;
                }

                if (field.DataType == FieldDataType.Text)
                {
                    var str = value.ToString() ?? string.Empty;
                    var allowed = ParseAllowedValues(field.AllowedValues);
                    if (allowed.Count > 0 && !allowed.Contains(str, StringComparer.OrdinalIgnoreCase))
                    {
                        str = allowed[0];
                    }

                    if (field.MaxLength.HasValue && str.Length > field.MaxLength.Value)
                    {
                        str = str[..field.MaxLength.Value];
                    }

                    row.SetValue(field.FieldName, str);
                    continue;
                }

                if (field.DataType == FieldDataType.Date)
                {
                    row.SetValue(field.FieldName, reportingDate.Date);
                    continue;
                }

                if (field.DataType == FieldDataType.Boolean)
                {
                    row.SetValue(field.FieldName, Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                    continue;
                }

                var dec = row.GetDecimal(field.FieldName);
                if (!dec.HasValue)
                {
                    continue;
                }

                var next = dec.Value;
                if (decimal.TryParse(field.MinValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var min)
                    && next < min)
                {
                    next = min;
                }

                if (decimal.TryParse(field.MaxValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var max)
                    && next > max)
                {
                    next = max;
                }

                row.SetValue(field.FieldName, ConvertNumericValue(field, next));
            }
        }
    }

    private void RepairValidationErrors(
        CachedTemplate template,
        ReturnDataRecord record,
        IReadOnlyList<ValidationError> errors,
        DateTime reportingDate)
    {
        var fields = template.CurrentVersion.Fields.ToDictionary(x => x.FieldName, StringComparer.OrdinalIgnoreCase);
        var formulas = template.CurrentVersion.IntraSheetFormulas.ToDictionary(x => x.RuleCode, StringComparer.OrdinalIgnoreCase);

        foreach (var error in errors.Where(x => x.Severity == ValidationSeverity.Error))
        {
            if (error.Category == ValidationCategory.TypeRange && fields.TryGetValue(error.Field, out var field))
            {
                foreach (var row in record.Rows)
                {
                    if (error.RuleId.StartsWith("REQ-", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, BuildInitialValue(
                            template.ReturnCode,
                            field,
                            reportingDate,
                            rowIndex: 0,
                            institution: new Institution { InstitutionCode = "FCDEMO", InstitutionName = "Finance Company Demo" },
                            spec: new InstitutionSeedSpec("FC", "FCDEMO", "Finance Company Demo", "fcdemo", "fcdemoadmin", "fcdemoadmin@regos.demo.local", "Finance Company Demo Admin", "fcdemo@regos.demo.local", "+2348000000000", "1 Demo Avenue, Lagos", "Lagos", "Lagos", DemoInstitutionPersona.Baseline),
                            itemCode: null));
                        continue;
                    }

                    if (error.RuleId.StartsWith("ENUM-", StringComparison.OrdinalIgnoreCase))
                    {
                        var allowed = ParseAllowedValues(field.AllowedValues);
                        if (allowed.Count > 0)
                        {
                            row.SetValue(field.FieldName, ConvertStringValue(field, allowed[0]));
                        }
                        continue;
                    }

                    if (error.RuleId.StartsWith("LEN-", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = row.GetString(field.FieldName) ?? string.Empty;
                        var maxLength = field.MaxLength.GetValueOrDefault(40);
                        row.SetValue(field.FieldName, value[..Math.Min(value.Length, maxLength)]);
                        continue;
                    }

                    if (error.RuleId.StartsWith("RANGE-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (decimal.TryParse(field.MinValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var min))
                        {
                            row.SetValue(field.FieldName, ConvertNumericValue(field, min));
                            continue;
                        }

                        if (decimal.TryParse(field.MaxValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var max))
                        {
                            row.SetValue(field.FieldName, ConvertNumericValue(field, max));
                        }
                    }
                }

                continue;
            }

            if (formulas.TryGetValue(error.RuleId, out var formula))
            {
                foreach (var row in record.Rows)
                {
                    ApplyFormulaTarget(formula, row, fields);
                }
                continue;
            }

            var targetFormula = template.CurrentVersion.IntraSheetFormulas
                .Where(x => string.Equals(x.TargetFieldName, error.Field, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.SortOrder)
                .FirstOrDefault();
            if (targetFormula is not null)
            {
                foreach (var row in record.Rows)
                {
                    ApplyFormulaTarget(targetFormula, row, fields);
                }
            }
        }
    }

    private static List<string> ParseOperandFields(string operandFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(operandFieldsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(operandFieldsJson)
                ?.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static object ConvertNumericValue(TemplateField field, decimal value)
        => field.DataType switch
        {
            FieldDataType.Integer => (int)Math.Round(value, MidpointRounding.AwayFromZero),
            FieldDataType.Money => decimal.Round(value, 2, MidpointRounding.AwayFromZero),
            _ => decimal.Round(value, 6, MidpointRounding.AwayFromZero)
        };

    private static decimal EvaluateCustomFunction(string functionName, IReadOnlyList<string> arguments, ReturnDataRow row)
    {
        decimal ValueAt(int index)
        {
            if (index >= arguments.Count)
            {
                return 0m;
            }

            var token = arguments[index];
            return decimal.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric)
                ? numeric
                : row.GetDecimal(token) ?? 0m;
        }

        return functionName.ToUpperInvariant() switch
        {
            "SUM" => Enumerable.Range(0, arguments.Count).Sum(ValueAt),
            "DELTA" => ValueAt(0) - ValueAt(1),
            "CAR" => CalculateRatio(ValueAt(0) + ValueAt(1), ValueAt(2)),
            "LCR" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "NSFR" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "NPL_RATIO" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "ECL" => decimal.Round(ValueAt(0) * ValueAt(1) * ValueAt(2), 2, MidpointRounding.AwayFromZero),
            "OSS_RATIO" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "PAR_RATIO" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "SOLVENCY_MARGIN" => decimal.Round(ValueAt(0) - ValueAt(1), 2, MidpointRounding.AwayFromZero),
            "COMBINED_RATIO" => decimal.Round(ValueAt(0) + ValueAt(1), 2, MidpointRounding.AwayFromZero),
            "RATE_BAND_CHECK" => EvaluateRateBandCheck(ValueAt(0), ValueAt(1), arguments.Count >= 3 ? ValueAt(2) : 10m),
            "NDIC_DPAS_RAW" => decimal.Round(ValueAt(0) * ValueAt(1), 2, MidpointRounding.AwayFromZero),
            "NDIC_DPAS_PREMIUM" => decimal.Round(Math.Max(ValueAt(0) * ValueAt(1), ValueAt(2)), 2, MidpointRounding.AwayFromZero),
            "BDC_MIN_CAPITAL_REQUIRED" => EvaluateBdcMinimumCapital(ValueAt(0), ValueAt(1), ValueAt(2)),
            "MFB_MIN_CAPITAL_REQUIRED" => EvaluateMfbMinimumCapital(ValueAt(0), ValueAt(1), ValueAt(2), ValueAt(3)),
            _ => 0m
        };
    }

    private static decimal EvaluateRateBandCheck(decimal actualRate, decimal referenceRate, decimal bandPercent)
    {
        var lowerBound = referenceRate * (1 - (bandPercent / 100m));
        var upperBound = referenceRate * (1 + (bandPercent / 100m));
        return actualRate >= lowerBound && actualRate <= upperBound ? 1m : 0m;
    }

    private static decimal EvaluateBdcMinimumCapital(decimal categoryCode, decimal categoryAMinimum, decimal categoryBMinimum)
        => categoryCode switch
        {
            <= 1m => categoryAMinimum,
            2m => categoryBMinimum,
            _ => categoryAMinimum
        };

    private static decimal EvaluateMfbMinimumCapital(
        decimal categoryCode,
        decimal unitMinimum,
        decimal stateMinimum,
        decimal nationalMinimum)
        => categoryCode switch
        {
            <= 1m => unitMinimum,
            2m => stateMinimum,
            3m => nationalMinimum,
            _ => unitMinimum
        };

    private static decimal CalculateRatio(decimal numerator, decimal denominator)
    {
        if (denominator == 0)
        {
            return 0m;
        }

        return decimal.Round((numerator / denominator) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static bool ShouldRefreshInstitutionSignalData(IReadOnlyList<TemplateSeedBatch> batches)
        => batches.Any(x => x.Module.ModuleCode is "BDC_CBN" or "DMB_BASEL3" or "FC_RETURNS" or "MFB_PAR");

    private static SubmissionSeedDisposition ResolveSubmissionDisposition(
        InstitutionSeedSpec spec,
        DemoPeriodSpec periodSpec,
        int templateIndex,
        int templateCount,
        int institutionSequence)
    {
        if (!periodSpec.IsCurrent || templateCount <= 0)
        {
            return SubmissionSeedDisposition.Accepted;
        }

        var (missingCount, returnedCount, warningCount) = ResolveCurrentDispositionCounts(spec.Persona, templateCount);
        var rotatedIndex = (templateIndex + institutionSequence) % templateCount;

        if (rotatedIndex < missingCount)
        {
            return SubmissionSeedDisposition.Missing;
        }

        if (rotatedIndex < missingCount + returnedCount)
        {
            return SubmissionSeedDisposition.Returned;
        }

        if (rotatedIndex < missingCount + returnedCount + warningCount)
        {
            return SubmissionSeedDisposition.AcceptedWithWarnings;
        }

        return SubmissionSeedDisposition.Accepted;
    }

    private static (int MissingCount, int ReturnedCount, int WarningCount) ResolveCurrentDispositionCounts(
        DemoInstitutionPersona persona,
        int templateCount)
    {
        var (missingRatio, returnedRatio, warningRatio) = persona switch
        {
            DemoInstitutionPersona.Baseline => (0m, 0m, 0.08m),
            DemoInstitutionPersona.FilingWatch => (0.18m, 0m, 0.08m),
            DemoInstitutionPersona.ReturnedSubmission => (0.05m, 0.14m, 0.08m),
            DemoInstitutionPersona.CapitalFragile => (0.08m, 0.08m, 0.08m),
            DemoInstitutionPersona.ResilienceStress => (0.06m, 0.06m, 0.08m),
            DemoInstitutionPersona.CyberElevated => (0.04m, 0.08m, 0.10m),
            DemoInstitutionPersona.RecoveryTrack => (0.04m, 0.05m, 0.14m),
            DemoInstitutionPersona.CompoundPressure => (0.24m, 0.12m, 0.06m),
            _ => (0m, 0m, 0m)
        };

        var missingCount = ComputeScaledDispositionCount(templateCount, missingRatio);
        var returnedCount = ComputeScaledDispositionCount(templateCount, returnedRatio);
        var warningCount = ComputeScaledDispositionCount(templateCount, warningRatio);

        var total = missingCount + returnedCount + warningCount;
        if (total <= templateCount)
        {
            return (missingCount, returnedCount, warningCount);
        }

        var overflow = total - templateCount;
        if (warningCount > 0)
        {
            var reduceWarning = Math.Min(warningCount, overflow);
            warningCount -= reduceWarning;
            overflow -= reduceWarning;
        }

        if (overflow > 0 && returnedCount > 0)
        {
            var reduceReturned = Math.Min(returnedCount, overflow);
            returnedCount -= reduceReturned;
            overflow -= reduceReturned;
        }

        if (overflow > 0 && missingCount > 0)
        {
            missingCount = Math.Max(0, missingCount - overflow);
        }

        return (missingCount, returnedCount, warningCount);
    }

    private static int ComputeScaledDispositionCount(int templateCount, decimal ratio)
    {
        if (templateCount <= 0 || ratio <= 0m)
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Round(templateCount * ratio, MidpointRounding.AwayFromZero));
    }

    private async Task RefreshInstitutionSignalDataAsync(
        Institution institution,
        InstitutionSeedSpec spec,
        IReadOnlyList<TemplateSeedBatch> batches,
        IReadOnlyList<SeededSubmissionArtifact> createdSubmissions,
        CancellationToken ct)
    {
        var currentExpectedCount = batches.Sum(x => x.Templates.Count);
        var currentSubmissions = createdSubmissions.Where(x => x.IsCurrentPeriod).ToList();
        var currentSubmittedCount = currentSubmissions.Count;
        var currentReturnedCount = currentSubmissions.Count(x => x.Status is SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected);
        var currentWarningCount = currentSubmissions.Count(x => x.Status == SubmissionStatus.AcceptedWithWarnings);
        var currentMissingCount = Math.Max(0, currentExpectedCount - currentSubmittedCount);
        var profile = BuildInstitutionSignalProfile(
            spec.Persona,
            currentExpectedCount,
            currentMissingCount,
            currentReturnedCount,
            currentWarningCount);

        await CleanupInstitutionSignalDataAsync(institution.TenantId, ct);

        var snapshots = BuildChsSnapshots(institution, profile);
        var incidents = BuildDataBreachIncidents(institution, spec, profile);
        var alerts = BuildSecurityAlerts(institution, spec, profile);
        var fieldChanges = BuildFieldChanges(institution, spec, profile, createdSubmissions);

        if (snapshots.Count > 0)
        {
            _db.ChsScoreSnapshots.AddRange(snapshots);
        }

        if (incidents.Count > 0)
        {
            _db.DataBreachIncidents.AddRange(incidents);
        }

        if (alerts.Count > 0)
        {
            _db.SecurityAlerts.AddRange(alerts);
        }

        if (fieldChanges.Count > 0)
        {
            _db.FieldChangeHistory.AddRange(fieldChanges);
        }

        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task CleanupInstitutionSignalDataAsync(Guid tenantId, CancellationToken ct)
    {
        var snapshots = await _db.ChsScoreSnapshots.Where(x => x.TenantId == tenantId).ToListAsync(ct);
        var incidents = await _db.DataBreachIncidents.Where(x => x.TenantId == tenantId).ToListAsync(ct);
        var alerts = await _db.SecurityAlerts.Where(x => x.TenantId == tenantId).ToListAsync(ct);
        var fieldChanges = await _db.FieldChangeHistory.Where(x => x.TenantId == tenantId).ToListAsync(ct);

        if (snapshots.Count > 0)
        {
            _db.ChsScoreSnapshots.RemoveRange(snapshots);
        }

        if (incidents.Count > 0)
        {
            _db.DataBreachIncidents.RemoveRange(incidents);
        }

        if (alerts.Count > 0)
        {
            _db.SecurityAlerts.RemoveRange(alerts);
        }

        if (fieldChanges.Count > 0)
        {
            _db.FieldChangeHistory.RemoveRange(fieldChanges);
        }

        if (snapshots.Count > 0 || incidents.Count > 0 || alerts.Count > 0 || fieldChanges.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
    }

    private static InstitutionSignalProfile BuildInstitutionSignalProfile(
        DemoInstitutionPersona persona,
        int currentExpectedCount,
        int currentMissingCount,
        int currentReturnedCount,
        int currentWarningCount)
    {
        var baseProfile = persona switch
        {
            DemoInstitutionPersona.Baseline => new InstitutionSignalProfile(96m, 94m, 88m, 90m, 91m, 0, 0, 0, 1.2m),
            DemoInstitutionPersona.FilingWatch => new InstitutionSignalProfile(74m, 85m, 79m, 82m, 77m, 0, 0, 0, -1.6m),
            DemoInstitutionPersona.ReturnedSubmission => new InstitutionSignalProfile(84m, 69m, 76m, 74m, 78m, 0, 0, 1, -1.8m),
            DemoInstitutionPersona.CapitalFragile => new InstitutionSignalProfile(79m, 81m, 42m, 70m, 73m, 0, 1, 1, -1.4m),
            DemoInstitutionPersona.ResilienceStress => new InstitutionSignalProfile(82m, 78m, 71m, 76m, 80m, 1, 1, 1, -1.2m),
            DemoInstitutionPersona.CyberElevated => new InstitutionSignalProfile(86m, 77m, 74m, 79m, 83m, 0, 2, 1, -1.1m),
            DemoInstitutionPersona.RecoveryTrack => new InstitutionSignalProfile(91m, 88m, 68m, 86m, 90m, 0, 0, 1, 2.4m),
            DemoInstitutionPersona.CompoundPressure => new InstitutionSignalProfile(58m, 55m, 34m, 52m, 60m, 2, 3, 3, -2.7m),
            _ => new InstitutionSignalProfile(90m, 85m, 80m, 84m, 86m, 0, 0, 0, 0m)
        };

        var coveragePercent = currentExpectedCount <= 0
            ? 100m
            : decimal.Round((currentExpectedCount - currentMissingCount) * 100m / currentExpectedCount, 1, MidpointRounding.AwayFromZero);
        var filingTimeliness = ClampScore((coveragePercent * 0.72m) + (baseProfile.FilingTimeliness * 0.28m) - (currentReturnedCount * 2.5m));
        var dataQuality = ClampScore(baseProfile.DataQuality - (currentReturnedCount * 7m) - (currentWarningCount * 2.5m));
        var regulatoryCapital = ClampScore(baseProfile.RegulatoryCapital);
        var auditGovernance = ClampScore(baseProfile.AuditGovernance - (currentReturnedCount * 1.5m));
        var engagement = ClampScore(baseProfile.Engagement - (currentMissingCount * 1.2m));
        var overallScore = ClampScore(
            (filingTimeliness * 0.24m)
            + (dataQuality * 0.20m)
            + (regulatoryCapital * 0.28m)
            + (auditGovernance * 0.14m)
            + (engagement * 0.14m));

        return baseProfile with
        {
            FilingTimeliness = filingTimeliness,
            DataQuality = dataQuality,
            RegulatoryCapital = regulatoryCapital,
            AuditGovernance = auditGovernance,
            Engagement = engagement,
            OverallScore = overallScore,
            Rating = MapChsRating(overallScore)
        };
    }

    private static List<ChsScoreSnapshot> BuildChsSnapshots(
        Institution institution,
        InstitutionSignalProfile profile)
    {
        var snapshots = new List<ChsScoreSnapshot>(3);
        for (var index = 0; index < 3; index++)
        {
            var weeksAgo = 2 - index;
            var overall = ClampScore(profile.OverallScore - (profile.WeeklyTrendDelta * weeksAgo));
            var filing = ClampScore(profile.FilingTimeliness - (profile.WeeklyTrendDelta * 0.85m * weeksAgo));
            var dataQuality = ClampScore(profile.DataQuality - (profile.WeeklyTrendDelta * 0.70m * weeksAgo));
            var capital = ClampScore(profile.RegulatoryCapital - (profile.WeeklyTrendDelta * 0.95m * weeksAgo));
            var audit = ClampScore(profile.AuditGovernance - (profile.WeeklyTrendDelta * 0.55m * weeksAgo));
            var engagement = ClampScore(profile.Engagement - (profile.WeeklyTrendDelta * 0.50m * weeksAgo));
            var computedAt = DateTime.UtcNow.Date.AddDays(-(weeksAgo * 7)).AddHours(8);

            snapshots.Add(new ChsScoreSnapshot
            {
                TenantId = institution.TenantId,
                PeriodLabel = $"{computedAt:yyyy}-W{ISOWeek.GetWeekOfYear(computedAt):00}",
                ComputedAt = computedAt,
                OverallScore = overall,
                Rating = MapChsRating(overall),
                FilingTimeliness = filing,
                DataQuality = dataQuality,
                RegulatoryCapital = capital,
                AuditGovernance = audit,
                Engagement = engagement
            });
        }

        return snapshots;
    }

    private static List<DataBreachIncident> BuildDataBreachIncidents(
        Institution institution,
        InstitutionSeedSpec spec,
        InstitutionSignalProfile profile)
    {
        var incidents = new List<DataBreachIncident>();
        var seedTime = DateTime.UtcNow.Date.AddHours(9);

        void AddIncident(
            DataBreachSeverity severity,
            DataBreachStatus status,
            string title,
            string description,
            int affectedSubjects,
            int daysAgo,
            bool isOpen)
        {
            var detectedAt = seedTime.AddDays(-daysAgo);
            incidents.Add(new DataBreachIncident
            {
                TenantId = institution.TenantId,
                Severity = severity,
                Status = status,
                Title = title,
                Description = description,
                DataSubjectsAffected = affectedSubjects,
                DataCategoriesAffected = "customer_profile, transaction_metadata",
                DetectedAt = detectedAt,
                ContainedAt = isOpen ? null : detectedAt.AddHours(14),
                NitdaNotificationDeadline = detectedAt.AddDays(3),
                NitdaNotifiedAt = isOpen ? null : detectedAt.AddDays(1),
                RemediatedAt = isOpen ? null : detectedAt.AddDays(4),
                DpoNotes = $"demo-persona:{spec.Persona}"
            });
        }

        switch (spec.Persona)
        {
            case DemoInstitutionPersona.ResilienceStress:
                AddIncident(
                    DataBreachSeverity.HIGH,
                    DataBreachStatus.Assessed,
                    $"{spec.City} branch endpoint containment case",
                    "Endpoint telemetry shows repeated malware callbacks on a branch workstation pending full eradication.",
                    420,
                    daysAgo: 5,
                    isOpen: true);
                break;
            case DemoInstitutionPersona.CyberElevated:
                AddIncident(
                    DataBreachSeverity.MEDIUM,
                    DataBreachStatus.Contained,
                    "Privileged credential leakage investigation",
                    "Compromised service credentials were rotated after abnormal authentication activity was traced to a third-party device.",
                    185,
                    daysAgo: 8,
                    isOpen: false);
                break;
            case DemoInstitutionPersona.RecoveryTrack:
                AddIncident(
                    DataBreachSeverity.HIGH,
                    DataBreachStatus.Remediated,
                    "Customer data disclosure case remediated",
                    "A previously reported disclosure incident was fully remediated and residual control actions were closed.",
                    260,
                    daysAgo: 18,
                    isOpen: false);
                break;
            case DemoInstitutionPersona.CompoundPressure:
                AddIncident(
                    DataBreachSeverity.CRITICAL,
                    DataBreachStatus.Assessed,
                    "Core channel data-loss event pending executive closure",
                    "A material data-loss event remains under active containment with regulator notification packs still open.",
                    2_150,
                    daysAgo: 3,
                    isOpen: true);
                AddIncident(
                    DataBreachSeverity.HIGH,
                    DataBreachStatus.Contained,
                    "Vendor integration breach investigation",
                    "An upstream vendor integration transmitted malformed files that triggered high-risk breach review.",
                    690,
                    daysAgo: 11,
                    isOpen: true);
                break;
        }

        return incidents;
    }

    private static List<SecurityAlert> BuildSecurityAlerts(
        Institution institution,
        InstitutionSeedSpec spec,
        InstitutionSignalProfile profile)
    {
        var alerts = new List<SecurityAlert>();
        var seedTime = DateTime.UtcNow.Date.AddHours(11);

        void AddAlert(string alertType, string severity, string status, string title, string description, int hoursAgo)
        {
            alerts.Add(new SecurityAlert
            {
                Id = Guid.NewGuid(),
                TenantId = institution.TenantId,
                AlertType = alertType,
                Severity = severity,
                Title = title,
                Description = description,
                Status = status,
                EvidenceJson = $$"""{"source":"demo-persona","persona":"{{spec.Persona}}"}""",
                CreatedAt = seedTime.AddHours(-hoursAgo)
            });
        }

        if (profile.OpenAlertCount == 0 && spec.Persona != DemoInstitutionPersona.RecoveryTrack)
        {
            return alerts;
        }

        switch (spec.Persona)
        {
            case DemoInstitutionPersona.CapitalFragile:
                AddAlert("control-gap", "medium", "open", "Capital committee evidence refresh overdue", "Capital remediation evidence has not been refreshed on schedule.", 38);
                break;
            case DemoInstitutionPersona.ResilienceStress:
                AddAlert("branch-network", "medium", "investigating", "Branch network latency spike", "The branch network remains in heightened monitoring while containment actions are validated.", 21);
                break;
            case DemoInstitutionPersona.CyberElevated:
                AddAlert("iam", "high", "open", "Privileged access anomaly", "A privileged account initiated access from an unusual source range and remains under analyst review.", 9);
                AddAlert("endpoint", "medium", "investigating", "Endpoint control policy drift", "Security baselines drifted on a monitored endpoint fleet pending policy reconciliation.", 33);
                break;
            case DemoInstitutionPersona.RecoveryTrack:
                AddAlert("endpoint", "low", "closed", "Endpoint hardening closure", "Residual hardening actions from a prior security case were closed after validation.", 72);
                break;
            case DemoInstitutionPersona.CompoundPressure:
                AddAlert("identity", "critical", "open", "Privileged identity compromise alert", "Multiple privileged sign-ins matched high-confidence takeover patterns and remain open.", 6);
                AddAlert("network", "high", "investigating", "Suspicious east-west movement", "Lateral movement indicators persist across segmented workloads under investigation.", 15);
                AddAlert("control-gap", "medium", "open", "Log retention gap flagged", "Security telemetry retention dropped below policy for a monitored perimeter device.", 47);
                break;
        }

        return alerts;
    }

    private static List<FieldChangeHistory> BuildFieldChanges(
        Institution institution,
        InstitutionSeedSpec spec,
        InstitutionSignalProfile profile,
        IReadOnlyList<SeededSubmissionArtifact> createdSubmissions)
    {
        if (profile.ModelReviewCount <= 0 || createdSubmissions.Count == 0)
        {
            return [];
        }

        var anchorSubmissions = createdSubmissions
            .Where(x => x.IsCurrentPeriod)
            .DefaultIfEmpty(createdSubmissions.OrderByDescending(x => x.SubmittedAt).First())
            .ToList();
        var fields = new[]
        {
            "capital_buffer_override",
            "validation_rule_scalar",
            "scenario_weight_adjustment",
            "portfolio_pd_scalar"
        };

        var changes = new List<FieldChangeHistory>(profile.ModelReviewCount);
        for (var index = 0; index < profile.ModelReviewCount; index++)
        {
            var anchor = anchorSubmissions[index % anchorSubmissions.Count];
            changes.Add(new FieldChangeHistory
            {
                TenantId = institution.TenantId,
                SubmissionId = anchor.SubmissionId,
                ReturnCode = anchor.ReturnCode,
                FieldName = fields[index % fields.Length],
                OldValue = (1.0m + (index * 0.1m)).ToString("0.0#", CultureInfo.InvariantCulture),
                NewValue = (1.2m + (index * 0.15m)).ToString("0.0#", CultureInfo.InvariantCulture),
                ChangeSource = "System",
                SourceDetail = $"demo-persona:{spec.Persona}",
                ChangedBy = "demo-seed",
                ChangedAt = DateTime.UtcNow.AddHours(-(index + 2))
            });
        }

        return changes;
    }

    private static decimal ClampScore(decimal value)
        => decimal.Round(Math.Clamp(value, 30m, 99m), 1, MidpointRounding.AwayFromZero);

    private static int MapChsRating(decimal overallScore)
        => (int)(overallScore switch
        {
            >= 90m => FC.Engine.Domain.Models.ChsRating.APlus,
            >= 80m => FC.Engine.Domain.Models.ChsRating.A,
            >= 70m => FC.Engine.Domain.Models.ChsRating.B,
            >= 60m => FC.Engine.Domain.Models.ChsRating.C,
            >= 50m => FC.Engine.Domain.Models.ChsRating.D,
            _ => FC.Engine.Domain.Models.ChsRating.F
        });

    private async Task<SeededSubmissionArtifact> CreateSubmissionAsync(
        CachedTemplate template,
        ValidatedSubmissionPrototype prototype,
        ReturnDataRecord record,
        string xml,
        Institution institution,
        int submittedByUserId,
        int returnPeriodId,
        DateTime submittedAt,
        DemoPeriodSpec periodSpec,
        SubmissionSeedDisposition disposition,
        CancellationToken ct)
    {
        var submission = Submission.Create(institution.Id, returnPeriodId, template.ReturnCode, institution.TenantId);
        submission.SetTemplateVersion(template.CurrentVersion.Id);
        submission.SubmittedByUserId = submittedByUserId;
        submission.ApprovalRequired = false;
        submission.CreatedAt = submittedAt.AddMinutes(-10);
        submission.SubmittedAt = submittedAt;
        submission.ProcessingDurationMs = 220;
        submission.StoreRawXml(xml);
        submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(record));

        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync(ct);

        await PersistRecordAsync(template, record, submission.Id, institution.TenantId, ct);

        var validationReport = ValidationReport.Create(submission.Id, institution.TenantId);
        validationReport.AddErrors(prototype.ValidationErrors.Select(CloneValidationError));
        validationReport.FinalizeAt(submittedAt.AddSeconds(2));
        submission.AttachValidationReport(validationReport);
        if (disposition == SubmissionSeedDisposition.Returned)
        {
            submission.MarkRejected();
        }
        else if (disposition == SubmissionSeedDisposition.AcceptedWithWarnings || validationReport.HasWarnings)
        {
            submission.MarkAcceptedWithWarnings();
        }
        else
        {
            submission.MarkAccepted();
        }

        await _db.SaveChangesAsync(ct);
        var seededStatus = submission.Status;
        _db.ChangeTracker.Clear();

        return new SeededSubmissionArtifact(
            submission.Id,
            template.ReturnCode,
            periodSpec.Frequency,
            periodSpec.IsCurrent,
            seededStatus,
            submittedAt);
    }

    private async Task UpdateInstitutionSubmissionStampAsync(int institutionId, DateTime lastSubmissionAt, CancellationToken ct)
    {
        var institution = await _db.Institutions.FirstAsync(x => x.Id == institutionId, ct);
        institution.LastSubmissionAt = lastSubmissionAt;
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
    }

    private async Task PersistRecordAsync(
        CachedTemplate template,
        ReturnDataRecord record,
        int submissionId,
        Guid tenantId,
        CancellationToken ct)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(tenantId, ct);
        var textColumnLengths = await LoadPhysicalTextColumnLengthsAsync(connection, template.PhysicalTableName, ct);
        foreach (var row in record.Rows)
        {
            var persistedRow = ClampRowToPhysicalSchema(row, textColumnLengths);
            var (sql, parameters) = _sqlBuilder.BuildInsert(
                template.PhysicalTableName,
                template.CurrentVersion.Fields,
                persistedRow,
                submissionId,
                tenantId);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
    }

    private async Task<IReadOnlyDictionary<string, int>> LoadPhysicalTextColumnLengthsAsync(
        System.Data.IDbConnection connection,
        string tableName,
        CancellationToken ct)
    {
        if (_physicalTextLengthCache.TryGetValue(tableName, out var cached))
        {
            return cached;
        }

        var rows = await connection.QueryAsync<PhysicalTextColumnLength>(
            new CommandDefinition(
                """
                SELECT c.name AS ColumnName, c.max_length AS MaxLengthBytes, t.name AS DataType
                FROM sys.columns c
                INNER JOIN sys.tables tb ON tb.object_id = c.object_id
                INNER JOIN sys.schemas s ON s.schema_id = tb.schema_id
                INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
                WHERE s.name = 'dbo'
                  AND tb.name = @TableName
                  AND t.name IN ('nvarchar', 'varchar', 'nchar', 'char')
                """,
                new { TableName = tableName },
                cancellationToken: ct));

        var resolved = rows.ToDictionary(
            x => x.ColumnName,
            x => x.MaxLengthBytes < 0
                ? int.MaxValue
                : x.DataType is "nvarchar" or "nchar"
                    ? x.MaxLengthBytes / 2
                    : x.MaxLengthBytes,
            StringComparer.OrdinalIgnoreCase);

        _physicalTextLengthCache[tableName] = resolved;
        return resolved;
    }

    private static ReturnDataRow ClampRowToPhysicalSchema(
        ReturnDataRow source,
        IReadOnlyDictionary<string, int> textColumnLengths)
    {
        var clone = new ReturnDataRow
        {
            RowKey = source.RowKey
        };

        foreach (var field in source.AllFields)
        {
            var value = field.Value;
            if (value is string text
                && textColumnLengths.TryGetValue(field.Key, out var maxLength)
                && maxLength > 0
                && text.Length > maxLength)
            {
                value = text[..maxLength];
            }

            clone.SetValue(field.Key, value);
        }

        return clone;
    }

    private static void EnsureTemplateExpectations(string institutionType, IReadOnlyList<CachedTemplate> templates, int expectedCount)
    {
        if (templates.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} published templates for {institutionType}, found {templates.Count}.");
        }
    }

    private async Task<Module> ResolveSingleModuleAsync(
        IReadOnlyList<CachedTemplate> templates,
        string expectedModuleCode,
        CancellationToken ct)
    {
        var moduleIds = templates
            .Select(x => x.ModuleId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (moduleIds.Count == 0)
        {
            return await _db.Modules
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ModuleCode == expectedModuleCode, ct)
                ?? throw new InvalidOperationException($"Module {expectedModuleCode} was not found.");
        }

        if (moduleIds.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected a single module for the selected templates, found {moduleIds.Count}.");
        }

        var module = await _db.Modules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == moduleIds[0], ct)
            ?? throw new InvalidOperationException($"Module {moduleIds[0]} was not found.");

        if (!string.Equals(module.ModuleCode, expectedModuleCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Expected module {expectedModuleCode}, found {module.ModuleCode}.");
        }

        return module;
    }

    private async Task<Dictionary<string, string>> LoadSampleXmlMapAsync(
        IReadOnlyList<CachedTemplate> templates,
        string templatesDirectory,
        CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates)
        {
            var path = Path.Combine(templatesDirectory, $"{template.ReturnCode}_Valid_Sample.xml");
            if (File.Exists(path))
            {
                map[template.ReturnCode] = await File.ReadAllTextAsync(path, ct);
            }
        }

        return map;
    }

    private static List<InstitutionSeedSpec> BuildInstitutionSpecs(string licenceTypeCode, int count)
    {
        var specs = new List<InstitutionSeedSpec>(count);
        for (var index = 1; index <= count; index++)
        {
            var code = licenceTypeCode.Equals("FC", StringComparison.OrdinalIgnoreCase)
                ? $"FCD{index:000}"
                : $"{licenceTypeCode}{index:000}";
            var lowerCode = code.ToLowerInvariant();
            var persona = ResolvePersona(licenceTypeCode, index);
            var (city, state) = ResolveInstitutionLocation(index);
            var institutionName = BuildInstitutionName(licenceTypeCode, index, city);

            specs.Add(new InstitutionSeedSpec(
                licenceTypeCode,
                code,
                institutionName,
                $"{lowerCode}",
                $"{lowerCode}admin",
                $"{lowerCode}admin@regos.demo.local",
                $"{institutionName} Admin",
                $"{lowerCode}@regos.demo.local",
                $"+234{ResolvePhonePrefix(index)}{index:000000}",
                BuildInstitutionAddress(index, city, state),
                city,
                state,
                persona));
        }

        return specs;
    }

    private static DemoInstitutionPersona ResolvePersona(string licenceTypeCode, int index)
    {
        var slot = (index - 1) % 10;
        return licenceTypeCode.ToUpperInvariant() switch
        {
            "BDC" => slot switch
            {
                0 or 1 or 2 => DemoInstitutionPersona.Baseline,
                3 or 4 => DemoInstitutionPersona.FilingWatch,
                5 => DemoInstitutionPersona.ReturnedSubmission,
                6 => DemoInstitutionPersona.CyberElevated,
                7 => DemoInstitutionPersona.ResilienceStress,
                8 => DemoInstitutionPersona.RecoveryTrack,
                _ => DemoInstitutionPersona.CompoundPressure
            },
            "DMB" => slot switch
            {
                0 or 1 => DemoInstitutionPersona.Baseline,
                2 or 3 => DemoInstitutionPersona.CapitalFragile,
                4 => DemoInstitutionPersona.ReturnedSubmission,
                5 => DemoInstitutionPersona.ResilienceStress,
                6 => DemoInstitutionPersona.CyberElevated,
                7 or 8 => DemoInstitutionPersona.CompoundPressure,
                _ => DemoInstitutionPersona.RecoveryTrack
            },
            "FC" => slot switch
            {
                0 or 1 or 2 => DemoInstitutionPersona.Baseline,
                3 => DemoInstitutionPersona.FilingWatch,
                4 or 5 => DemoInstitutionPersona.CapitalFragile,
                6 => DemoInstitutionPersona.ReturnedSubmission,
                7 => DemoInstitutionPersona.CyberElevated,
                8 => DemoInstitutionPersona.RecoveryTrack,
                _ => DemoInstitutionPersona.CompoundPressure
            },
            "MFB" => slot switch
            {
                0 or 1 => DemoInstitutionPersona.Baseline,
                2 or 3 => DemoInstitutionPersona.FilingWatch,
                4 => DemoInstitutionPersona.ReturnedSubmission,
                5 => DemoInstitutionPersona.CapitalFragile,
                6 or 7 => DemoInstitutionPersona.ResilienceStress,
                8 => DemoInstitutionPersona.RecoveryTrack,
                _ => DemoInstitutionPersona.CompoundPressure
            },
            _ => DemoInstitutionPersona.Baseline
        };
    }

    private static (string City, string State) ResolveInstitutionLocation(int index)
    {
        string[] locations =
        [
            "Lagos:Lagos",
            "Abuja:FCT",
            "Port Harcourt:Rivers",
            "Ibadan:Oyo",
            "Kano:Kano",
            "Enugu:Enugu",
            "Benin City:Edo",
            "Uyo:Akwa Ibom",
            "Kaduna:Kaduna",
            "Ilorin:Kwara",
            "Abeokuta:Ogun",
            "Jos:Plateau"
        ];

        var parts = locations[(index - 1) % locations.Length].Split(':', 2);
        return (parts[0], parts[1]);
    }

    private static string BuildInstitutionName(string licenceTypeCode, int index, string city)
    {
        return licenceTypeCode.ToUpperInvariant() switch
        {
            "BDC" => BuildSectorName(
                index,
                city,
                ["Marina", "Crown", "Meridian", "Northfield", "Summit", "Heritage", "Atlas", "BlueGate"],
                ["Exchange Bureau", "FX Desk", "Forex Hub", "Capital Exchange", "Treasury Exchange", "Currency House"],
                "Ltd"),
            "DMB" => BuildSectorName(
                index,
                city,
                ["Union", "Citadel", "Northbridge", "Harbour", "Anchor", "Metropolitan", "First Meridian"],
                ["Commercial Bank", "Trust Bank", "Merchant Bank", "Bank"],
                "Plc"),
            "FC" => BuildSectorName(
                index,
                city,
                ["Cardinal", "Summit", "Axis", "Oakline", "Bluecrest", "Primefield", "Northgate", "Bridgepoint", "Sterling", "Crestview", "Pinnacle", "Westbay"],
                ["Finance Company", "Credit House", "Capital Finance", "Finance House", "Consumer Finance", "Lending Company", "Financial Services", "Commercial Finance", "Asset Finance", "Finance & Leasing"],
                "Ltd"),
            "MFB" => BuildSectorName(
                index,
                city,
                ["Unity", "Bridge", "Harvest", "Cedar", "Shoreline", "Prosperity"],
                ["Microfinance Bank", "Community Bank", "Microfinance", "Cooperative Bank"],
                "Ltd"),
            _ => $"RegOS {licenceTypeCode.ToUpperInvariant()} {index:000} {city}"
        };
    }

    private static string BuildSectorName(
        int index,
        string city,
        IReadOnlyList<string> prefixes,
        IReadOnlyList<string> sectors,
        string suffix)
    {
        var prefix = prefixes[(index - 1) % prefixes.Count];
        var sector = sectors[((index - 1) / prefixes.Count) % sectors.Count];
        return $"{prefix} {city} {sector} {suffix}";
    }

    private static string BuildInstitutionAddress(int index, string city, string state)
        => $"{12 + ((index - 1) % 48)} Market Square, {city}, {state}";

    private static string ResolvePhonePrefix(int index)
    {
        string[] prefixes = ["803", "805", "806", "807", "808", "809", "810", "812", "813", "814", "815", "816", "817", "818"];
        return prefixes[(index - 1) % prefixes.Length];
    }

    private static int ResolveInstitutionCategoryCode(string institutionCode, int maxCategory)
    {
        if (maxCategory <= 1)
        {
            return 1;
        }

        var digits = new string(institutionCode.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var numericSuffix) || numericSuffix <= 0)
        {
            return 1;
        }

        return ((numericSuffix - 1) % maxCategory) + 1;
    }

    private static TemplateSeedBatch[] BuildFrequencyBatches(
        IReadOnlyList<CachedTemplate> templates,
        Module module,
        IReadOnlyDictionary<string, string> sampleXmlMap,
        IReadOnlyList<DemoPeriodSpec> monthlyPeriodSpecs,
        IReadOnlyList<DemoPeriodSpec> quarterlyPeriodSpecs,
        IReadOnlyList<DemoPeriodSpec> semiAnnualPeriodSpecs,
        IReadOnlyList<DemoPeriodSpec> computedPeriodSpecs)
    {
        var batches = new List<TemplateSeedBatch>();

        void AddBatch(ReturnFrequency frequency, IReadOnlyList<DemoPeriodSpec> periodSpecs)
        {
            if (periodSpecs.Count == 0)
            {
                return;
            }

            var scopedTemplates = templates
                .Where(template => template.Frequency == frequency)
                .ToList();
            if (scopedTemplates.Count == 0)
            {
                return;
            }

            batches.Add(new TemplateSeedBatch(scopedTemplates, module, sampleXmlMap, periodSpecs));
        }

        AddBatch(ReturnFrequency.Monthly, monthlyPeriodSpecs);
        AddBatch(ReturnFrequency.Quarterly, quarterlyPeriodSpecs);
        AddBatch(ReturnFrequency.SemiAnnual, semiAnnualPeriodSpecs);
        AddBatch(ReturnFrequency.Computed, computedPeriodSpecs);
        return batches.ToArray();
    }

    private static string BuildPeriodLookupKey(int year, int month, string frequency)
        => $"{year:D4}:{month:D2}:{frequency.Trim()}";

    private static List<DemoPeriodSpec> BuildMonthlyPeriodSpecs(DateTime nowUtc, int count)
    {
        var latestCompletedMonthEnd = ResolveLatestCompletedMonthEnd(nowUtc);
        var specs = new List<DemoPeriodSpec>(count);
        for (var index = 0; index < count; index++)
        {
            var reportingDate = latestCompletedMonthEnd.AddMonths(-((count - 1) - index));
            specs.Add(new DemoPeriodSpec(
                Index: index,
                Year: reportingDate.Year,
                Month: reportingDate.Month,
                Quarter: ((reportingDate.Month - 1) / 3) + 1,
                Frequency: "Monthly",
                ReportingDate: reportingDate,
                SubmittedAt: reportingDate.AddDays(10),
                IsCurrent: index == count - 1));
        }

        return specs;
    }

    private static List<DemoPeriodSpec> BuildSemiAnnualPeriodSpecs(DateTime nowUtc, int count)
    {
        var latestCompletedSemiAnnualEnd = ResolveLatestCompletedSemiAnnualEnd(nowUtc);
        var specs = new List<DemoPeriodSpec>(count);
        for (var index = 0; index < count; index++)
        {
            var reportingDate = latestCompletedSemiAnnualEnd.AddMonths(-6 * ((count - 1) - index));
            specs.Add(new DemoPeriodSpec(
                Index: index,
                Year: reportingDate.Year,
                Month: reportingDate.Month,
                Quarter: ((reportingDate.Month - 1) / 3) + 1,
                Frequency: "SemiAnnual",
                ReportingDate: reportingDate,
                SubmittedAt: reportingDate.AddDays(24),
                IsCurrent: index == count - 1));
        }

        return specs;
    }

    private static List<DemoPeriodSpec> BuildComputedPeriodSpecs(DateTime nowUtc, int count)
    {
        var latestCompletedMonthEnd = ResolveLatestCompletedMonthEnd(nowUtc);
        var specs = new List<DemoPeriodSpec>(count);
        for (var index = 0; index < count; index++)
        {
            var reportingDate = latestCompletedMonthEnd.AddMonths(-((count - 1) - index));
            specs.Add(new DemoPeriodSpec(
                Index: index,
                Year: reportingDate.Year,
                Month: reportingDate.Month,
                Quarter: ((reportingDate.Month - 1) / 3) + 1,
                Frequency: "Computed",
                ReportingDate: reportingDate,
                SubmittedAt: reportingDate.AddDays(12),
                IsCurrent: index == count - 1));
        }

        return specs;
    }

    private static List<DemoPeriodSpec> BuildQuarterlyPeriodSpecs(DateTime nowUtc, int count)
    {
        var latestCompletedQuarterEnd = ResolveLatestCompletedQuarterEnd(nowUtc);
        var specs = new List<DemoPeriodSpec>(count);
        for (var index = 0; index < count; index++)
        {
            var reportingDate = latestCompletedQuarterEnd.AddMonths(-3 * ((count - 1) - index));
            specs.Add(new DemoPeriodSpec(
                Index: index,
                Year: reportingDate.Year,
                Month: reportingDate.Month,
                Quarter: ((reportingDate.Month - 1) / 3) + 1,
                Frequency: "Quarterly",
                ReportingDate: reportingDate,
                SubmittedAt: reportingDate.AddDays(18),
                IsCurrent: index == count - 1));
        }

        return specs;
    }

    private static DateTime ResolveLatestCompletedMonthEnd(DateTime utcNow)
    {
        var thisMonthEnd = new DateTime(
            utcNow.Year,
            utcNow.Month,
            DateTime.DaysInMonth(utcNow.Year, utcNow.Month));

        return utcNow.Date >= thisMonthEnd
            ? thisMonthEnd
            : thisMonthEnd.AddMonths(-1);
    }

    private static DateTime ResolveLatestCompletedQuarterEnd(DateTime utcNow)
    {
        var currentQuarterEndMonth = (((utcNow.Month - 1) / 3) + 1) * 3;
        var currentQuarterEnd = new DateTime(
            utcNow.Year,
            currentQuarterEndMonth,
            DateTime.DaysInMonth(utcNow.Year, currentQuarterEndMonth));

        return utcNow.Date >= currentQuarterEnd
            ? currentQuarterEnd
            : currentQuarterEnd.AddMonths(-3);
    }

    private static DateTime ResolveLatestCompletedSemiAnnualEnd(DateTime utcNow)
    {
        var currentSemiAnnualEndMonth = utcNow.Month <= 6 ? 6 : 12;
        var currentSemiAnnualEnd = new DateTime(
            utcNow.Year,
            currentSemiAnnualEndMonth,
            DateTime.DaysInMonth(utcNow.Year, currentSemiAnnualEndMonth));

        if (utcNow.Date >= currentSemiAnnualEnd)
        {
            return currentSemiAnnualEnd;
        }

        return currentSemiAnnualEndMonth == 6
            ? new DateTime(utcNow.Year - 1, 12, 31)
            : new DateTime(utcNow.Year, 6, 30);
    }

    private static int ResolveDeadlineOffsetDays(Module module, string frequency)
    {
        if (module.DeadlineOffsetDays.HasValue)
        {
            return module.DeadlineOffsetDays.Value;
        }

        return frequency switch
        {
            "Monthly" => 30,
            "Quarterly" => 45,
            "SemiAnnual" => 60,
            _ => 30
        };
    }

    private static void NormalizeRecord(
        CachedTemplate template,
        ReturnDataRecord record,
        Institution institution,
        InstitutionSeedSpec spec,
        DateTime reportingDate,
        int institutionSequence)
    {
        var rows = record.Rows.ToList();
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            foreach (var field in template.CurrentVersion.Fields)
            {
                var normalized = field.FieldName.Trim().ToLowerInvariant();
                var coordinated = TryResolveCoordinatedValue(template.ReturnCode, field, reportingDate);
                if (coordinated is not null)
                {
                    row.SetValue(field.FieldName, coordinated);
                    ApplyFieldConstraints(field, row, reportingDate);
                    continue;
                }

                if (normalized is "reporting_year")
                {
                    row.SetValue(field.FieldName, reportingDate.Year);
                    continue;
                }

                if (normalized is "reporting_month")
                {
                    row.SetValue(field.FieldName, reportingDate.Month);
                    continue;
                }

                if (normalized is "reporting_quarter")
                {
                    row.SetValue(field.FieldName, ((reportingDate.Month - 1) / 3) + 1);
                    continue;
                }

                if (normalized is "return_code" or "returncode")
                {
                    row.SetValue(field.FieldName, template.ReturnCode);
                    continue;
                }

                if (normalized is "institution_code" or "institutioncode")
                {
                    row.SetValue(field.FieldName, CoerceText(field, institution.InstitutionCode));
                    continue;
                }

                if (normalized.Contains("institution", StringComparison.OrdinalIgnoreCase)
                    && normalized.Contains("name", StringComparison.OrdinalIgnoreCase))
                {
                    row.SetValue(field.FieldName, CoerceText(field, institution.InstitutionName));
                    continue;
                }

                if (field.DataType == FieldDataType.Date)
                {
                    row.SetValue(field.FieldName, reportingDate.Date);
                    continue;
                }

                if (field.DataType == FieldDataType.Text)
                {
                    if (normalized.Contains("email", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, spec.ContactEmail));
                        continue;
                    }

                    if (normalized.Contains("phone", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, spec.ContactPhone));
                        continue;
                    }

                    if (normalized.Contains("address", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, spec.Address));
                        continue;
                    }

                    if (normalized.Contains("city", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, "Lagos"));
                        continue;
                    }

                    if (normalized.Contains("state", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, "Lagos"));
                        continue;
                    }

                    if (normalized.Contains("country", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, "NG"));
                        continue;
                    }

                    if (normalized.Contains("currency", StringComparison.OrdinalIgnoreCase))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, "NGN"));
                        continue;
                    }
                }

                if (string.Equals(template.ReturnCode, "BDC_BRN", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyBdcBranchOverrides(field, row, institution, reportingDate, institutionSequence, rowIndex);
                }

                var current = row.GetValue(field.FieldName);
                if (current is string text)
                {
                    var next = text
                        .Replace("ACCESSBA", institution.InstitutionCode, StringComparison.OrdinalIgnoreCase)
                        .Replace("CASHCODE", institution.InstitutionCode, StringComparison.OrdinalIgnoreCase)
                        .Replace("Access Bank Plc", institution.InstitutionName, StringComparison.OrdinalIgnoreCase)
                        .Replace("CASHCODE BDC LTD", institution.InstitutionName, StringComparison.OrdinalIgnoreCase);

                    if (!ReferenceEquals(next, text) || !string.Equals(next, text, StringComparison.Ordinal))
                    {
                        row.SetValue(field.FieldName, CoerceText(field, next));
                    }
                }

                ApplyFieldConstraints(field, row, reportingDate);
            }
        }
    }

    private static void ApplyBdcBranchOverrides(
        TemplateField field,
        ReturnDataRow row,
        Institution institution,
        DateTime reportingDate,
        int institutionSequence,
        int rowIndex)
    {
        var normalized = field.FieldName.Trim().ToLowerInvariant();
        var branchSuffix = $"{(institutionSequence % 90) + 10}{rowIndex + 1:0}";

        if (normalized == "branch_code")
        {
            var branchCode = $"{institution.InstitutionCode[..Math.Min(3, institution.InstitutionCode.Length)].ToUpperInvariant()}BR{branchSuffix}";
            if (field.MaxLength.HasValue && branchCode.Length > field.MaxLength.Value)
            {
                branchCode = branchCode[..field.MaxLength.Value];
            }

            row.SetValue(field.FieldName, branchCode);
            return;
        }

        if (normalized == "branch_name")
        {
            row.SetValue(field.FieldName, $"{institution.InstitutionCode} Branch {rowIndex + 1}");
            return;
        }

        if (normalized == "branch_address")
        {
            row.SetValue(field.FieldName, $"{20 + institutionSequence} Marina, Lagos");
            return;
        }

        if (normalized == "head_office_branch_fx_volume")
        {
            if (row.GetDecimal("branch_fx_volume") is { } volume)
            {
                row.SetValue(field.FieldName, volume);
            }
            return;
        }

        if (normalized == "other_branches_fx_volume")
        {
            row.SetValue(field.FieldName, 0m);
            return;
        }

        if (normalized == "total_branch_fx_volume")
        {
            if (row.GetDecimal("branch_fx_volume") is { } volume)
            {
                row.SetValue(field.FieldName, volume);
            }
            return;
        }

        if (normalized == "active_branches")
        {
            row.SetValue(field.FieldName, 1);
            return;
        }

        if (normalized == "inactive_branches")
        {
            row.SetValue(field.FieldName, 0);
            return;
        }

        if (normalized == "total_branches")
        {
            row.SetValue(field.FieldName, 1);
            return;
        }

        if (normalized == "branch_fx_volume" && row.GetDecimal(field.FieldName) is null)
        {
            row.SetValue(field.FieldName, 12_500_000m + (institutionSequence * 125_000m) + (reportingDate.Month * 1_000m));
        }
    }

    private static string CoerceText(TemplateField field, string value)
    {
        if (field.MaxLength.HasValue && value.Length > field.MaxLength.Value)
        {
            return value[..field.MaxLength.Value];
        }

        return value;
    }

    private static void ApplyFieldConstraints(TemplateField field, ReturnDataRow row, DateTime reportingDate)
    {
        var current = row.GetValue(field.FieldName);

        if (current is null && !string.IsNullOrWhiteSpace(field.DefaultValue))
        {
            current = ConvertStringValue(field, field.DefaultValue);
            if (current is not null)
            {
                row.SetValue(field.FieldName, current);
            }
        }

        if (field.DataType == FieldDataType.Text)
        {
            var text = row.GetString(field.FieldName) ?? string.Empty;
            var allowed = ParseAllowedValues(field.AllowedValues);
            if (allowed.Count > 0 && !allowed.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                text = allowed[0];
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                row.SetValue(field.FieldName, CoerceText(field, text));
            }

            return;
        }

        if (field.DataType == FieldDataType.Date && row.GetDateTime(field.FieldName) is null)
        {
            row.SetValue(field.FieldName, reportingDate.Date);
        }
    }

    private static object? TryResolveCoordinatedValue(string returnCode, TemplateField field, DateTime reportingDate)
    {
        var key = $"{returnCode}:{field.FieldName.Trim()}";
        if (!CoordinatedFieldKeys.Contains(key))
        {
            return null;
        }

        var normalized = field.FieldName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "branch_fx_volume" or "head_office_branch_fx_volume" or "total_branch_fx_volume" or "total_buying_volume" or "fx_trading_income_basis"
                => 260_000m,
            "total_assets" when string.Equals(returnCode, "BDC_CAP", StringComparison.OrdinalIgnoreCase)
                => 3_000_000m,
            "total_assets" when string.Equals(returnCode, "BDC_FIN", StringComparison.OrdinalIgnoreCase)
                => 3_000_000m,
            _ => null
        };
    }

    private static List<string> ParseAllowedValues(string? allowedValues)
    {
        if (string.IsNullOrWhiteSpace(allowedValues))
        {
            return [];
        }

        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(allowedValues);
            if (parsed is { Count: > 0 })
            {
                return parsed
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Fall through to delimiter parsing.
        }

        return allowedValues
            .Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static object? ConvertStringValue(TemplateField field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return field.DataType switch
        {
            FieldDataType.Integer => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var integer)
                ? integer
                : 0,
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var decimalValue)
                ? decimalValue
                : 0m,
            FieldDataType.Boolean => bool.TryParse(value, out var boolValue) && boolValue,
            FieldDataType.Date => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue)
                ? dateValue.Date
                : DateTime.UtcNow.Date,
            _ => value.Trim()
        };
    }

    private static ReturnDataRecord CloneRecord(ReturnDataRecord record)
    {
        var clone = new ReturnDataRecord(record.ReturnCode, record.TemplateVersionId, record.Category);
        foreach (var row in record.Rows)
        {
            var rowClone = new ReturnDataRow
            {
                RowKey = row.RowKey
            };

            foreach (var field in row.AllFields)
            {
                rowClone.SetValue(field.Key, field.Value);
            }

            clone.AddRow(rowClone);
        }

        return clone;
    }

    private static ValidationError CloneValidationError(ValidationError error)
        => new()
        {
            RuleId = error.RuleId,
            Field = error.Field,
            Message = error.Message,
            Severity = error.Severity,
            Category = error.Category,
            ExpectedValue = error.ExpectedValue,
            ActualValue = error.ActualValue,
            ReferencedReturnCode = error.ReferencedReturnCode
        };

    private static string FormatValidationErrors(IEnumerable<ValidationError> errors)
        => string.Join("; ", errors.Select(error =>
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(error.ActualValue))
            {
                details.Add($"actual={error.ActualValue}");
            }

            if (!string.IsNullOrWhiteSpace(error.ExpectedValue))
            {
                details.Add($"expected={error.ExpectedValue}");
            }

            if (!string.IsNullOrWhiteSpace(error.ReferencedReturnCode))
            {
                details.Add($"refs={error.ReferencedReturnCode}");
            }

            return details.Count == 0
                ? $"{error.RuleId}:{error.Message}"
                : $"{error.RuleId}:{error.Message} ({string.Join(", ", details)})";
        }));

    private static string BuildXml(
        CachedTemplate template,
        ReturnDataRecord record,
        string institutionCode,
        DateTime reportingDate)
    {
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();
        XNamespace ns = template.XmlNamespace;

        var root = new XElement(ns + template.XmlRootElement,
            new XElement(ns + "Header",
                new XElement(ns + "InstitutionCode", institutionCode),
                new XElement(ns + "ReportingDate", reportingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new XElement(ns + "ReturnCode", template.ReturnCode)));

        if (record.Category == StructuralCategory.FixedRow)
        {
            var dataElement = new XElement(ns + "Data");
            AppendFieldElements(dataElement, record.Rows.FirstOrDefault(), fields, ns);
            root.Add(dataElement);
        }
        else
        {
            var rowsElement = new XElement(ns + "Rows");
            foreach (var row in record.Rows)
            {
                var rowElement = new XElement(ns + "Row");
                AppendFieldElements(rowElement, row, fields, ns);
                rowsElement.Add(rowElement);
            }

            root.Add(rowsElement);
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }

    private static void AppendFieldElements(
        XElement parent,
        ReturnDataRow? row,
        IReadOnlyList<TemplateField> fields,
        XNamespace ns)
    {
        if (row is null)
        {
            return;
        }

        foreach (var field in fields)
        {
            var value = row.GetValue(field.FieldName);
            if (value is null && !field.IsRequired)
            {
                continue;
            }

            parent.Add(new XElement(ns + field.XmlElementName, FormatXmlValue(value)));
        }
    }

    private static string FormatXmlValue(object? value)
        => value switch
        {
            null => string.Empty,
            DateTime dateValue => dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "true" : "false",
            decimal decimalValue => decimalValue.ToString("0.######", CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString("0.######", CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString("0.######", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private async Task<List<ValidationError>> ValidateXsdAsync(string returnCode, string xml, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        var schemaSet = await _xsdGenerator.GenerateSchema(returnCode, ct);
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemaSet,
            Async = true
        };

        settings.ValidationEventHandler += (_, args) =>
        {
            errors.Add(new ValidationError
            {
                RuleId = "XSD",
                Field = "XML",
                Message = args.Message,
                Severity = args.Severity == System.Xml.Schema.XmlSeverityType.Error
                    ? ValidationSeverity.Error
                    : ValidationSeverity.Warning,
                Category = ValidationCategory.Schema
            });
        };

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var reader = XmlReader.Create(stream, settings);
        while (await reader.ReadAsync()) { }
        return errors;
    }

    private async Task<ReturnDataRecord> ParseXmlAsync(string returnCode, string xml, CancellationToken ct)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return await _xmlParser.Parse(stream, returnCode, ct);
    }

    private async Task<ValidationReport> ValidateRecordAsync(
        ReturnDataRecord record,
        string returnCode,
        int institutionId,
        int returnPeriodId,
        CancellationToken ct)
    {
        var submission = Submission.Create(institutionId, returnPeriodId, returnCode, tenantId: null);
        var tenantId = await _db.Institutions
            .Where(x => x.Id == institutionId)
            .Select(x => x.TenantId)
            .FirstAsync(ct);
        submission.TenantId = tenantId;
        return await _validationOrchestrator.Validate(record, submission, institutionId, returnPeriodId, ct);
    }

    private static DemoCredentialGroup BuildCredentialGroup(
        Institution institution,
        InstitutionUser adminUser,
        int templateCount,
        int periodCount,
        string sharedPassword)
    {
        var group = new DemoCredentialGroup
        {
            Audience = institution.LicenseType ?? "Institution",
            LoginUrl = InstitutionLoginUrl,
            InstitutionCode = institution.InstitutionCode,
            InstitutionName = institution.InstitutionName,
            LicenseType = institution.LicenseType ?? "Unknown",
            Notes = $"Bulk demo tenant seeded across {templateCount} templates and {periodCount} reporting periods for this demo pack."
        };

        group.Accounts.Add(new DemoCredentialAccount
        {
            Audience = institution.InstitutionCode,
            LoginUrl = InstitutionLoginUrl,
            InstitutionCode = institution.InstitutionCode,
            InstitutionName = institution.InstitutionName,
            Username = adminUser.Username,
            Email = adminUser.Email,
            DisplayName = adminUser.DisplayName,
            Role = adminUser.Role.ToString(),
            Password = sharedPassword,
            MfaRequired = false
        });

        return group;
    }

    private async Task ClearTenantSessionContextAsync(CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            await _db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context @key=N'TenantId', @value=NULL;", ct);
        }
    }
}

public sealed class BulkInstitutionDemoSeedResult
{
    public int BdcInstitutionsProcessed { get; init; }
    public int DmbInstitutionsProcessed { get; init; }
    public int FcInstitutionsProcessed { get; init; }
    public int MfbInstitutionsProcessed { get; init; }
    public int OverlayInstitutionsProcessed { get; init; }
    public int InstitutionsCreated { get; set; }
    public int PeriodsCreated { get; set; }
    public int SubmissionsCreated { get; set; }
    public DemoCredentialSeedResult Credentials { get; init; } = new();
}

internal sealed record InstitutionSeedSpec(
    string LicenceTypeCode,
    string InstitutionCode,
    string InstitutionName,
    string TenantSlug,
    string AdminUsername,
    string AdminEmail,
    string AdminDisplayName,
    string ContactEmail,
    string ContactPhone,
    string Address,
    string City,
    string State,
    DemoInstitutionPersona Persona);

internal sealed record DemoPeriodSpec(
    int Index,
    int Year,
    int Month,
    int Quarter,
    string Frequency,
    DateTime ReportingDate,
    DateTime SubmittedAt,
    bool IsCurrent);

internal sealed record TemplateSeedBatch(
    IReadOnlyList<CachedTemplate> Templates,
    Module Module,
    IReadOnlyDictionary<string, string> SampleXmlMap,
    IReadOnlyList<DemoPeriodSpec> PeriodSpecs);

internal sealed class PhysicalTextColumnLength
{
    public string ColumnName { get; set; } = string.Empty;
    public short MaxLengthBytes { get; set; }
    public string DataType { get; set; } = string.Empty;
}

internal sealed record ValidatedSubmissionPrototype(
    ReturnDataRecord Record,
    IReadOnlyList<ValidationError> ValidationErrors);

internal sealed record EnsuredInstitutionResult(
    Institution Institution,
    InstitutionUser AdminUser,
    bool InstitutionCreated);

internal sealed record EnsuredPeriodsResult(
    IReadOnlyList<ReturnPeriod> Periods,
    int CreatedCount);

internal sealed record InstitutionSeedExecutionResult(
    bool InstitutionCreated,
    int PeriodsCreated,
    int SubmissionsCreated,
    DemoCredentialGroup CredentialGroup);

internal sealed record SeededSubmissionArtifact(
    int SubmissionId,
    string ReturnCode,
    string Frequency,
    bool IsCurrentPeriod,
    SubmissionStatus Status,
    DateTime SubmittedAt);

internal sealed record InstitutionSignalProfile(
    decimal FilingTimeliness,
    decimal DataQuality,
    decimal RegulatoryCapital,
    decimal AuditGovernance,
    decimal Engagement,
    int OpenIncidentCount,
    int OpenAlertCount,
    int ModelReviewCount,
    decimal WeeklyTrendDelta,
    decimal OverallScore = 0m,
    int Rating = 0);

internal enum DemoInstitutionPersona
{
    Baseline,
    FilingWatch,
    ReturnedSubmission,
    CapitalFragile,
    ResilienceStress,
    CyberElevated,
    RecoveryTrack,
    CompoundPressure
}

internal enum SubmissionSeedDisposition
{
    Accepted,
    AcceptedWithWarnings,
    Returned,
    Missing
}
