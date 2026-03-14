using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed partial class RegulatorIntentClassifier : IRegulatorIntentClassifier
{
    private static readonly IReadOnlyDictionary<string, string> StaticFieldSynonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["car"] = "carratio",
        ["capital adequacy"] = "carratio",
        ["capital adequacy ratio"] = "carratio",
        ["npl"] = "nplratio",
        ["npl ratio"] = "nplratio",
        ["non performing loans"] = "nplratio",
        ["liquidity"] = "liquidityratio",
        ["liquidity ratio"] = "liquidityratio",
        ["lcr"] = "liquidityratio",
        ["loan to deposit ratio"] = "loandepositratio",
        ["ldr"] = "loandepositratio",
        ["roa"] = "roa",
        ["return on assets"] = "roa",
        ["total assets"] = "totalassets",
        ["assets"] = "totalassets"
    };

    private static readonly string[] ComparisonMarkers = [" vs ", " versus ", " against "];
    private static readonly string[] PronounTokens = ["their", "them", "they", "it", "its", "the bank", "that bank", "the institution", "that institution"];

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ILlmService _llmService;
    private readonly IRegulatorIntelligenceService _regulatorIntelligenceService;
    private readonly ILogger<RegulatorIntentClassifier> _logger;

    public RegulatorIntentClassifier(
        IDbContextFactory<MetadataDbContext> dbFactory,
        ILlmService llmService,
        IRegulatorIntelligenceService regulatorIntelligenceService,
        ILogger<RegulatorIntentClassifier> logger)
    {
        _dbFactory = dbFactory;
        _llmService = llmService;
        _regulatorIntelligenceService = regulatorIntelligenceService;
        _logger = logger;
    }

    public async Task<RegulatorIntentResult> ClassifyAsync(
        string userQuery,
        RegulatorContext context,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);
        ArgumentNullException.ThrowIfNull(context);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var regulatorCode = NormalizeRegulatorCode(context.RegulatorCode);
        var effectiveQuery = await ResolvePronounsAsync(db, userQuery, context, regulatorCode, ct);
        var normalizedQuery = NormalizeText(effectiveQuery);
        var periodCode = ExtractPeriodCode(effectiveQuery);
        var licenceCategory = ResolveLicenceCategory(normalizedQuery, regulatorCode);
        var fieldCode = await ResolveFieldCodeAsync(db, normalizedQuery, regulatorCode, ct);
        var requestedCount = ExtractRequestedCount(normalizedQuery);
        var ascending = IsAscendingRanking(normalizedQuery);

        var entityResolution = await ResolveEntitiesAsync(
            db,
            effectiveQuery,
            normalizedQuery,
            regulatorCode,
            licenceCategory,
            periodCode,
            context,
            ct);

        var result = BuildDeterministicResult(
            effectiveQuery,
            normalizedQuery,
            regulatorCode,
            periodCode,
            licenceCategory,
            fieldCode,
            requestedCount,
            ascending,
            entityResolution,
            context);

        if (result.Confidence < 0.70m)
        {
            result = await ApplyLlmFallbackAsync(
                db,
                effectiveQuery,
                regulatorCode,
                periodCode,
                licenceCategory,
                fieldCode,
                requestedCount,
                ascending,
                entityResolution,
                result,
                ct);
        }

        var effectiveEntityResolution = ShouldPreferCurrentExaminationEntity(
            result.IntentCode,
            normalizedQuery,
            context,
            entityResolution)
            ? EntityResolution.Empty
            : entityResolution;

        if (!ShouldApplyEntityDisambiguation(result.IntentCode))
        {
            result.NeedsDisambiguation = false;
            result.DisambiguationOptions = null;
        }
        else if (effectiveEntityResolution.NeedsDisambiguation)
        {
            result.NeedsDisambiguation = true;
            result.DisambiguationOptions = effectiveEntityResolution.DisambiguationOptions;
        }

        ApplyResolvedEntities(result, effectiveEntityResolution);
        ApplyDefaultParameters(result, regulatorCode, requestedCount, ascending);

        return result;
    }

    private static RegulatorIntentResult BuildDeterministicResult(
        string effectiveQuery,
        string normalizedQuery,
        string regulatorCode,
        string? periodCode,
        string? licenceCategory,
        string? fieldCode,
        int requestedCount,
        bool ascending,
        EntityResolution entityResolution,
        RegulatorContext context)
    {
        var result = new RegulatorIntentResult
        {
            IntentCode = "UNCLEAR",
            Confidence = 0.35m,
            PeriodCode = periodCode,
            FieldCode = fieldCode,
            LicenceCategory = licenceCategory
        };

        if (ContainsAny(normalizedQuery, "help", "what can i ask", "how does this work", "usage examples"))
        {
            result.IntentCode = "HELP";
            result.Confidence = 0.99m;
            return result;
        }

        if (CircularReferenceRegex().IsMatch(effectiveQuery) || ContainsAny(normalizedQuery, "circular", "guideline", "regulation", "what does", "requirement"))
        {
            result.IntentCode = "REGULATORY_LOOKUP";
            result.Confidence = 0.95m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "examination briefing", "exam briefing", "briefing for", "examination brief", "prepare examination"))
        {
            result.IntentCode = "EXAMINATION_BRIEF";
            result.Confidence = 0.98m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "validation hotspot", "validation hotspots", "validation error hotspot", "error hotspot", "cross entity validation"))
        {
            result.IntentCode = "VALIDATION_HOTSPOT";
            result.Confidence = 0.96m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "policy impact", "policy simulation", "scenario impact", "crr change", "crr increase", "crr", "ldr impact", "capital policy"))
        {
            result.IntentCode = "POLICY_IMPACT";
            result.Confidence = 0.95m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "cross border", "cross-border", "pan african", "group divergence", "harmonisation", "consolidation"))
        {
            result.IntentCode = "CROSS_BORDER";
            result.Confidence = 0.95m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "supervisory action", "supervisory actions", "action backlog", "open actions", "overdue actions"))
        {
            result.IntentCode = "SUPERVISORY_ACTIONS";
            result.Confidence = 0.94m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "sanctions", "watchlist", "aml screening", "pep exposure", "screening exposure"))
        {
            result.IntentCode = "SANCTIONS_EXPOSURE";
            result.Confidence = 0.96m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "stress test", "stress scenario", "stress scenarios", "shock scenario"))
        {
            result.IntentCode = "STRESS_SCENARIOS";
            result.Confidence = 0.96m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "contagion", "cascade", "spillover", "fails", "failure scenario", "domino effect"))
        {
            result.IntentCode = "CONTAGION_QUERY";
            result.Confidence = 0.97m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "systemic risk", "systemic dashboard", "financial stability", "fsi", "system wide risk", "system-wide risk"))
        {
            result.IntentCode = "SYSTEMIC_DASHBOARD";
            result.Confidence = 0.96m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "early warning", "ewi", "warning flags", "warning status"))
        {
            result.IntentCode = "EWI_STATUS";
            result.Confidence = 0.95m;
            return result;
        }

        if (ContainsAny(
                normalizedQuery,
                "sector health summary",
                "sector health",
                "sector summary",
                "sector overview",
                "sector snapshot",
                "overview of the sector",
                "current sector overview"))
        {
            result.IntentCode = "SECTOR_SUMMARY";
            result.Confidence = 0.97m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "when is the next filing due", "next filing due", "next return due", "upcoming deadline", "filing deadline"))
        {
            result.IntentCode = "DEADLINE";
            result.Confidence = 0.95m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "are we compliant", "compliance status", "compliant"))
        {
            result.IntentCode = "COMPLIANCE_STATUS";
            result.Confidence = 0.93m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "anomaly status", "anomaly report", "outlier", "data quality"))
        {
            result.IntentCode = "ANOMALY_STATUS";
            result.Confidence = 0.93m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "compliance health", "chs", "health score"))
        {
            result.IntentCode = requestedCount > 1 || ContainsAny(normalizedQuery, "rank", "ranking", "top", "bottom", "all")
                ? "CHS_RANKING"
                : entityResolution.Entities.Count > 0 || context.CurrentExaminationEntityId.HasValue
                    ? "CHS_ENTITY"
                    : "COMPLIANCE_STATUS";
            result.Confidence = 0.95m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "filing delinquency", "timeliness ranking", "late filing ranking", "filing timeliness"))
        {
            result.IntentCode = "FILING_DELINQUENCY";
            result.Confidence = 0.96m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "overdue return", "overdue returns", "pending return", "pending returns", "filing status", "who has overdue", "which banks have overdue"))
        {
            result.IntentCode = "FILING_STATUS";
            result.Confidence = 0.95m;
            return result;
        }

        if ((QuarterPeriodRegex().Matches(effectiveQuery).Count >= 2 || MonthPeriodRegex().Matches(effectiveQuery).Count >= 2) &&
            ContainsAny(normalizedQuery, "compare", "difference", "versus", " vs ", "between"))
        {
            result.IntentCode = "COMPARISON_PERIOD";
            result.Confidence = 0.91m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "rank", "ranking", "top ", "bottom "))
        {
            if (ContainsAny(normalizedQuery, "anomaly", "quality"))
            {
                result.IntentCode = "RISK_RANKING";
                result.Confidence = 0.93m;
                return result;
            }

            result.IntentCode = "TOP_N_RANKING";
            result.Confidence = fieldCode is null ? 0.68m : 0.92m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "compare", " versus ", " vs ", "against"))
        {
            result.IntentCode = entityResolution.Entities.Count >= 2 ? "ENTITY_COMPARE" : "COMPARISON_PEER";
            result.Confidence = entityResolution.Entities.Count >= 2 ? 0.95m : 0.76m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "sector trend", "sector history", "sector movement", "across the sector", "over the last", "over the past") &&
            fieldCode is not null &&
            (licenceCategory is not null || ContainsAny(normalizedQuery, "sector", "banks", "institutions")))
        {
            result.IntentCode = "SECTOR_TREND";
            result.Confidence = 0.92m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "sector average", "sector median", "aggregate", "across all", "sector wide average", "sector-wide average"))
        {
            result.IntentCode = "SECTOR_AGGREGATE";
            result.Confidence = fieldCode is null ? 0.68m : 0.91m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "profile", "full picture", "full profile", "full dossier", "overview of", "how is", "tell me about", "show me a profile") &&
            (entityResolution.Entities.Count > 0 || context.CurrentExaminationEntityId.HasValue))
        {
            result.IntentCode = "ENTITY_PROFILE";
            result.Confidence = 0.94m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "trend", "history", "movement", "last ", "past ") && fieldCode is not null)
        {
            result.IntentCode = entityResolution.Entities.Count > 0 ? "TREND" : "SECTOR_TREND";
            result.Confidence = 0.86m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "what if", "doubled", "tripled", "increase by", "decrease by", "shock"))
        {
            result.IntentCode = "SCENARIO";
            result.Confidence = 0.88m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "peer", "benchmark", "peer median", "peer average"))
        {
            result.IntentCode = "COMPARISON_PEER";
            result.Confidence = 0.86m;
            return result;
        }

        if (ContainsAny(normalizedQuery, "search", "find", "show all", "list validation", "list returns"))
        {
            result.IntentCode = "SEARCH";
            result.Confidence = 0.80m;
            return result;
        }

        if ((entityResolution.Entities.Count > 0 || context.CurrentExaminationEntityId.HasValue) && fieldCode is not null)
        {
            result.IntentCode = "CURRENT_VALUE";
            result.Confidence = 0.84m;
        }

        return result;
    }

    private async Task<RegulatorIntentResult> ApplyLlmFallbackAsync(
        MetadataDbContext db,
        string effectiveQuery,
        string regulatorCode,
        string? periodCode,
        string? licenceCategory,
        string? fieldCode,
        int requestedCount,
        bool ascending,
        EntityResolution entityResolution,
        RegulatorIntentResult current,
        CancellationToken ct)
    {
        try
        {
            var llmResult = await _llmService.CompleteStructuredAsync<LlmIntentEnvelope>(
                new LlmRequest
                {
                    SystemPrompt = BuildClassificationSystemPrompt(),
                    UserMessage = BuildClassificationUserMessage(effectiveQuery, regulatorCode, periodCode, licenceCategory, fieldCode),
                    Temperature = 0.0m,
                    MaxTokens = 1200,
                    ResponseFormat = "json"
                },
                ct);

            var result = new RegulatorIntentResult
            {
                IntentCode = string.IsNullOrWhiteSpace(llmResult.IntentCode) ? current.IntentCode : llmResult.IntentCode.Trim().ToUpperInvariant(),
                Confidence = llmResult.Confidence <= 0m ? current.Confidence : llmResult.Confidence,
                PeriodCode = llmResult.PeriodCode ?? periodCode,
                FieldCode = llmResult.FieldCode ?? fieldCode,
                LicenceCategory = llmResult.LicenceCategory ?? licenceCategory,
                NeedsDisambiguation = llmResult.NeedsDisambiguation,
                DisambiguationOptions = llmResult.DisambiguationOptions
            };

            if (requestedCount > 1)
            {
                result.ExtractedParameters["limit"] = requestedCount.ToString(CultureInfo.InvariantCulture);
            }

            if (ascending)
            {
                result.ExtractedParameters["direction"] = "ASC";
            }

            if (llmResult.ExtractedParameters is not null)
            {
                foreach (var pair in llmResult.ExtractedParameters)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                    {
                        result.ExtractedParameters[pair.Key] = pair.Value;
                    }
                }
            }

            var resolvedFromLlm = entityResolution.Entities;
            if (llmResult.EntityNames?.Count > 0)
            {
                var llmEntityResolution = await ResolveEntitiesByNamesAsync(db, llmResult.EntityNames, regulatorCode, result.LicenceCategory, ct);
                if (llmEntityResolution.Entities.Count > 0)
                {
                    resolvedFromLlm = llmEntityResolution.Entities;
                    result.NeedsDisambiguation |= llmEntityResolution.NeedsDisambiguation;
                    result.DisambiguationOptions = llmEntityResolution.DisambiguationOptions ?? result.DisambiguationOptions;
                }
            }

            ApplyResolvedEntities(result, new EntityResolution(resolvedFromLlm, result.NeedsDisambiguation, result.DisambiguationOptions));
            ApplyDefaultParameters(result, regulatorCode, requestedCount, ascending);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM fallback failed for regulator intent classification. Falling back to deterministic result.");
            ApplyResolvedEntities(current, entityResolution);
            ApplyDefaultParameters(current, regulatorCode, requestedCount, ascending);
            return current;
        }
    }

    private static void ApplyResolvedEntities(RegulatorIntentResult result, EntityResolution entityResolution)
    {
        result.ResolvedEntityNames = entityResolution.Entities
            .Select(x => x.InstitutionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        result.ResolvedEntityIds = entityResolution.Entities
            .Select(x => x.TenantId)
            .Distinct()
            .ToList();
    }

    private static void ApplyDefaultParameters(RegulatorIntentResult result, string regulatorCode, int requestedCount, bool ascending)
    {
        result.ExtractedParameters["regulatorCode"] = regulatorCode;

        if (!string.IsNullOrWhiteSpace(result.FieldCode))
        {
            result.ExtractedParameters["fieldCode"] = result.FieldCode;
        }

        if (!string.IsNullOrWhiteSpace(result.PeriodCode))
        {
            result.ExtractedParameters["periodCode"] = result.PeriodCode;
        }

        if (!string.IsNullOrWhiteSpace(result.LicenceCategory))
        {
            result.ExtractedParameters["licenceCategory"] = result.LicenceCategory;
        }

        if (requestedCount > 1)
        {
            result.ExtractedParameters["limit"] = requestedCount.ToString(CultureInfo.InvariantCulture);
        }

        if (ascending)
        {
            result.ExtractedParameters["direction"] = "ASC";
        }

        if (result.ResolvedEntityNames.Count > 0)
        {
            result.ExtractedParameters["entityNames"] = string.Join(", ", result.ResolvedEntityNames);
        }
    }

    private async Task<EntityResolution> ResolveEntitiesAsync(
        MetadataDbContext db,
        string effectiveQuery,
        string normalizedQuery,
        string regulatorCode,
        string? licenceCategory,
        string? periodCode,
        RegulatorContext context,
        CancellationToken ct)
    {
        var directory = await LoadEntityDirectoryAsync(db, regulatorCode, licenceCategory, ct);
        if (directory.Count == 0)
        {
            return EntityResolution.Empty;
        }

        var groupResolution = await ResolveGroupEntitiesAsync(directory, normalizedQuery, regulatorCode, periodCode, ct);
        if (groupResolution.Entities.Count > 0)
        {
            return groupResolution;
        }

        var aliases = await db.RegIqEntityAliases
            .AsNoTracking()
            .Where(x => x.IsActive
                        && (string.IsNullOrWhiteSpace(licenceCategory) || x.LicenceCategory == licenceCategory)
                        && (string.IsNullOrWhiteSpace(regulatorCode) || x.RegulatorAgency == regulatorCode || regulatorCode == "NFIU"))
            .ToListAsync(ct);

        var compactQuery = NormalizeCompact(effectiveQuery);
        var exactMatches = new Dictionary<Guid, EntityResolutionItem>();

        foreach (var alias in aliases
                     .Where(x => compactQuery.Contains(x.NormalizedAlias, StringComparison.Ordinal))
                     .OrderByDescending(x => x.NormalizedAlias.Length))
        {
            foreach (var entity in ResolveAliasCandidates(alias, directory))
            {
                exactMatches[entity.TenantId] = new EntityResolutionItem(entity.TenantId, entity.InstitutionName, entity.LicenceCategory, 1.0m);
            }
        }

        var searchPhrases = ExtractEntitySearchPhrases(effectiveQuery, context);
        var resolved = exactMatches.Values.ToList();
        var disambiguationOptions = new List<string>();

        foreach (var phrase in searchPhrases)
        {
            var matches = FindEntityMatches(directory, aliases, phrase);
            if (matches.Count == 0)
            {
                continue;
            }

            if (matches.Count > 1 && matches[0].Score - matches[1].Score < 0.08m)
            {
                disambiguationOptions = matches.Take(3).Select(x => x.InstitutionName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            if (resolved.All(x => x.TenantId != matches[0].TenantId))
            {
                resolved.Add(matches[0]);
            }
        }

        if (exactMatches.Count > 0 && disambiguationOptions.Count == 0)
        {
            return new EntityResolution(
                resolved.OrderBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase).ToList(),
                false,
                null);
        }

        if (resolved.Count > 0)
        {
            return new EntityResolution(
                resolved.DistinctBy(x => x.TenantId).ToList(),
                disambiguationOptions.Count > 1,
                disambiguationOptions.Count > 1 ? disambiguationOptions : null);
        }

        if (context.CurrentExaminationEntityId.HasValue)
        {
            var entity = directory.FirstOrDefault(x => x.TenantId == context.CurrentExaminationEntityId.Value);
            if (entity is not null)
            {
                return new EntityResolution(
                [
                    new EntityResolutionItem(entity.TenantId, entity.InstitutionName, entity.LicenceCategory, 0.90m)
                ],
                false,
                null);
            }
        }

        return EntityResolution.Empty;
    }

    private async Task<EntityResolution> ResolveEntitiesByNamesAsync(
        MetadataDbContext db,
        IReadOnlyList<string> entityNames,
        string regulatorCode,
        string? licenceCategory,
        CancellationToken ct)
    {
        var directory = await LoadEntityDirectoryAsync(db, regulatorCode, licenceCategory, ct);
        var aliases = await db.RegIqEntityAliases
            .AsNoTracking()
            .Where(x => x.IsActive
                        && (string.IsNullOrWhiteSpace(licenceCategory) || x.LicenceCategory == licenceCategory)
                        && (string.IsNullOrWhiteSpace(regulatorCode) || x.RegulatorAgency == regulatorCode || regulatorCode == "NFIU"))
            .ToListAsync(ct);

        var entities = new List<EntityResolutionItem>();
        var disambiguation = new List<string>();

        foreach (var name in entityNames)
        {
            var matches = FindEntityMatches(directory, aliases, name);
            if (matches.Count == 0)
            {
                continue;
            }

            if (matches.Count > 1 && matches[0].Score - matches[1].Score < 0.08m)
            {
                disambiguation.AddRange(matches.Take(3).Select(x => x.InstitutionName));
            }

            entities.Add(matches[0]);
        }

        return new EntityResolution(
            entities.DistinctBy(x => x.TenantId).ToList(),
            disambiguation.Count > 1,
            disambiguation.Count > 1 ? disambiguation.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null);
    }

    private async Task<List<EntityDirectoryRow>> LoadEntityDirectoryAsync(
        MetadataDbContext db,
        string regulatorCode,
        string? licenceCategory,
        CancellationToken ct)
    {
        var licenceRows = await db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.IsActive && x.LicenceType != null)
            .ToListAsync(ct);

        var licenceMap = licenceRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.EffectiveDate).First());

        var institutions = await db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Join(
                db.Tenants.AsNoTracking().Where(x => x.Status == TenantStatus.Active && x.TenantType == TenantType.Institution),
                institution => institution.TenantId,
                tenant => tenant.TenantId,
                (institution, tenant) => new { institution, tenant })
            .ToListAsync(ct);

        return institutions
            .Select(x =>
            {
                licenceMap.TryGetValue(x.institution.TenantId, out var licenceRow);
                var licence = licenceRow?.LicenceType?.Code ?? x.institution.LicenseType ?? "UNKNOWN";
                var entityRegulator = licenceRow?.LicenceType?.Regulator ?? regulatorCode;
                return new EntityDirectoryRow(
                    x.institution.TenantId,
                    x.institution.Id,
                    x.institution.InstitutionName,
                    licence,
                    entityRegulator,
                    NormalizeCompact(x.institution.InstitutionName));
            })
            .Where(x => string.IsNullOrWhiteSpace(licenceCategory) || MatchesLicenceCategory(x.LicenceCategory, licenceCategory))
            .Where(x => string.IsNullOrWhiteSpace(regulatorCode) || regulatorCode == "NFIU" || string.Equals(x.RegulatorAgency, regulatorCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<EntityResolution> ResolveGroupEntitiesAsync(
        IReadOnlyList<EntityDirectoryRow> directory,
        string normalizedQuery,
        string regulatorCode,
        string? periodCode,
        CancellationToken ct)
    {
        if (ContainsAny(normalizedQuery, "tier 1 banks", "tier-1 banks"))
        {
            var topFive = await _regulatorIntelligenceService.RankEntitiesByMetricAsync("totalassets", regulatorCode, "DMB", periodCode, 5, false, ct);
            var entities = topFive
                .Select(x => new EntityResolutionItem(x.TenantId, x.InstitutionName, x.LicenceCategory, 1.0m))
                .ToList();
            return new EntityResolution(entities, false, null);
        }

        var groupCategory = normalizedQuery switch
        {
            _ when ContainsAny(normalizedQuery, "all commercial banks", "commercial banks", "all dmbs", "dmb sector", "which banks", "all banks") => "DMB",
            _ when ContainsAny(normalizedQuery, "all microfinance banks", "microfinance banks", "all mfbs", "mfb sector") => "MFB",
            _ when ContainsAny(normalizedQuery, "all insurers", "insurance sector", "insurers") => "INS",
            _ when ContainsAny(normalizedQuery, "capital market operators", "all cmos", "broker dealers", "broker dealers sector") => "CMO",
            _ when ContainsAny(normalizedQuery, "bureau de change", "all bdcs", "bdc sector") => "BDC",
            _ => null
        };

        if (groupCategory is null)
        {
            return EntityResolution.Empty;
        }

        var entitiesInGroup = directory
            .Where(x => MatchesLicenceCategory(x.LicenceCategory, groupCategory))
            .Select(x => new EntityResolutionItem(x.TenantId, x.InstitutionName, x.LicenceCategory, 1.0m))
            .ToList();

        return entitiesInGroup.Count == 0
            ? EntityResolution.Empty
            : new EntityResolution(entitiesInGroup, false, null);
    }

    private static List<EntityResolutionItem> FindEntityMatches(
        IReadOnlyList<EntityDirectoryRow> directory,
        IReadOnlyList<RegIqEntityAlias> aliases,
        string searchTerm)
    {
        var normalizedSearch = NormalizeCompact(searchTerm);
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return [];
        }

        var matches = new Dictionary<Guid, EntityResolutionItem>();

        foreach (var alias in aliases)
        {
            var score = ScoreTextMatch(normalizedSearch, alias.NormalizedAlias);
            if (score <= 0m)
            {
                continue;
            }

            foreach (var entity in ResolveAliasCandidates(alias, directory))
            {
                MergeCandidate(matches, new EntityResolutionItem(entity.TenantId, entity.InstitutionName, entity.LicenceCategory, score));
            }
        }

        foreach (var entity in directory)
        {
            var score = ScoreTextMatch(normalizedSearch, entity.NormalizedInstitutionName);
            if (score <= 0m)
            {
                continue;
            }

            MergeCandidate(matches, new EntityResolutionItem(entity.TenantId, entity.InstitutionName, entity.LicenceCategory, score));
        }

        return matches.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void MergeCandidate(IDictionary<Guid, EntityResolutionItem> matches, EntityResolutionItem candidate)
    {
        if (!matches.TryGetValue(candidate.TenantId, out var current) || candidate.Score > current.Score)
        {
            matches[candidate.TenantId] = candidate;
        }
    }

    private static List<EntityDirectoryRow> ResolveAliasCandidates(RegIqEntityAlias alias, IReadOnlyList<EntityDirectoryRow> directory)
    {
        var canonical = NormalizeCompact(alias.CanonicalName);
        var holding = NormalizeCompact(alias.HoldingCompanyName ?? string.Empty);

        return directory
            .Where(x => alias.TenantId.HasValue && x.TenantId == alias.TenantId.Value
                        || x.NormalizedInstitutionName == canonical
                        || x.NormalizedInstitutionName.Contains(canonical, StringComparison.Ordinal)
                        || (!string.IsNullOrWhiteSpace(holding) && x.NormalizedInstitutionName.Contains(holding, StringComparison.Ordinal)))
            .DistinctBy(x => x.TenantId)
            .ToList();
    }

    private async Task<string?> ResolveFieldCodeAsync(
        MetadataDbContext db,
        string normalizedQuery,
        string regulatorCode,
        CancellationToken ct)
    {
        var synonyms = await db.ComplianceIqFieldSynonyms
            .AsNoTracking()
            .Where(x => x.IsActive && (x.RegulatorCode == null || x.RegulatorCode == regulatorCode))
            .OrderByDescending(x => x.Synonym.Length)
            .ToListAsync(ct);

        foreach (var synonym in synonyms)
        {
            if (normalizedQuery.Contains(synonym.Synonym, StringComparison.OrdinalIgnoreCase))
            {
                return synonym.FieldCode;
            }
        }

        foreach (var pair in StaticFieldSynonyms.OrderByDescending(x => x.Key.Length))
        {
            if (normalizedQuery.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private async Task<string> ResolvePronounsAsync(
        MetadataDbContext db,
        string query,
        RegulatorContext context,
        string regulatorCode,
        CancellationToken ct)
    {
        if (!ContainsAny(NormalizeText(query), PronounTokens))
        {
            return query;
        }

        var entityName = context.RecentEntities.FirstOrDefault().Name;
        if (string.IsNullOrWhiteSpace(entityName) && context.CurrentExaminationEntityId.HasValue)
        {
            entityName = await db.Institutions
                .AsNoTracking()
                .Where(x => x.TenantId == context.CurrentExaminationEntityId.Value)
                .Select(x => x.InstitutionName)
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            return query;
        }

        var resolved = query;
        foreach (var token in PronounTokens)
        {
            resolved = Regex.Replace(resolved, $@"\b{Regex.Escape(token)}\b", entityName, RegexOptions.IgnoreCase);
        }

        return resolved;
    }

    private static List<string> ExtractEntitySearchPhrases(string query, RegulatorContext context)
    {
        var phrases = new List<string>();
        var lowered = query.Trim();

        foreach (var marker in ComparisonMarkers)
        {
            var idx = lowered.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var left = lowered[..idx];
                var right = lowered[(idx + marker.Length)..];
                phrases.Add(CleanEntityPhrase(left));
                phrases.Add(CleanEntityPhrase(right.Split(" on ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0]));
            }
        }

        if (lowered.StartsWith("compare ", StringComparison.OrdinalIgnoreCase))
        {
            var compareClause = lowered["compare ".Length..];
            var beforeMetric = compareClause.Split(" on ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
            if (beforeMetric.Contains(" and ", StringComparison.OrdinalIgnoreCase))
            {
                phrases.AddRange(beforeMetric.Split(" and ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(CleanEntityPhrase));
            }
        }

        foreach (var token in new[] { "profile of ", "briefing for ", "show me ", "show ", "about ", "for " })
        {
            var idx = lowered.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var fragment = lowered[(idx + token.Length)..];
                phrases.Add(CleanEntityPhrase(fragment.Split(" on ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0]));
            }
        }

        var stripped = Regex.Replace(
            lowered,
            @"\b(give|me|a|an|full|profile|show|tell|how|is|doing|what|about|their|its|the|institution|entity|sector|trend|rank|ranking|top|bottom|compare|versus|vs|and|on|for|with|current|latest|over|past|last|which|banks|bank|returns|return|filings|filing|status|q[1-4]|fy\d{4}|20\d{2})\b",
            " ",
            RegexOptions.IgnoreCase);
        phrases.Add(CleanEntityPhrase(stripped));

        if (!string.IsNullOrWhiteSpace(context.RecentEntities.FirstOrDefault().Name))
        {
            phrases.Add(context.RecentEntities[0].Name);
        }

        return phrases
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanEntityPhrase(string value)
    {
        var cleaned = Regex.Replace(value, @"[^A-Za-z0-9&\-\s]", " ");
        cleaned = Regex.Replace(cleaned, @"\b(compare|show|give|tell|profile|briefing|about|for|me)\b", " ", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    private static string NormalizeRegulatorCode(string? regulatorCode) =>
        string.IsNullOrWhiteSpace(regulatorCode) ? "CBN" : regulatorCode.Trim().ToUpperInvariant();

    private static string NormalizeText(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

    private static string NormalizeCompact(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);

    private static string? ResolveLicenceCategory(string normalizedQuery, string regulatorCode)
    {
        if (ContainsAny(normalizedQuery, "commercial bank", "commercial banks", "dmb", "deposit money bank", "tier 1 bank", "tier-1 bank"))
        {
            return "DMB";
        }

        if (ContainsAny(normalizedQuery, "microfinance", "mfb", "microfinance bank"))
        {
            return "MFB";
        }

        if (ContainsAny(normalizedQuery, "merchant bank", "merchant banks"))
        {
            return "MB";
        }

        if (ContainsAny(normalizedQuery, "non interest bank", "islamic bank", "nib"))
        {
            return "NIB";
        }

        if (ContainsAny(normalizedQuery, "insurer", "insurers", "insurance"))
        {
            return "INS";
        }

        if (ContainsAny(normalizedQuery, "capital market operator", "capital market operators", "cmo", "broker dealer"))
        {
            return "CMO";
        }

        if (ContainsAny(normalizedQuery, "bureau de change", "bdc"))
        {
            return "BDC";
        }

        return regulatorCode switch
        {
            "NAICOM" => "INS",
            "SEC" => "CMO",
            _ => null
        };
    }

    private static string? ExtractPeriodCode(string query)
    {
        var quarter = QuarterPeriodRegex().Match(query);
        if (quarter.Success)
        {
            var year = quarter.Groups[1].Success
                ? quarter.Groups[1].Value
                : quarter.Groups[3].Value;
            var q = quarter.Groups[2].Success
                ? quarter.Groups[2].Value
                : quarter.Groups[4].Value;
            return $"{year}-Q{q}";
        }

        var month = MonthPeriodRegex().Match(query);
        if (month.Success)
        {
            return $"{month.Groups[1].Value}-{month.Groups[2].Value}";
        }

        var fy = Regex.Match(query, @"\bFY\s*(20\d{2})\b", RegexOptions.IgnoreCase);
        return fy.Success ? fy.Groups[1].Value : null;
    }

    private static int ExtractRequestedCount(string normalizedQuery)
    {
        var top = Regex.Match(normalizedQuery, @"\b(?:top|bottom|rank|show)\s+(\d+)\b", RegexOptions.IgnoreCase);
        return top.Success
            ? Math.Clamp(int.Parse(top.Groups[1].Value, CultureInfo.InvariantCulture), 1, 50)
            : 0;
    }

    private static bool IsAscendingRanking(string normalizedQuery) =>
        ContainsAny(normalizedQuery, "bottom ", "lowest ", "least ", "worst ");

    private static decimal ScoreTextMatch(string normalizedSearch, string normalizedCandidate)
    {
        if (string.IsNullOrWhiteSpace(normalizedSearch) || string.IsNullOrWhiteSpace(normalizedCandidate))
        {
            return 0m;
        }

        if (normalizedCandidate == normalizedSearch)
        {
            return 1.0m;
        }

        if (normalizedCandidate.StartsWith(normalizedSearch, StringComparison.Ordinal)
            || normalizedSearch.StartsWith(normalizedCandidate, StringComparison.Ordinal))
        {
            return 0.95m;
        }

        if (normalizedCandidate.Contains(normalizedSearch, StringComparison.Ordinal)
            || normalizedSearch.Contains(normalizedCandidate, StringComparison.Ordinal))
        {
            return 0.88m;
        }

        var similarity = ComputeSimilarity(normalizedSearch, normalizedCandidate);
        return similarity >= 0.72m ? similarity : 0m;
    }

    private static decimal ComputeSimilarity(string left, string right)
    {
        var distance = LevenshteinDistance(left, right);
        var longest = Math.Max(left.Length, right.Length);
        if (longest == 0)
        {
            return 0m;
        }

        var ratio = 1m - ((decimal)distance / longest);
        return decimal.Round(Math.Max(0m, 0.70m + (ratio * 0.25m)), 4);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var costs = new int[right.Length + 1];
        for (var i = 0; i < costs.Length; i++)
        {
            costs[i] = i;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            costs[0] = i;
            var last = i - 1;

            for (var j = 1; j <= right.Length; j++)
            {
                var current = costs[j];
                var substitution = left[i - 1] == right[j - 1] ? last : last + 1;
                var insertion = costs[j] + 1;
                var deletion = costs[j - 1] + 1;
                costs[j] = Math.Min(substitution, Math.Min(insertion, deletion));
                last = current;
            }
        }

        return costs[right.Length];
    }

    private static bool MatchesLicenceCategory(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        || actual.Contains(expected, StringComparison.OrdinalIgnoreCase)
        || expected.Contains(actual, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string normalizedQuery, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (normalizedQuery.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldApplyEntityDisambiguation(string intentCode)
    {
        return intentCode.ToUpperInvariant() switch
        {
            "ENTITY_PROFILE" or "ENTITY_COMPARE" or "EXAMINATION_BRIEF" or "CURRENT_VALUE" or "TREND"
                or "COMPARISON_PEER" or "COMPARISON_PERIOD" or "COMPLIANCE_STATUS" or "ANOMALY_STATUS"
                or "CHS_ENTITY" or "CONTAGION_QUERY" or "SANCTIONS_EXPOSURE" or "FILING_STATUS"
                or "DEADLINE" or "SEARCH" => true,
            _ => false
        };
    }

    private static bool ShouldPreferCurrentExaminationEntity(
        string intentCode,
        string normalizedQuery,
        RegulatorContext context,
        EntityResolution entityResolution)
    {
        if (!context.CurrentExaminationEntityId.HasValue)
        {
            return false;
        }

        if (ContainsAny(
                normalizedQuery,
                "all ",
                "across",
                "sector",
                "systemic",
                "compare",
                " versus ",
                " vs ",
                "against",
                "ranking",
                "rank ",
                "top ",
                "bottom ",
                "peer",
                "cross border",
                "policy",
                "stress",
                "contagion"))
        {
            return false;
        }

        if (entityResolution.Entities.Any(x => x.Score >= 0.95m))
        {
            return false;
        }

        return intentCode.ToUpperInvariant() switch
        {
            "ENTITY_PROFILE" or "EXAMINATION_BRIEF" or "CURRENT_VALUE" or "TREND" or "COMPARISON_PERIOD"
                or "COMPARISON_PEER" or "COMPLIANCE_STATUS" or "ANOMALY_STATUS" or "CHS_ENTITY"
                or "SANCTIONS_EXPOSURE" or "FILING_STATUS" => true,
            _ => false
        };
    }

    private static string BuildClassificationUserMessage(
        string query,
        string regulatorCode,
        string? periodCode,
        string? licenceCategory,
        string? fieldCode)
    {
        return $$"""
        Query: {{query}}
        RegulatorCode: {{regulatorCode}}
        SuggestedPeriodCode: {{periodCode ?? "null"}}
        SuggestedLicenceCategory: {{licenceCategory ?? "null"}}
        SuggestedFieldCode: {{fieldCode ?? "null"}}

        Return JSON with:
        {
          "intentCode": "STRING",
          "confidence": 0.0,
          "entityNames": ["..."],
          "fieldCode": "string|null",
          "periodCode": "string|null",
          "licenceCategory": "string|null",
          "needsDisambiguation": false,
          "disambiguationOptions": ["..."],
          "extractedParameters": { "key": "value" }
        }
        """;
    }

    private static string BuildClassificationSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are RegulatorIQ's Nigerian financial supervisory intent classifier.");
        builder.AppendLine("Classify regulator questions into one supported intent code.");
        builder.AppendLine("Understand Nigerian regulators: CBN, NDIC, NAICOM, SEC, NFIU, PENCOM, FMBN.");
        builder.AppendLine("Understand Nigerian metrics: CAR, NPL, LCR, LDR, ROA, ROE, total assets, filing timeliness, CHS, EWI, sanctions exposure.");
        builder.AppendLine("Understand groups such as all commercial banks, DMB sector, MFB sector, insurers, CMO sector, tier-1 banks.");
        builder.AppendLine("Supported intents:");
        builder.AppendLine("- CURRENT_VALUE, TREND, COMPARISON_PEER, COMPARISON_PERIOD, DEADLINE, REGULATORY_LOOKUP, COMPLIANCE_STATUS, ANOMALY_STATUS, SCENARIO, SEARCH");
        builder.AppendLine("- SECTOR_SUMMARY, SECTOR_AGGREGATE, ENTITY_COMPARE, RISK_RANKING, HELP, ENTITY_PROFILE, SECTOR_TREND, TOP_N_RANKING, FILING_STATUS, FILING_DELINQUENCY, CHS_RANKING, CHS_ENTITY, EWI_STATUS, SYSTEMIC_DASHBOARD, CONTAGION_QUERY, STRESS_SCENARIOS, SANCTIONS_EXPOSURE, EXAMINATION_BRIEF, SUPERVISORY_ACTIONS, CROSS_BORDER, POLICY_IMPACT, VALIDATION_HOTSPOT");
        builder.AppendLine("Examples:");
        builder.AppendLine("- SECTOR_SUMMARY: Show sector health summary. Give me a sector overview. What does the current sector picture look like?");
        builder.AppendLine("- ENTITY_PROFILE: Give me a full profile of Access Bank. Tell me about Wema Bank. How is Zenith Bank doing?");
        builder.AppendLine("- ENTITY_COMPARE: Compare GTBank vs Zenith on CAR and NPL. Compare Access Bank and First Bank. GTBank versus UBA on liquidity.");
        builder.AppendLine("- SECTOR_AGGREGATE: What is average CAR across all commercial banks? Show sector median NPL for DMBs. Aggregate liquidity ratio across insurers.");
        builder.AppendLine("- SECTOR_TREND: Show sector NPL trend for DMBs over 2 years. How has sector CAR moved over the last 8 quarters? Trend of filing timeliness across MFBs.");
        builder.AppendLine("- TOP_N_RANKING: Top 5 banks by total assets. Rank DMBs by CAR. Bottom 10 institutions by liquidity ratio.");
        builder.AppendLine("- FILING_STATUS: Which banks have overdue returns? Show pending Q1 filings across MFBs. Who is late on the latest prudential return?");
        builder.AppendLine("- FILING_DELINQUENCY: Rank banks by filing timeliness. Show worst filing offenders in the sector. Bottom 10 by filing discipline.");
        builder.AppendLine("- CHS_RANKING: Rank all DMBs by compliance health score. Show CHS ranking for MFBs. Bottom 5 institutions by CHS.");
        builder.AppendLine("- CHS_ENTITY: What is Access Bank's compliance health score? Show Wema's CHS breakdown. Compliance health for Zenith Bank.");
        builder.AppendLine("- EWI_STATUS: Show current early warning flags. Which institutions have active EWIs? EWI status across the banking sector.");
        builder.AppendLine("- SYSTEMIC_DASHBOARD: Show me the systemic risk dashboard. What is the current financial stability picture? System-wide risk overview.");
        builder.AppendLine("- CONTAGION_QUERY: What happens if Access Bank fails? Show contagion around GTBank. Spillover analysis for Zenith Bank.");
        builder.AppendLine("- STRESS_SCENARIOS: List available stress scenarios. Show the latest deposit run stress test. Run the oil shock scenario.");
        builder.AppendLine("- SANCTIONS_EXPOSURE: Show sanctions exposure for Access Bank. Which entities have watchlist matches? AML screening exposure across DMBs.");
        builder.AppendLine("- EXAMINATION_BRIEF: Generate an examination briefing for Stanbic IBTC. Prepare an exam brief for Wema Bank. I need a supervisory briefing on Access Bank.");
        builder.AppendLine("- SUPERVISORY_ACTIONS: Show open supervisory actions. What actions are overdue? Supervisory action backlog for my portfolio.");
        builder.AppendLine("- CROSS_BORDER: Show cross-border divergence for GTCO group. Pan-African dashboard for banking groups. Consolidation intelligence for UBA group.");
        builder.AppendLine("- POLICY_IMPACT: Show policy impact of a CRR increase. Policy simulation results for LDR tightening. What happens if reserve requirements change?");
        builder.AppendLine("- VALIDATION_HOTSPOT: Show validation hotspots across banks. Which rules fail most often? Cross-entity validation error patterns.");
        builder.AppendLine("- RISK_RANKING: Rank institutions by anomaly pressure. Which banks have the worst data quality? Show anomaly ranking.");
        builder.AppendLine("- REGULATORY_LOOKUP: What does BSD/DIR/2024/003 require? Find the circular on AML returns. Explain CBN capital guidance.");
        builder.AppendLine("- SEARCH: Search validation errors for CAR. Find returns with warnings. Show all submissions with errors.");
        builder.AppendLine("- SCENARIO: What if NPL doubles? If CAR falls by 10 percent what happens? Run a prudential what-if.");
        builder.AppendLine("- CURRENT_VALUE: What is Access Bank's CAR? Show Zenith's liquidity ratio. Latest NPL for First Bank.");
        builder.AppendLine("- TREND: Show Access Bank CAR trend. NPL history for Zenith. How has Wema's liquidity moved?");
        builder.AppendLine("- COMPARISON_PEER: Compare Access Bank to peers on CAR. Peer median NPL for Zenith. How does GTBank stack against its peers?");
        builder.AppendLine("- COMPARISON_PERIOD: Compare Access Bank CAR between Q4 2025 and Q1 2026. Difference in NPL between FY2024 and FY2025. Compare two periods for Zenith liquidity.");
        builder.AppendLine("- DEADLINE: When is the next filing due? Show upcoming deadlines. What filings are due this month?");
        builder.AppendLine("- COMPLIANCE_STATUS: Are we compliant? Show compliance posture. Compliance status for this institution.");
        builder.AppendLine("- ANOMALY_STATUS: Show anomaly status for the latest return. Any outliers in Access Bank's filing? Latest anomaly report.");
        builder.AppendLine("- HELP: What can I ask? Show me examples. Help me use RegulatorIQ.");
        builder.AppendLine("Return JSON only.");
        return builder.ToString();
    }

    [GeneratedRegex(@"\b(?:(20\d{2})\s*[-/]?\s*Q([1-4])|Q([1-4])\s*(20\d{2}))\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuarterPeriodRegex();

    [GeneratedRegex(@"\b(20\d{2})[-/](0[1-9]|1[0-2])\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthPeriodRegex();

    [GeneratedRegex(@"\b[A-Z]{2,5}/[A-Z]{2,5}/20\d{2}/\d{3,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CircularReferenceRegex();

    private sealed class LlmIntentEnvelope
    {
        [JsonPropertyName("intentCode")]
        public string IntentCode { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }

        [JsonPropertyName("entityNames")]
        public List<string>? EntityNames { get; set; }

        [JsonPropertyName("fieldCode")]
        public string? FieldCode { get; set; }

        [JsonPropertyName("periodCode")]
        public string? PeriodCode { get; set; }

        [JsonPropertyName("licenceCategory")]
        public string? LicenceCategory { get; set; }

        [JsonPropertyName("needsDisambiguation")]
        public bool NeedsDisambiguation { get; set; }

        [JsonPropertyName("disambiguationOptions")]
        public List<string>? DisambiguationOptions { get; set; }

        [JsonPropertyName("extractedParameters")]
        public Dictionary<string, string>? ExtractedParameters { get; set; }
    }

    private sealed record EntityDirectoryRow(
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string LicenceCategory,
        string RegulatorAgency,
        string NormalizedInstitutionName);

    private sealed record EntityResolutionItem(
        Guid TenantId,
        string InstitutionName,
        string LicenceCategory,
        decimal Score);

    private sealed record EntityResolution(
        List<EntityResolutionItem> Entities,
        bool NeedsDisambiguation,
        List<string>? DisambiguationOptions)
    {
        public static EntityResolution Empty => new([], false, null);
    }
}
