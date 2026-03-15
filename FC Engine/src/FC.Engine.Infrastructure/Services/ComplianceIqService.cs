using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

public sealed partial class ComplianceIqService : IComplianceIqService
{
    private static readonly ConcurrentDictionary<string, List<DateTime>> RateWindows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyList<string> KeyRatioFieldCodes =
    [
        "carratio",
        "nplratio",
        "liquidityratio",
        "loandepositratio",
        "roa",
        "roe",
        "netinterestmargin",
        "coverageratio",
        "leverageratio"
    ];

    private static readonly IReadOnlyDictionary<string, string> DefaultModuleByRegulator = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CBN"] = "CBN_PRUDENTIAL",
        ["NDIC"] = "NDIC_SRF",
        ["NAICOM"] = "NAICOM_QR",
        ["SEC"] = "SEC_CMO"
    };

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IAuditLogger _auditLogger;
    private readonly IComplianceHealthService _complianceHealthService;
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly ILogger<ComplianceIqService> _logger;

    public ComplianceIqService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IAuditLogger auditLogger,
        IComplianceHealthService complianceHealthService,
        IAnomalyDetectionService anomalyDetectionService,
        ILogger<ComplianceIqService> logger)
    {
        _dbFactory = dbFactory;
        _auditLogger = auditLogger;
        _complianceHealthService = complianceHealthService;
        _anomalyDetectionService = anomalyDetectionService;
        _logger = logger;
    }

    public async Task<ComplianceIqQueryResponse> QueryAsync(ComplianceIqQueryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new ComplianceIqQueryResponse
            {
                Answer = "Ask a question about returns, deadlines, anomalies, peer benchmarks, or compliance health.",
                IntentCode = "UNCLEAR",
                ConfidenceLevel = "LOW",
                ErrorMessage = "EMPTY_QUERY"
            };
        }

        var stopwatch = Stopwatch.StartNew();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var config = await LoadConfigMapAsync(db, ct);
        var rateLimit = CheckRateLimit(request, config);
        if (rateLimit.IsExceeded)
        {
            return new ComplianceIqQueryResponse
            {
                Answer = $"You have reached the ComplianceIQ query limit for the {rateLimit.ExceededWindow} window. Please try again in {rateLimit.RetryAfterSeconds} seconds.",
                IntentCode = "HELP",
                ConfidenceLevel = "HIGH",
                ErrorMessage = "RATE_LIMITED",
                TotalTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }

        var conversation = await ResolveConversationAsync(db, request, ct);
        var turnNumber = conversation.TurnCount + 1;
        var intent = await ClassifyIntentAsync(request);
        var entities = await ExtractEntitiesAsync(db, request.Query, intent.IntentCode, request, config, ct);

        ComplianceIqQueryPlan? plan = null;
        ExecutionResult execution = ExecutionResult.Empty;
        ComplianceIqQueryResponse response;

        if (!request.IsRegulatorContext && IsRegulatorOnlyIntent(intent.IntentCode))
        {
            response = BuildAccessDeniedResponse();
        }
        else if (intent.IntentCode == "HELP")
        {
            response = await BuildHelpResponseAsync(db, request.IsRegulatorContext, config, ct);
        }
        else if (intent.IntentCode == "UNCLEAR")
        {
            response = BuildClarificationResponse();
        }
        else
        {
            var template = await SelectTemplateAsync(db, intent.IntentCode, entities, request.IsRegulatorContext, ct);
            if (template is null)
            {
                response = new ComplianceIqQueryResponse
                {
                    Answer = "I understood the question category but could not find a grounded query template for it yet. Try asking more specifically about a field, period, anomaly, or filing deadline.",
                    IntentCode = intent.IntentCode,
                    ConfidenceLevel = "LOW",
                    ErrorMessage = "NO_TEMPLATE"
                };
            }
            else
            {
                plan = BuildPlan(template, intent, entities, request);
                execution = await ExecuteTemplateAsync(db, template, entities, request, config, ct);
                response = BuildGroundedResponse(request.Query, intent, entities, plan, execution.Rows, config);
            }
        }

        stopwatch.Stop();
        response.TotalTimeMs = (int)stopwatch.ElapsedMilliseconds;
        response.ConversationId = conversation.Id;

        var turnId = await RecordTurnAsync(
            db,
            conversation,
            request,
            turnNumber,
            intent,
            entities,
            plan,
            execution,
            response,
            ct);

        response.TurnId = turnId;
        RecordRateLimit(request);

        await AuditTurnAsync(turnId, request, intent, plan, response, ct);

        _logger.LogInformation(
            "ComplianceIQ query processed for tenant {TenantId}: intent={IntentCode}, template={TemplateCode}, rows={RowCount}, regulator={RegulatorContext}",
            request.TenantId,
            intent.IntentCode,
            plan?.TemplateCode ?? "NONE",
            execution.Rows.Count,
            request.IsRegulatorContext);

        return response;
    }

    public async Task<IReadOnlyList<ComplianceIqQuickQuestionView>> GetQuickQuestionsAsync(
        bool isRegulatorContext,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ComplianceIqQuickQuestions
            .AsNoTracking()
            .Where(x => x.IsActive && x.RequiresRegulatorContext == isRegulatorContext)
            .OrderBy(x => x.SortOrder)
            .Select(x => new ComplianceIqQuickQuestionView
            {
                Id = x.Id,
                QuestionText = x.QuestionText,
                Category = x.Category,
                IconClass = x.IconClass,
                RequiresRegulatorContext = x.RequiresRegulatorContext
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ComplianceIqConversationTurnView>> GetConversationHistoryAsync(
        Guid conversationId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.ComplianceIqTurns
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.TenantId == tenantId)
            .OrderBy(x => x.TurnNumber)
            .Select(x => new ComplianceIqConversationTurnView
            {
                TurnId = x.Id,
                ConversationId = x.ConversationId,
                TurnNumber = x.TurnNumber,
                QueryText = x.QueryText,
                ResponseText = x.ResponseText,
                IntentCode = x.IntentCode,
                ConfidenceLevel = x.ConfidenceLevel,
                VisualizationType = x.VisualizationType,
                CreatedAt = x.CreatedAt,
                TotalTimeMs = x.TotalTimeMs
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ComplianceIqHistoryEntry>> GetQueryHistoryAsync(
        Guid tenantId,
        string? userId = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var safeLimit = Math.Clamp(limit, 1, 250);

        var query = db.ComplianceIqTurns
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(x => x.UserId == userId);
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(safeLimit)
            .Select(x => new ComplianceIqHistoryEntry
            {
                TurnId = x.Id,
                UserId = x.UserId,
                QueryText = x.QueryText,
                IntentCode = x.IntentCode,
                TemplateCode = x.TemplateCode,
                ConfidenceLevel = x.ConfidenceLevel,
                CreatedAt = x.CreatedAt,
                TotalTimeMs = x.TotalTimeMs
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ComplianceIqTemplateCatalogItem>> GetTemplateCatalogAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.ComplianceIqTemplates
            .AsNoTracking()
            .OrderBy(x => x.IntentCode)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.TemplateCode)
            .Select(x => new ComplianceIqTemplateCatalogItem
            {
                Id = x.Id,
                IntentCode = x.IntentCode,
                TemplateCode = x.TemplateCode,
                DisplayName = x.DisplayName,
                Description = x.Description,
                VisualizationType = x.VisualizationType,
                RequiresRegulatorContext = x.RequiresRegulatorContext,
                IsActive = x.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task SubmitFeedbackAsync(
        int turnId,
        Guid tenantId,
        string userId,
        short rating,
        string? feedbackText,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var turn = await db.ComplianceIqTurns
            .FirstOrDefaultAsync(x => x.Id == turnId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"ComplianceIQ turn {turnId} was not found for tenant {tenantId}.");

        var safeRating = (short)Math.Clamp(rating, (short)1, (short)5);
        db.ComplianceIqFeedback.Add(new ComplianceIqFeedback
        {
            TurnId = turn.Id,
            UserId = userId,
            Rating = safeRating,
            FeedbackText = string.IsNullOrWhiteSpace(feedbackText) ? null : feedbackText.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await _auditLogger.Log(
            "ComplianceIqTurn",
            turn.Id,
            "NL_QUERY_FEEDBACK_SUBMITTED",
            null,
            new { turn.Id, safeRating, feedbackText },
            userId,
            ct);
    }

    public async Task<byte[]> ExportConversationPdfAsync(
        Guid conversationId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var conversation = await db.ComplianceIqConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"ComplianceIQ conversation {conversationId} was not found.");

        var turns = await GetConversationHistoryAsync(conversationId, tenantId, ct);
        var tenantName = await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TenantName)
            .FirstOrDefaultAsync(ct)
            ?? "Unknown tenant";

        QuestPDF.Settings.License = LicenseType.Community;

        return new ComplianceIqConversationDocument(
                turns,
                tenantName,
                conversation.UserId)
            .GeneratePdf();
    }

    private static bool IsRegulatorOnlyIntent(string intentCode) =>
        intentCode is "SECTOR_AGGREGATE" or "ENTITY_COMPARE" or "RISK_RANKING";

    private async Task<ComplianceIqConversation> ResolveConversationAsync(
        MetadataDbContext db,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (request.ConversationId.HasValue)
        {
            var existing = await db.ComplianceIqConversations
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ConversationId.Value &&
                    x.TenantId == request.TenantId &&
                    x.UserId == request.UserId,
                    ct);

            if (existing is not null)
            {
                return existing;
            }
        }

        var conversation = new ComplianceIqConversation
        {
            Id = request.ConversationId ?? Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = request.UserId,
            UserRole = request.UserRole,
            IsRegulatorContext = request.IsRegulatorContext,
            Title = BuildConversationTitle(request.Query),
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            TurnCount = 0,
            IsActive = true
        };

        db.ComplianceIqConversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    private static string BuildConversationTitle(string query)
    {
        var trimmed = query.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..77] + "...";
    }

    private static ComplianceIqRateLimitResult CheckRateLimit(
        ComplianceIqQueryRequest request,
        IReadOnlyDictionary<string, string> config)
    {
        var perMinute = ParseInt(config, "rate.queries_per_minute", 10);
        var perHour = ParseInt(config, "rate.queries_per_hour", 100);
        var perDay = ParseInt(config, "rate.queries_per_day", 500);
        var now = DateTime.UtcNow;
        var key = $"{request.TenantId:N}:{request.UserId}".ToLowerInvariant();
        var timestamps = RateWindows.GetOrAdd(key, _ => new List<DateTime>());

        lock (timestamps)
        {
            timestamps.RemoveAll(x => x < now.AddDays(-1));

            var minuteCount = timestamps.Count(x => x > now.AddMinutes(-1));
            if (minuteCount >= perMinute)
            {
                return new ComplianceIqRateLimitResult
                {
                    UserId = request.UserId,
                    IsExceeded = true,
                    ExceededWindow = "minute",
                    RetryAfterSeconds = 60
                };
            }

            var hourCount = timestamps.Count(x => x > now.AddHours(-1));
            if (hourCount >= perHour)
            {
                return new ComplianceIqRateLimitResult
                {
                    UserId = request.UserId,
                    IsExceeded = true,
                    ExceededWindow = "hour",
                    RetryAfterSeconds = 3600
                };
            }

            if (timestamps.Count >= perDay)
            {
                return new ComplianceIqRateLimitResult
                {
                    UserId = request.UserId,
                    IsExceeded = true,
                    ExceededWindow = "day",
                    RetryAfterSeconds = 86400
                };
            }
        }

        return new ComplianceIqRateLimitResult { UserId = request.UserId };
    }

    private static void RecordRateLimit(ComplianceIqQueryRequest request)
    {
        var key = $"{request.TenantId:N}:{request.UserId}".ToLowerInvariant();
        var timestamps = RateWindows.GetOrAdd(key, _ => new List<DateTime>());
        lock (timestamps)
        {
            timestamps.Add(DateTime.UtcNow);
        }
    }

    private static async Task<Dictionary<string, string>> LoadConfigMapAsync(MetadataDbContext db, CancellationToken ct)
    {
        var rows = await db.ComplianceIqConfigs
            .AsNoTracking()
            .Where(x => x.EffectiveTo == null)
            .OrderByDescending(x => x.EffectiveFrom)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ConfigKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.First().ConfigValue,
                StringComparer.OrdinalIgnoreCase);
    }

    private Task<ComplianceIqIntentClassification> ClassifyIntentAsync(ComplianceIqQueryRequest request)
    {
        var normalized = request.Query.Trim().ToLowerInvariant();

        if (ContainsAny(normalized, "help", "what can i ask", "how does this work", "usage examples"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "HELP",
                Confidence = 0.99m,
                Reasoning = "The question asks about system capabilities."
            });
        }

        if (ContainsAny(normalized, "deadline", "due", "overdue", "filing due", "next filing"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "DEADLINE",
                Confidence = 0.95m,
                Reasoning = "The question refers to filing dates or overdue obligations."
            });
        }

        if (ContainsAny(normalized, "anomaly", "outlier", "data quality", "quality score"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "ANOMALY_STATUS",
                Confidence = 0.93m,
                Reasoning = "The question refers to anomaly detection or data quality."
            });
        }

        if (ContainsAny(normalized, "compliance health", "health score", "compliance score", "are we compliant"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "COMPLIANCE_STATUS",
                Confidence = 0.93m,
                Reasoning = "The question asks for compliance posture or Compliance Health Score."
            });
        }

        if (CircularReferenceRegex().IsMatch(request.Query) || ContainsAny(normalized, "circular", "guideline", "require", "what does"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "REGULATORY_LOOKUP",
                Confidence = 0.9m,
                Reasoning = "The question is asking for a regulation, circular, or guidance lookup."
            });
        }

        if (ContainsAny(normalized, "what if", "if npl", "if car", "doubled", "tripled", "halved", "increase by", "decrease by"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "SCENARIO",
                Confidence = 0.95m,
                Reasoning = "The question is phrased as a what-if or scenario analysis."
            });
        }

        if (request.IsRegulatorContext && ContainsAny(normalized, "rank", "ranking", "lowest quality", "highest anomaly", "worst"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "RISK_RANKING",
                Confidence = 0.91m,
                Reasoning = "The question asks for a regulator ranking of institutions."
            });
        }

        if (request.IsRegulatorContext && ContainsAny(normalized, "aggregate", "sector average", "across all", "sector median"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "SECTOR_AGGREGATE",
                Confidence = 0.89m,
                Reasoning = "The question asks for a regulator aggregate across institutions."
            });
        }

        if (request.IsRegulatorContext && ContainsAny(normalized, "compare", "versus", " vs "))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "ENTITY_COMPARE",
                Confidence = 0.87m,
                Reasoning = "The question asks for cross-entity comparison in regulator context."
            });
        }

        if (ContainsAny(normalized, "peer", "benchmark", "compare to peers", "peer median", "peer average"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "COMPARISON_PEER",
                Confidence = 0.92m,
                Reasoning = "The question asks for a peer benchmark."
            });
        }

        if ((QuarterPeriodRegex().Matches(request.Query).Count >= 2 || MonthPeriodRegex().Matches(request.Query).Count >= 2) &&
            ContainsAny(normalized, "compare", "versus", " vs ", "difference"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "COMPARISON_PERIOD",
                Confidence = 0.91m,
                Reasoning = "The question asks for a comparison between periods."
            });
        }

        if (ContainsAny(normalized, "trend", "history", "last ", "past ", "how has", "movement"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "TREND",
                Confidence = 0.9m,
                Reasoning = "The question asks about historical movement over time."
            });
        }

        if (ContainsAny(normalized, "validation", "search", "find", "show all", "list returns"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "SEARCH",
                Confidence = 0.84m,
                Reasoning = "The question is a discovery or search request."
            });
        }

        if (ContainsAny(normalized, "what is", "show me", "current", "latest", "our car", "our npl", "our liquidity"))
        {
            return Task.FromResult(new ComplianceIqIntentClassification
            {
                IntentCode = "CURRENT_VALUE",
                Confidence = 0.86m,
                Reasoning = "The question asks for a current point-in-time metric."
            });
        }

        return Task.FromResult(new ComplianceIqIntentClassification
        {
            IntentCode = "UNCLEAR",
            Confidence = 0.35m,
            Reasoning = "The question could not be classified confidently."
        });
    }

    private async Task<ComplianceIqExtractedEntities> ExtractEntitiesAsync(
        MetadataDbContext db,
        string query,
        string intentCode,
        ComplianceIqQueryRequest request,
        IReadOnlyDictionary<string, string> config,
        CancellationToken ct)
    {
        var entities = new ComplianceIqExtractedEntities();
        var normalized = query.Trim().ToLowerInvariant();

        ExtractPeriods(query, entities, config);
        ExtractScenarioMultiplier(query, entities);
        ExtractRegulatorAndLicence(normalized, entities);

        entities.WantsOverdueItems = ContainsAny(normalized, "overdue", "late", "missed");
        entities.RequestedTopCount = ExtractTopCount(normalized);

        await ResolveFieldSynonymsAsync(db, normalized, entities, ct);
        await ResolveTemplateFieldMatchesAsync(db, normalized, entities, ct);
        await ResolveEntityNamesAsync(db, normalized, request.IsRegulatorContext, entities, ct);
        ResolveSearchKeyword(query, intentCode, entities);

        if (string.IsNullOrWhiteSpace(entities.ModuleCode))
        {
            entities.ModuleCode = GuessModuleCode(normalized, entities.RegulatorCode);
        }

        if (string.IsNullOrWhiteSpace(entities.RegulatorCode) &&
            !string.IsNullOrWhiteSpace(entities.ModuleCode))
        {
            entities.RegulatorCode = entities.ModuleCode switch
            {
                "CBN_PRUDENTIAL" => "CBN",
                "NDIC_SRF" => "NDIC",
                "NAICOM_QR" => "NAICOM",
                "SEC_CMO" => "SEC",
                _ => request.RegulatorCode
            };
        }

        if (entities.PeriodCount <= 0)
        {
            entities.PeriodCount = ParseInt(config, "trend.default_periods", 8);
        }

        return entities;
    }

    private static void ExtractPeriods(
        string query,
        ComplianceIqExtractedEntities entities,
        IReadOnlyDictionary<string, string> config)
    {
        var quarterMatches = QuarterPeriodRegex().Matches(query);
        if (quarterMatches.Count > 0)
        {
            entities.PeriodCode = BuildQuarterCode(quarterMatches[0]);
            if (quarterMatches.Count > 1)
            {
                entities.ComparisonPeriodCode = BuildQuarterCode(quarterMatches[1]);
            }
        }

        var monthMatches = MonthPeriodRegex().Matches(query);
        if (monthMatches.Count > 0 && string.IsNullOrWhiteSpace(entities.PeriodCode))
        {
            entities.PeriodCode = $"{monthMatches[0].Groups[1].Value}-{monthMatches[0].Groups[2].Value}";
            if (monthMatches.Count > 1)
            {
                entities.ComparisonPeriodCode = $"{monthMatches[1].Groups[1].Value}-{monthMatches[1].Groups[2].Value}";
            }
        }

        var countMatch = TrendLookbackRegex().Match(query);
        entities.PeriodCount = countMatch.Success
            ? int.Parse(countMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : ParseInt(config, "trend.default_periods", 8);
    }

    private static string BuildQuarterCode(Match match)
    {
        var year = match.Groups[1].Success
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : DateTime.UtcNow.Year;
        var quarter = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return $"{year}-Q{quarter}";
    }

    private static void ExtractScenarioMultiplier(string query, ComplianceIqExtractedEntities entities)
    {
        if (Regex.IsMatch(query, @"\bdoubled?\b", RegexOptions.IgnoreCase))
        {
            entities.ScenarioMultiplier = 2m;
            return;
        }

        if (Regex.IsMatch(query, @"\btripled?\b", RegexOptions.IgnoreCase))
        {
            entities.ScenarioMultiplier = 3m;
            return;
        }

        if (Regex.IsMatch(query, @"\bhalved?\b", RegexOptions.IgnoreCase))
        {
            entities.ScenarioMultiplier = 0.5m;
            return;
        }

        var increase = Regex.Match(query, @"(\d+(?:\.\d+)?)\s*%\s*(?:increase|rise|growth)", RegexOptions.IgnoreCase);
        if (increase.Success)
        {
            var pct = decimal.Parse(increase.Groups[1].Value, CultureInfo.InvariantCulture);
            entities.ScenarioMultiplier = 1m + (pct / 100m);
            return;
        }

        var decrease = Regex.Match(query, @"(\d+(?:\.\d+)?)\s*%\s*(?:decrease|drop|decline|fall)", RegexOptions.IgnoreCase);
        if (decrease.Success)
        {
            var pct = decimal.Parse(decrease.Groups[1].Value, CultureInfo.InvariantCulture);
            entities.ScenarioMultiplier = Math.Max(0m, 1m - (pct / 100m));
        }
    }

    private static void ExtractRegulatorAndLicence(string normalizedQuery, ComplianceIqExtractedEntities entities)
    {
        if (ContainsAny(normalizedQuery, " cbn", "cbn ", "central bank"))
        {
            entities.RegulatorCode = "CBN";
        }
        else if (ContainsAny(normalizedQuery, " ndic", "deposit insurance"))
        {
            entities.RegulatorCode = "NDIC";
        }
        else if (ContainsAny(normalizedQuery, " naicom", "insurance commission"))
        {
            entities.RegulatorCode = "NAICOM";
        }
        else if (ContainsAny(normalizedQuery, " sec ", "securities and exchange"))
        {
            entities.RegulatorCode = "SEC";
        }

        if (ContainsAny(normalizedQuery, "commercial bank", "dmb"))
        {
            entities.LicenceCategory = "COMMERCIAL_BANK";
        }
        else if (ContainsAny(normalizedQuery, "merchant bank"))
        {
            entities.LicenceCategory = "MERCHANT_BANK";
        }
        else if (ContainsAny(normalizedQuery, "microfinance"))
        {
            entities.LicenceCategory = "MICROFINANCE_BANK_NATIONAL";
        }
        else if (ContainsAny(normalizedQuery, "insurance"))
        {
            entities.LicenceCategory = "GENERAL_INSURANCE";
        }
        else if (ContainsAny(normalizedQuery, "broker", "dealer"))
        {
            entities.LicenceCategory = "BROKER_DEALER";
        }
    }

    private async Task ResolveFieldSynonymsAsync(
        MetadataDbContext db,
        string normalizedQuery,
        ComplianceIqExtractedEntities entities,
        CancellationToken ct)
    {
        var synonyms = await db.ComplianceIqFieldSynonyms
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Synonym.Length)
            .ToListAsync(ct);

        foreach (var synonym in synonyms)
        {
            if (!normalizedQuery.Contains(synonym.Synonym, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entities.FieldCodes.Contains(synonym.FieldCode, StringComparer.OrdinalIgnoreCase))
            {
                entities.FieldCodes.Add(synonym.FieldCode);
                entities.FieldNames.Add(synonym.Synonym);
            }

            entities.ModuleCode ??= synonym.ModuleCode;
            entities.RegulatorCode ??= synonym.RegulatorCode;
        }
    }

    private static async Task ResolveTemplateFieldMatchesAsync(
        MetadataDbContext db,
        string normalizedQuery,
        ComplianceIqExtractedEntities entities,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count > 0)
        {
            return;
        }

        var templateFields = await db.TemplateFields
            .AsNoTracking()
            .OrderByDescending(x => x.DisplayName.Length)
            .Select(x => new { x.FieldName, x.DisplayName })
            .Take(500)
            .ToListAsync(ct);

        foreach (var field in templateFields)
        {
            if (!normalizedQuery.Contains(field.DisplayName.ToLowerInvariant()) &&
                !normalizedQuery.Contains(field.FieldName.ToLowerInvariant()))
            {
                continue;
            }

            var normalizedCode = AnomalySupport.NormalizeFieldCode(field.FieldName);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                continue;
            }

            entities.FieldCodes.Add(normalizedCode);
            entities.FieldNames.Add(field.DisplayName);
            break;
        }
    }

    private static async Task ResolveEntityNamesAsync(
        MetadataDbContext db,
        string normalizedQuery,
        bool regulatorContext,
        ComplianceIqExtractedEntities entities,
        CancellationToken ct)
    {
        if (!regulatorContext)
        {
            return;
        }

        var institutions = await db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.InstitutionName)
            .Distinct()
            .ToListAsync(ct);

        foreach (var institutionName in institutions.OrderByDescending(x => x.Length))
        {
            if (normalizedQuery.Contains(institutionName.ToLowerInvariant()))
            {
                entities.EntityNames.Add(institutionName);
            }
        }

        if (entities.EntityNames.Count > 0 || !normalizedQuery.Contains(" vs ", StringComparison.Ordinal))
        {
            return;
        }

        var parts = normalizedQuery.Split(" vs ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var cleaned = part
                .Replace("compare", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("on", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                entities.EntityNames.Add(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned));
            }
        }
    }

    private static void ResolveSearchKeyword(string query, string intentCode, ComplianceIqExtractedEntities entities)
    {
        if (intentCode == "REGULATORY_LOOKUP")
        {
            entities.CircularReference ??= CircularReferenceRegex().Match(query).Success
                ? CircularReferenceRegex().Match(query).Value.ToUpperInvariant()
                : null;
            entities.SearchKeyword = entities.CircularReference ?? query.Trim();
            return;
        }

        if (intentCode != "SEARCH")
        {
            return;
        }

        if (ContainsAny(query.ToLowerInvariant(), "validation", "error", "warning"))
        {
            entities.SearchKeyword = "validation";
            return;
        }

        var keyword = query
            .Replace("search", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("find", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("show", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        entities.SearchKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword;
    }

    private static string? GuessModuleCode(string normalizedQuery, string? regulatorCode)
    {
        if (ContainsAny(normalizedQuery, "prudential"))
        {
            return "CBN_PRUDENTIAL";
        }

        if (ContainsAny(normalizedQuery, "status report"))
        {
            return "NDIC_SRF";
        }

        if (ContainsAny(normalizedQuery, "quarterly return", "gross premium", "claims paid"))
        {
            return "NAICOM_QR";
        }

        if (ContainsAny(normalizedQuery, "capital market", "aum"))
        {
            return "SEC_CMO";
        }

        if (!string.IsNullOrWhiteSpace(regulatorCode) &&
            DefaultModuleByRegulator.TryGetValue(regulatorCode, out var moduleCode))
        {
            return moduleCode;
        }

        return null;
    }

    private static int ExtractTopCount(string normalizedQuery)
    {
        var match = Regex.Match(normalizedQuery, @"(?:top|rank|show)\s+(\d+)", RegexOptions.IgnoreCase);
        return match.Success
            ? Math.Clamp(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), 1, 50)
            : 10;
    }

    private async Task<ComplianceIqTemplate?> SelectTemplateAsync(
        MetadataDbContext db,
        string intentCode,
        ComplianceIqExtractedEntities entities,
        bool regulatorContext,
        CancellationToken ct)
    {
        var templates = await db.ComplianceIqTemplates
            .AsNoTracking()
            .Where(x => x.IsActive && x.IntentCode == intentCode && (!x.RequiresRegulatorContext || regulatorContext))
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        if (templates.Count == 0)
        {
            return null;
        }

        return intentCode switch
        {
            "CURRENT_VALUE" when entities.FieldCodes.Count == 0 => templates.FirstOrDefault(x => x.TemplateCode == "CV_KEY_RATIOS"),
            "CURRENT_VALUE" => templates.FirstOrDefault(x => x.TemplateCode == "CV_SINGLE_FIELD"),
            "TREND" => templates.FirstOrDefault(x => x.TemplateCode == "TR_FIELD_HISTORY"),
            "COMPARISON_PEER" => templates.FirstOrDefault(x => x.TemplateCode == "CP_PEER_METRIC"),
            "COMPARISON_PERIOD" => templates.FirstOrDefault(x => x.TemplateCode == "CPR_TWO_PERIODS"),
            "DEADLINE" => templates.FirstOrDefault(x => x.TemplateCode == "DL_CALENDAR"),
            "REGULATORY_LOOKUP" => templates.FirstOrDefault(x => x.TemplateCode == "RL_KNOWLEDGE"),
            "COMPLIANCE_STATUS" => templates.FirstOrDefault(x => x.TemplateCode == "CS_HEALTH_SCORE"),
            "ANOMALY_STATUS" => templates.FirstOrDefault(x => x.TemplateCode == "AS_LATEST_REPORT"),
            "SCENARIO" => templates.FirstOrDefault(x => x.TemplateCode == "SC_CAR_NPL"),
            "SEARCH" => templates.FirstOrDefault(x => x.TemplateCode == "SR_VALIDATION_ERRORS"),
            "SECTOR_AGGREGATE" => templates.FirstOrDefault(x => x.TemplateCode == "SA_FIELD_AGGREGATE"),
            "ENTITY_COMPARE" => templates.FirstOrDefault(x => x.TemplateCode == "EC_ENTITY_COMPARE"),
            "RISK_RANKING" => templates.FirstOrDefault(x => x.TemplateCode == "RR_ANOMALY_RANKING"),
            _ => templates.FirstOrDefault()
        };
    }

    private static ComplianceIqQueryPlan BuildPlan(
        ComplianceIqTemplate template,
        ComplianceIqIntentClassification intent,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenantId"] = request.TenantId.ToString(),
            ["regulatorContext"] = request.IsRegulatorContext.ToString()
        };

        if (!string.IsNullOrWhiteSpace(entities.ModuleCode))
        {
            parameters["moduleCode"] = entities.ModuleCode;
        }

        if (!string.IsNullOrWhiteSpace(entities.RegulatorCode))
        {
            parameters["regulatorCode"] = entities.RegulatorCode;
        }

        if (!string.IsNullOrWhiteSpace(entities.PeriodCode))
        {
            parameters["periodCode"] = entities.PeriodCode;
        }

        if (!string.IsNullOrWhiteSpace(entities.ComparisonPeriodCode))
        {
            parameters["comparisonPeriodCode"] = entities.ComparisonPeriodCode;
        }

        if (!string.IsNullOrWhiteSpace(entities.LicenceCategory))
        {
            parameters["licenceCategory"] = entities.LicenceCategory;
        }

        if (!string.IsNullOrWhiteSpace(entities.SearchKeyword))
        {
            parameters["keyword"] = entities.SearchKeyword;
        }

        if (entities.FieldCodes.Count > 0)
        {
            parameters["fieldCode"] = entities.FieldCodes[0];
        }

        parameters["periodCount"] = entities.PeriodCount.ToString(CultureInfo.InvariantCulture);
        parameters["requestedTopCount"] = entities.RequestedTopCount.ToString(CultureInfo.InvariantCulture);

        if (entities.ScenarioMultiplier.HasValue)
        {
            parameters["scenarioMultiplier"] = entities.ScenarioMultiplier.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (entities.EntityNames.Count > 0)
        {
            parameters["entityNames"] = string.Join(", ", entities.EntityNames);
        }

        return new ComplianceIqQueryPlan
        {
            IntentCode = intent.IntentCode,
            TemplateCode = template.TemplateCode,
            ResultFormat = template.ResultFormat,
            VisualizationType = template.VisualizationType,
            Explanation = template.Description,
            Parameters = parameters
        };
    }

    private async Task<ExecutionResult> ExecuteTemplateAsync(
        MetadataDbContext db,
        ComplianceIqTemplate template,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        IReadOnlyDictionary<string, string> config,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        List<Dictionary<string, object?>> rows = template.TemplateCode switch
        {
            "CV_SINGLE_FIELD" => await ExecuteCurrentValueAsync(db, entities, request, ct),
            "CV_KEY_RATIOS" => await ExecuteKeyRatiosAsync(db, entities, request, ct),
            "TR_FIELD_HISTORY" => await ExecuteTrendAsync(db, entities, request, ct),
            "CP_PEER_METRIC" => await ExecutePeerComparisonAsync(db, entities, request, ct),
            "CPR_TWO_PERIODS" => await ExecutePeriodComparisonAsync(db, entities, request, ct),
            "DL_CALENDAR" => await ExecuteDeadlineAsync(db, entities, request, ct),
            "RL_KNOWLEDGE" => await ExecuteRegulatoryLookupAsync(db, entities, ct),
            "CS_HEALTH_SCORE" => await ExecuteComplianceStatusAsync(request, ct),
            "AS_LATEST_REPORT" => await ExecuteAnomalyStatusAsync(entities, request, ct),
            "SC_CAR_NPL" => await ExecuteScenarioAsync(db, entities, request, config, ct),
            "SR_VALIDATION_ERRORS" => await ExecuteSearchAsync(db, entities, request, ct),
            "SA_FIELD_AGGREGATE" => await ExecuteSectorAggregateAsync(db, entities, request, ct),
            "EC_ENTITY_COMPARE" => await ExecuteEntityCompareAsync(db, entities, request, ct),
            "RR_ANOMALY_RANKING" => await ExecuteRiskRankingAsync(db, entities, request, ct),
            _ => new List<Dictionary<string, object?>>()
        };

        stopwatch.Stop();
        var maxRows = ParseInt(config, "response.max_rows", 25);
        if (rows.Count > maxRows)
        {
            rows = rows.Take(maxRows).ToList();
        }

        return new ExecutionResult(rows, (int)stopwatch.ElapsedMilliseconds);
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteCurrentValueAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var snapshots = await LoadSnapshotsAsync(db, request.TenantId, entities.ModuleCode, ct);
        var fieldCode = entities.FieldCodes[0];

        var latest = snapshots
            .Where(x => x.Metrics.ContainsKey(fieldCode))
            .OrderByDescending(x => x.PeriodSortKey)
            .ThenByDescending(x => x.SubmittedAt)
            .FirstOrDefault();

        if (latest is null)
        {
            return new List<Dictionary<string, object?>>();
        }

        var metric = latest.Metrics[fieldCode];
        return
        [
            Row(
                ("field_code", metric.FieldCode),
                ("field_label", metric.FieldLabel),
                ("value", metric.Value),
                ("period_code", latest.PeriodCode),
                ("module_code", latest.ModuleCode),
                ("institution_name", latest.InstitutionName),
                ("submitted_at", latest.SubmittedAt))
        ];
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteKeyRatiosAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        var snapshots = await LoadSnapshotsAsync(db, request.TenantId, entities.ModuleCode ?? "CBN_PRUDENTIAL", ct);
        var latest = snapshots
            .OrderByDescending(x => x.PeriodSortKey)
            .ThenByDescending(x => x.SubmittedAt)
            .FirstOrDefault();

        if (latest is null)
        {
            return new List<Dictionary<string, object?>>();
        }

        return KeyRatioFieldCodes
            .Where(code => latest.Metrics.ContainsKey(code))
            .Select(code => latest.Metrics[code])
            .Select(metric => Row(
                ("field_code", metric.FieldCode),
                ("field_label", metric.FieldLabel),
                ("value", metric.Value),
                ("period_code", latest.PeriodCode),
                ("module_code", latest.ModuleCode),
                ("institution_name", latest.InstitutionName)))
            .ToList();
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteTrendAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var fieldCode = entities.FieldCodes[0];
        var snapshots = await LoadSnapshotsAsync(db, request.TenantId, entities.ModuleCode, ct);

        return snapshots
            .Where(x => x.Metrics.ContainsKey(fieldCode))
            .OrderByDescending(x => x.PeriodSortKey)
            .ThenByDescending(x => x.SubmittedAt)
            .Take(Math.Clamp(entities.PeriodCount, 1, 16))
            .Reverse()
            .Select(x =>
            {
                var metric = x.Metrics[fieldCode];
                return Row(
                    ("field_code", metric.FieldCode),
                    ("field_label", metric.FieldLabel),
                    ("value", metric.Value),
                    ("period_code", x.PeriodCode),
                    ("module_code", x.ModuleCode),
                    ("institution_name", x.InstitutionName));
            })
            .ToList();
    }

    private async Task<List<Dictionary<string, object?>>> ExecutePeerComparisonAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var currentRows = await ExecuteCurrentValueAsync(db, entities, request, ct);
        if (currentRows.Count == 0)
        {
            return currentRows;
        }

        var current = currentRows[0];
        var fieldCode = entities.FieldCodes[0];
        var periodCode = current["period_code"]?.ToString() ?? string.Empty;
        var licenceCategory = entities.LicenceCategory
            ?? await ResolveLicenceCategoryAsync(db, request.TenantId, ct)
            ?? "UNKNOWN";

        var peerStats = await db.AnomalyPeerGroupStatistics
            .AsNoTracking()
            .Include(x => x.ModelVersion)
            .Where(x =>
                x.FieldCode == fieldCode &&
                x.PeriodCode == periodCode &&
                x.LicenceCategory == licenceCategory &&
                x.InstitutionSizeBand == "ALL" &&
                x.ModelVersion != null &&
                x.ModelVersion.Status == "ACTIVE")
            .OrderByDescending(x => x.ModelVersionId)
            .FirstOrDefaultAsync(ct);

        decimal myValue = Convert.ToDecimal(current["value"] ?? 0m, CultureInfo.InvariantCulture);

        if (peerStats is null)
        {
            peerStats = await BuildFallbackPeerStatsAsync(db, request.TenantId, fieldCode, entities.ModuleCode, periodCode, licenceCategory, ct);
            if (peerStats is null)
            {
                return new List<Dictionary<string, object?>>();
            }
        }

        var deviationPct = peerStats.PeerMedian.GetValueOrDefault() == 0m
            ? 0m
            : decimal.Round(((myValue - peerStats.PeerMedian.GetValueOrDefault()) / Math.Abs(peerStats.PeerMedian.GetValueOrDefault())) * 100m, 2);

        return
        [
            Row(
                ("field_code", fieldCode),
                ("field_label", current["field_label"]),
                ("my_value", myValue),
                ("peer_median", peerStats.PeerMedian),
                ("peer_q1", peerStats.PeerQ1),
                ("peer_q3", peerStats.PeerQ3),
                ("peer_count", peerStats.PeerCount),
                ("licence_category", licenceCategory),
                ("deviation_pct", deviationPct),
                ("period_code", periodCode),
                ("module_code", current["module_code"]))
        ];
    }

    private async Task<List<Dictionary<string, object?>>> ExecutePeriodComparisonAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var fieldCode = entities.FieldCodes[0];
        var snapshots = await LoadSnapshotsAsync(db, request.TenantId, entities.ModuleCode, ct);
        var withMetric = snapshots
            .Where(x => x.Metrics.ContainsKey(fieldCode))
            .OrderByDescending(x => x.PeriodSortKey)
            .ThenByDescending(x => x.SubmittedAt)
            .ToList();

        if (withMetric.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        SubmissionSnapshot? first;
        SubmissionSnapshot? second;

        if (!string.IsNullOrWhiteSpace(entities.PeriodCode) && !string.IsNullOrWhiteSpace(entities.ComparisonPeriodCode))
        {
            first = withMetric.FirstOrDefault(x => x.PeriodCode == entities.PeriodCode);
            second = withMetric.FirstOrDefault(x => x.PeriodCode == entities.ComparisonPeriodCode);
        }
        else
        {
            first = withMetric.ElementAtOrDefault(0);
            second = withMetric.ElementAtOrDefault(1);
        }

        if (first is null || second is null)
        {
            return new List<Dictionary<string, object?>>();
        }

        return new List<Dictionary<string, object?>>
        {
            ToMetricRow(first, fieldCode),
            ToMetricRow(second, fieldCode)
        };
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteDeadlineAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        var query = db.ReturnPeriods
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.TenantId == request.TenantId && x.Module != null);

        if (!string.IsNullOrWhiteSpace(entities.RegulatorCode))
        {
            query = query.Where(x => x.Module!.RegulatorCode == entities.RegulatorCode);
        }

        var now = DateTime.UtcNow.Date;
        var periods = await query
            .OrderBy(x => x.EffectiveDeadline)
            .ToListAsync(ct);

        var rows = periods
            .Select(x =>
            {
                var dueDate = x.EffectiveDeadline.Date;
                var daysRemaining = (dueDate - now).Days;
                return Row(
                    ("module_code", x.Module!.ModuleCode),
                    ("module_name", x.Module.ModuleName),
                    ("period_code", RegulatorAnalyticsSupport.FormatPeriodCode(x)),
                    ("due_date", dueDate),
                    ("days_remaining", daysRemaining),
                    ("status", daysRemaining < 0 ? "OVERDUE" : daysRemaining <= 7 ? "DUE_SOON" : "UPCOMING"),
                    ("regulator_code", x.Module.RegulatorCode));
            })
            .Where(x => !entities.WantsOverdueItems || Convert.ToInt32(x["days_remaining"] ?? 0, CultureInfo.InvariantCulture) < 0)
            .Take(entities.WantsOverdueItems ? 20 : 10)
            .ToList();

        return rows;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteRegulatoryLookupAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        CancellationToken ct)
    {
        var keyword = entities.CircularReference ?? entities.SearchKeyword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<Dictionary<string, object?>>();
        }

        var articles = await db.KnowledgeBaseArticles
            .AsNoTracking()
            .Where(x => x.IsPublished &&
                        (x.Title.Contains(keyword) ||
                         x.Content.Contains(keyword) ||
                         (x.Tags != null && x.Tags.Contains(keyword))))
            .OrderBy(x => x.DisplayOrder)
            .Take(10)
            .ToListAsync(ct);

        var graphNodes = await db.KnowledgeGraphNodes
            .AsNoTracking()
            .Where(x =>
                (x.DisplayName.Contains(keyword) || (x.SourceReference != null && x.SourceReference.Contains(keyword)) || (x.Code != null && x.Code.Contains(keyword))) &&
                (entities.RegulatorCode == null || x.RegulatorCode == entities.RegulatorCode))
            .OrderBy(x => x.DisplayName)
            .Take(10)
            .ToListAsync(ct);

        var rows = new List<Dictionary<string, object?>>();
        rows.AddRange(articles.Select(x => Row(
            ("source_type", "KnowledgeBase"),
            ("title", x.Title),
            ("summary", ExtractSummary(x.Content)),
            ("module_code", x.ModuleCode ?? entities.ModuleCode ?? string.Empty),
            ("field_code", string.Empty),
            ("period_code", string.Empty),
            ("institution_name", string.Empty))));

        rows.AddRange(graphNodes.Select(x => Row(
            ("source_type", "KnowledgeGraph"),
            ("title", x.DisplayName),
            ("summary", x.SourceReference ?? x.Code ?? x.NodeType),
            ("module_code", string.Empty),
            ("field_code", x.Code ?? string.Empty),
            ("period_code", string.Empty),
            ("institution_name", string.Empty))));

        return rows.Take(10).ToList();
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteComplianceStatusAsync(
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        var score = await _complianceHealthService.GetCurrentScore(request.TenantId, ct);

        return
        [
            Row(
                ("overall_score", score.OverallScore),
                ("rating", ComplianceHealthService.RatingLabel(score.Rating)),
                ("trend", score.Trend.ToString()),
                ("filing_timeliness", score.FilingTimeliness),
                ("data_quality", score.DataQuality),
                ("regulatory_capital", score.RegulatoryCapital),
                ("audit_governance", score.AuditGovernance),
                ("engagement", score.Engagement),
                ("period_code", score.PeriodLabel),
                ("module_code", "CHS"),
                ("institution_name", score.TenantName))
        ];
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteAnomalyStatusAsync(
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        var reports = await _anomalyDetectionService.GetReportsForTenantAsync(
            request.TenantId,
            entities.ModuleCode,
            entities.PeriodCode,
            ct);

        if (reports.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        if (!string.IsNullOrWhiteSpace(entities.ModuleCode))
        {
            var latest = reports.OrderByDescending(x => x.AnalysedAt).First();
            return latest.Findings
                .OrderByDescending(x => AnomalySupport.SeverityRank(x.Severity))
                .ThenBy(x => x.FieldLabel)
                .Take(10)
                .Select(x => Row(
                    ("field_code", x.FieldCode),
                    ("field_label", x.FieldLabel),
                    ("severity", x.Severity),
                    ("finding_type", x.FindingType),
                    ("detection_method", x.DetectionMethod),
                    ("explanation", x.Explanation),
                    ("module_code", latest.ModuleCode),
                    ("period_code", latest.PeriodCode),
                    ("institution_name", latest.InstitutionName)))
                .ToList();
        }

        return reports
            .OrderByDescending(x => x.AnalysedAt)
            .Take(5)
            .Select(x => Row(
                ("module_code", x.ModuleCode),
                ("period_code", x.PeriodCode),
                ("quality_score", x.OverallQualityScore),
                ("traffic_light", x.TrafficLight),
                ("alert_count", x.AlertCount),
                ("warning_count", x.WarningCount),
                ("total_findings", x.TotalFindings),
                ("institution_name", x.InstitutionName)))
            .ToList();
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteScenarioAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        IReadOnlyDictionary<string, string> config,
        CancellationToken ct)
    {
        var multiplier = entities.ScenarioMultiplier ?? ParseDecimal(config, "scenario.default_npl_multiplier", 2m);
        var snapshots = await LoadSnapshotsAsync(db, request.TenantId, "CBN_PRUDENTIAL", ct);
        var latest = snapshots
            .OrderByDescending(x => x.PeriodSortKey)
            .ThenByDescending(x => x.SubmittedAt)
            .FirstOrDefault();

        if (latest is null)
        {
            return new List<Dictionary<string, object?>>();
        }

        var currentNplAmount = GetMetricValue(latest, "nplamount");
        var currentProvision = GetMetricValue(latest, "provisionamount");
        var shareholdersFunds = GetMetricValue(latest, "shareholdersfunds");
        var rwa = GetMetricValue(latest, "riskweightedassets");
        var totalLoans = GetMetricValue(latest, "totalloans");
        var totalDeposits = GetMetricValue(latest, "totaldeposits");
        var currentCar = GetMetricValue(latest, "carratio", rwa == 0m ? 0m : decimal.Round((shareholdersFunds / rwa) * 100m, 2));
        var currentNplRatio = GetMetricValue(latest, "nplratio", totalLoans == 0m ? 0m : decimal.Round((currentNplAmount / totalLoans) * 100m, 2));

        var projectedNplAmount = decimal.Round(currentNplAmount * multiplier, 2);
        var additionalProvision = decimal.Round(Math.Max(0m, projectedNplAmount - currentNplAmount) * 0.5m, 2);
        var projectedCapital = shareholdersFunds - additionalProvision;
        var projectedCar = rwa == 0m ? 0m : decimal.Round((projectedCapital / rwa) * 100m, 2);
        var projectedNplRatio = totalLoans == 0m ? 0m : decimal.Round((projectedNplAmount / totalLoans) * 100m, 2);
        var projectedLdr = totalDeposits == 0m ? 0m : decimal.Round((totalLoans / totalDeposits) * 100m, 2);

        return
        [
            Row(
                ("period_code", latest.PeriodCode),
                ("module_code", latest.ModuleCode),
                ("institution_name", latest.InstitutionName),
                ("current_car", currentCar),
                ("projected_car", projectedCar),
                ("current_npl_ratio", currentNplRatio),
                ("projected_npl_ratio", projectedNplRatio),
                ("current_npl_amount", currentNplAmount),
                ("projected_npl_amount", projectedNplAmount),
                ("additional_provision_needed", additionalProvision),
                ("projected_ldr", projectedLdr),
                ("scenario_multiplier", multiplier),
                ("car_breaches_minimum", projectedCar < 15m))
        ];
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteSearchAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (string.Equals(entities.SearchKeyword, "validation", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(entities.SearchKeyword))
        {
            var validationRows = await (
                from report in db.ValidationReports.AsNoTracking()
                join submission in db.Submissions.AsNoTracking() on report.SubmissionId equals submission.Id
                join period in db.ReturnPeriods.AsNoTracking() on submission.ReturnPeriodId equals period.Id
                join module in db.Modules.AsNoTracking() on period.ModuleId equals module.Id
                join institution in db.Institutions.AsNoTracking() on submission.InstitutionId equals institution.Id
                where report.TenantId == request.TenantId && submission.TenantId == request.TenantId
                select new
                {
                    submission.Id,
                    submission.Status,
                    submission.SubmittedAt,
                    submission.ReturnCode,
                    period.Year,
                    period.Month,
                    period.Quarter,
                    ModuleCode = module.ModuleCode,
                    InstitutionName = institution.InstitutionName,
                    ErrorCount = db.ValidationErrors.Count(x => x.ValidationReportId == report.Id && x.Severity == ValidationSeverity.Error),
                    WarningCount = db.ValidationErrors.Count(x => x.ValidationReportId == report.Id && x.Severity == ValidationSeverity.Warning)
                })
                .OrderByDescending(x => x.SubmittedAt)
                .Take(20)
                .ToListAsync(ct);

            return validationRows
                .Where(x => x.ErrorCount > 0 || x.WarningCount > 0)
                .Select(x => Row(
                    ("submission_id", x.Id),
                    ("return_code", x.ReturnCode),
                    ("module_code", x.ModuleCode),
                    ("period_code", FormatPeriodCode(x.Year, x.Month, x.Quarter)),
                    ("status", x.Status.ToString()),
                    ("error_count", x.ErrorCount),
                    ("warning_count", x.WarningCount),
                    ("submitted_at", x.SubmittedAt),
                    ("institution_name", x.InstitutionName)))
                .ToList();
        }

        var keyword = entities.SearchKeyword!;
        var submissions = await db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .ThenInclude(x => x!.Module)
            .Include(x => x.Institution)
            .Where(x =>
                x.TenantId == request.TenantId &&
                (x.ReturnCode.Contains(keyword) || (x.ParsedDataJson != null && x.ParsedDataJson.Contains(keyword))))
            .OrderByDescending(x => x.SubmittedAt)
            .Take(20)
            .ToListAsync(ct);

        return submissions.Select(x => Row(
                ("submission_id", x.Id),
                ("return_code", x.ReturnCode),
                ("module_code", x.ReturnPeriod?.Module?.ModuleCode ?? string.Empty),
                ("period_code", x.ReturnPeriod != null ? RegulatorAnalyticsSupport.FormatPeriodCode(x.ReturnPeriod) : string.Empty),
                ("status", x.Status.ToString()),
                ("submitted_at", x.SubmittedAt),
                ("institution_name", x.Institution?.InstitutionName ?? string.Empty)))
            .ToList();
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteSectorAggregateAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var allSnapshots = await LoadSnapshotsAsync(db, null, entities.ModuleCode, ct);
        var fieldCode = entities.FieldCodes[0];
        var filtered = allSnapshots
            .Where(x => x.Metrics.ContainsKey(fieldCode))
            .ToList();

        if (!string.IsNullOrWhiteSpace(entities.PeriodCode))
        {
            filtered = filtered.Where(x => x.PeriodCode == entities.PeriodCode).ToList();
        }

        if (!string.IsNullOrWhiteSpace(entities.LicenceCategory))
        {
            var licenceMap = await ResolveLicenceCategoryMapAsync(db, filtered.Select(x => x.TenantId).Distinct().ToList(), ct);
            filtered = filtered.Where(x => licenceMap.GetValueOrDefault(x.TenantId) == entities.LicenceCategory).ToList();
        }

        if (filtered.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var latestPeriod = filtered.MaxBy(x => x.PeriodSortKey)!.PeriodCode;
        var periodRows = filtered.Where(x => x.PeriodCode == latestPeriod).ToList();
        var values = periodRows.Select(x => x.Metrics[fieldCode].Value).OrderBy(x => x).ToList();

        return
        [
            Row(
                ("field_code", fieldCode),
                ("period_code", latestPeriod),
                ("module_code", periodRows[0].ModuleCode),
                ("entity_count", values.Count),
                ("sector_average", decimal.Round(values.Average(), 2)),
                ("sector_median", decimal.Round(AnomalySupport.Median(values), 2)),
                ("sector_min", values.Min()),
                ("sector_max", values.Max()),
                ("sector_std_dev", decimal.Round(AnomalySupport.StandardDeviation(values), 2)))
        ];
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteEntityCompareAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        if (entities.FieldCodes.Count == 0 || entities.EntityNames.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var fieldCode = entities.FieldCodes[0];
        var candidateInstitutions = await db.Institutions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(ct);
        var institutions = candidateInstitutions
            .Where(x => entities.EntityNames.Any(name =>
                x.InstitutionName.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (institutions.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var snapshots = await LoadSnapshotsAsync(db, null, entities.ModuleCode, ct);
        return institutions
            .Select(inst =>
            {
                var latest = snapshots
                    .Where(x => x.InstitutionId == inst.Id && x.Metrics.ContainsKey(fieldCode))
                    .OrderByDescending(x => x.PeriodSortKey)
                    .ThenByDescending(x => x.SubmittedAt)
                    .FirstOrDefault();

                if (latest is null)
                {
                    return null;
                }

                var metric = latest.Metrics[fieldCode];
                return Row(
                    ("institution_name", inst.InstitutionName),
                    ("field_code", metric.FieldCode),
                    ("field_label", metric.FieldLabel),
                    ("value", metric.Value),
                    ("period_code", latest.PeriodCode),
                    ("module_code", latest.ModuleCode));
            })
            .Where(x => x is not null)
            .Cast<Dictionary<string, object?>>()
            .ToList();
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteRiskRankingAsync(
        MetadataDbContext db,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryRequest request,
        CancellationToken ct)
    {
        var query = db.AnomalyReports
            .AsNoTracking()
            .Include(x => x.Findings)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(entities.ModuleCode))
        {
            query = query.Where(x => x.ModuleCode == entities.ModuleCode);
        }

        if (!string.IsNullOrWhiteSpace(entities.PeriodCode))
        {
            query = query.Where(x => x.PeriodCode == entities.PeriodCode);
        }

        var reports = await query
            .OrderBy(x => x.OverallQualityScore)
            .ThenByDescending(x => x.AlertCount)
            .Take(Math.Clamp(entities.RequestedTopCount, 1, 30))
            .ToListAsync(ct);

        if (reports.Count == 0)
        {
            return new List<Dictionary<string, object?>>();
        }

        var institutionIds = reports.Select(x => x.InstitutionId).Distinct().ToList();
        var licenceLookup = await db.Institutions
            .AsNoTracking()
            .Where(x => institutionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.LicenseType ?? "UNKNOWN", ct);

        return reports.Select(x => Row(
                ("institution_name", x.InstitutionName),
                ("licence_type", licenceLookup.GetValueOrDefault(x.InstitutionId, "UNKNOWN")),
                ("module_code", x.ModuleCode),
                ("period_code", x.PeriodCode),
                ("quality_score", x.OverallQualityScore),
                ("traffic_light", x.TrafficLight),
                ("alert_count", x.AlertCount),
                ("total_findings", x.TotalFindings),
                ("unacknowledged_count", x.Findings.Count(f => !f.IsAcknowledged))))
            .ToList();
    }

    private ComplianceIqQueryResponse BuildGroundedResponse(
        string originalQuery,
        ComplianceIqIntentClassification intent,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryPlan plan,
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyDictionary<string, string> config)
    {
        var response = new ComplianceIqQueryResponse
        {
            IntentCode = intent.IntentCode,
            Rows = rows.ToList(),
            VisualizationType = plan.VisualizationType
        };

        if (rows.Count == 0)
        {
            response.Answer = BuildNoDataMessage(intent.IntentCode, entities);
            response.ConfidenceLevel = "LOW";
            response.FollowUpSuggestions = BuildFollowUps(intent.IntentCode, entities, false);
            return response;
        }

        response.Citations = BuildCitations(rows, entities);
        response.FollowUpSuggestions = BuildFollowUps(intent.IntentCode, entities, IsRegulatorOnlyIntent(intent.IntentCode));
        response.ConfidenceLevel = DetermineConfidence(intent.Confidence, rows.Count, config);
        response.Answer = intent.IntentCode switch
        {
            "CURRENT_VALUE" => BuildCurrentValueAnswer(rows),
            "TREND" => BuildTrendAnswer(rows),
            "COMPARISON_PEER" => BuildPeerComparisonAnswer(rows),
            "COMPARISON_PERIOD" => BuildPeriodComparisonAnswer(rows),
            "DEADLINE" => BuildDeadlineAnswer(rows),
            "REGULATORY_LOOKUP" => BuildLookupAnswer(originalQuery, rows),
            "COMPLIANCE_STATUS" => BuildComplianceStatusAnswer(rows),
            "ANOMALY_STATUS" => BuildAnomalyAnswer(rows),
            "SCENARIO" => BuildScenarioAnswer(rows),
            "SEARCH" => BuildSearchAnswer(rows),
            "SECTOR_AGGREGATE" => BuildSectorAggregateAnswer(rows),
            "ENTITY_COMPARE" => BuildEntityCompareAnswer(rows),
            "RISK_RANKING" => BuildRiskRankingAnswer(rows),
            _ => BuildGenericTableAnswer(rows)
        };

        return response;
    }

    private static string BuildCurrentValueAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 1 && rows[0].ContainsKey("value"))
        {
            var row = rows[0];
            var label = row["field_label"]?.ToString() ?? row["field_code"]?.ToString() ?? "metric";
            return $"{label} is {FormatFieldValue(row["value"], row["field_code"]?.ToString())} as of {row["period_code"]} from {row["module_code"]}.";
        }

        var periodCode = rows[0]["period_code"]?.ToString() ?? "the latest filing";
        var moduleCode = rows[0]["module_code"]?.ToString() ?? "your return";
        var highlights = rows
            .Take(4)
            .Select(x => $"{x["field_label"]}: {FormatFieldValue(x["value"], x["field_code"]?.ToString())}");

        return $"Your latest key ratios from {moduleCode} for {periodCode} are {string.Join(", ", highlights)}.";
    }

    private static string BuildTrendAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var first = rows[0];
        var last = rows[^1];
        var start = Convert.ToDecimal(first["value"] ?? 0m, CultureInfo.InvariantCulture);
        var end = Convert.ToDecimal(last["value"] ?? 0m, CultureInfo.InvariantCulture);
        var change = end - start;
        var direction = change > 0m ? "up" : change < 0m ? "down" : "flat";
        var label = last["field_label"]?.ToString() ?? last["field_code"]?.ToString() ?? "metric";

        return $"{label} moved {direction} from {FormatFieldValue(start, last["field_code"]?.ToString())} in {first["period_code"]} to {FormatFieldValue(end, last["field_code"]?.ToString())} in {last["period_code"]}.";
    }

    private static string BuildPeerComparisonAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var row = rows[0];
        var label = row["field_label"]?.ToString() ?? row["field_code"]?.ToString() ?? "metric";
        var myValue = FormatFieldValue(row["my_value"], row["field_code"]?.ToString());
        var peerMedian = FormatFieldValue(row["peer_median"], row["field_code"]?.ToString());
        var deviation = Convert.ToDecimal(row["deviation_pct"] ?? 0m, CultureInfo.InvariantCulture);
        var direction = deviation >= 0m ? "above" : "below";
        return $"{label} is {myValue} for {row["period_code"]}, versus a peer median of {peerMedian}. That is {Math.Abs(deviation):N2}% {direction} the peer median for the {row["licence_category"]} segment.";
    }

    private static string BuildPeriodComparisonAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var ordered = rows.OrderBy(x => x["period_code"]?.ToString()).ToList();
        var first = ordered[0];
        var second = ordered[^1];
        var firstValue = Convert.ToDecimal(first["value"] ?? 0m, CultureInfo.InvariantCulture);
        var secondValue = Convert.ToDecimal(second["value"] ?? 0m, CultureInfo.InvariantCulture);
        var pct = firstValue == 0m ? 0m : decimal.Round(((secondValue - firstValue) / Math.Abs(firstValue)) * 100m, 2);
        var label = first["field_label"]?.ToString() ?? first["field_code"]?.ToString() ?? "metric";
        return $"{label} changed from {FormatFieldValue(firstValue, first["field_code"]?.ToString())} in {first["period_code"]} to {FormatFieldValue(secondValue, second["field_code"]?.ToString())} in {second["period_code"]}, a movement of {pct:N2}%.";
    }

    private static string BuildDeadlineAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var overdue = rows.Where(x => string.Equals(x["status"]?.ToString(), "OVERDUE", StringComparison.OrdinalIgnoreCase)).ToList();
        if (overdue.Count > 0)
        {
            var next = overdue[0];
            return $"You have {overdue.Count} overdue filing(s). The earliest overdue item is {next["module_code"]} for {next["period_code"]}, due on {FormatDate(next["due_date"])}.";
        }

        var upcoming = rows[0];
        return $"Your next filing is {upcoming["module_code"]} for {upcoming["period_code"]}, due on {FormatDate(upcoming["due_date"])}.";
    }

    private static string BuildLookupAnswer(string originalQuery, IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var first = rows[0];
        return $"I found {rows.Count} relevant reference item(s) for \"{originalQuery}\". The strongest match is {first["title"]}: {first["summary"]}.";
    }

    private static string BuildComplianceStatusAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var row = rows[0];
        return $"Your current Compliance Health Score is {FormatFieldValue(row["overall_score"], "score")} ({row["rating"]}) for {row["period_code"]}. Data quality is {FormatFieldValue(row["data_quality"], "ratio")} and filing timeliness is {FormatFieldValue(row["filing_timeliness"], "ratio")}.";
    }

    private static string BuildAnomalyAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        if (rows[0].ContainsKey("quality_score"))
        {
            var row = rows[0];
            return $"Your latest anomaly review is {row["traffic_light"]} with a quality score of {FormatFieldValue(row["quality_score"], "score")} for {row["module_code"]} {row["period_code"]}. Alerts: {row["alert_count"]}, warnings: {row["warning_count"]}, total findings: {row["total_findings"]}.";
        }

        var top = rows[0];
        return $"The latest detailed anomaly signal is {top["field_label"]} ({top["severity"]}) in {top["module_code"]} {top["period_code"]}: {top["explanation"]}";
    }

    private static string BuildScenarioAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var row = rows[0];
        return $"Under the requested NPL shock, CAR moves from {FormatFieldValue(row["current_car"], "ratio")} to {FormatFieldValue(row["projected_car"], "ratio")}. NPL ratio moves from {FormatFieldValue(row["current_npl_ratio"], "ratio")} to {FormatFieldValue(row["projected_npl_ratio"], "ratio")}.";
    }

    private static string BuildSearchAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var first = rows[0];
        return $"I found {rows.Count} matching submission record(s). The first result is {first["return_code"]} for {first["period_code"]} with status {first["status"]}.";
    }

    private static string BuildSectorAggregateAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var row = rows[0];
        return $"Across {row["entity_count"]} institution(s), {row["field_code"]} has a sector average of {FormatFieldValue(row["sector_average"], row["field_code"]?.ToString())} and a sector median of {FormatFieldValue(row["sector_median"], row["field_code"]?.ToString())} for {row["period_code"]}.";
    }

    private static string BuildEntityCompareAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var comparisons = rows
            .Take(4)
            .Select(x => $"{x["institution_name"]}: {FormatFieldValue(x["value"], x["field_code"]?.ToString())} ({x["period_code"]})");
        return $"Comparison results: {string.Join("; ", comparisons)}.";
    }

    private static string BuildRiskRankingAnswer(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var worst = rows[0];
        return $"The lowest-quality institution in the current ranking is {worst["institution_name"]} with a quality score of {FormatFieldValue(worst["quality_score"], "score")} for {worst["module_code"]} {worst["period_code"]}.";
    }

    private static string BuildGenericTableAnswer(IReadOnlyList<Dictionary<string, object?>> rows) =>
        $"I found {rows.Count} grounded result row(s) for your question.";

    private static string BuildNoDataMessage(string intentCode, ComplianceIqExtractedEntities entities) =>
        intentCode switch
        {
            "CURRENT_VALUE" => $"I could not find an accepted return containing {entities.FieldNames.FirstOrDefault() ?? entities.FieldCodes.FirstOrDefault() ?? "the requested metric"}.",
            "TREND" => "I could not find enough accepted historical data to build that trend.",
            "DEADLINE" => "I could not find matching filing calendar items for that question.",
            "ANOMALY_STATUS" => "I could not find anomaly reports matching that question yet.",
            _ => "I could not find grounded data for that question."
        };

    private static ComplianceIqQueryResponse BuildAccessDeniedResponse() =>
        new()
        {
            Answer = "That question requires regulator-level cross-tenant access. Ask the same question from the regulator workspace, or narrow it to your own institution.",
            IntentCode = "UNCLEAR",
            ConfidenceLevel = "HIGH",
            ErrorMessage = "REGULATOR_CONTEXT_REQUIRED"
        };

    private static ComplianceIqQueryResponse BuildClarificationResponse() =>
        new()
        {
            Answer = "I could not classify that question confidently. Try a more explicit prompt such as \"What is our current CAR?\", \"Show NPL trend over the last 8 quarters\", or \"When is our next filing due?\"",
            IntentCode = "UNCLEAR",
            ConfidenceLevel = "LOW",
            FollowUpSuggestions = new List<string>
            {
                "What is our current CAR?",
                "Show NPL trend over the last 8 quarters",
                "When is our next filing due?"
            }
        };

    private async Task<ComplianceIqQueryResponse> BuildHelpResponseAsync(
        MetadataDbContext db,
        bool isRegulatorContext,
        IReadOnlyDictionary<string, string> config,
        CancellationToken ct)
    {
        var message = config.GetValueOrDefault("help.welcome_message")
            ?? "Welcome to ComplianceIQ. Ask about returns, deadlines, anomalies, peer benchmarks, compliance health, or regulator intelligence.";

        var followUps = await db.ComplianceIqQuickQuestions
            .AsNoTracking()
            .Where(x => x.IsActive && x.RequiresRegulatorContext == isRegulatorContext)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.QuestionText)
            .Take(5)
            .ToListAsync(ct);

        return new ComplianceIqQueryResponse
        {
            Answer = message,
            IntentCode = "HELP",
            ConfidenceLevel = "HIGH",
            VisualizationType = "text",
            FollowUpSuggestions = followUps
        };
    }

    private static string DetermineConfidence(decimal intentConfidence, int rowCount, IReadOnlyDictionary<string, string> config)
    {
        var high = ParseDecimal(config, "confidence.high_threshold", 0.85m);
        var medium = ParseDecimal(config, "confidence.medium_threshold", 0.60m);

        if (rowCount == 0)
        {
            return "LOW";
        }

        if (intentConfidence >= high)
        {
            return "HIGH";
        }

        return intentConfidence >= medium ? "MEDIUM" : "LOW";
    }

    private static List<ComplianceIqCitation> BuildCitations(
        IReadOnlyList<Dictionary<string, object?>> rows,
        ComplianceIqExtractedEntities entities)
    {
        return rows
            .Take(5)
            .Select(row => new ComplianceIqCitation
            {
                SourceType = row.GetValueOrDefault("source_type")?.ToString() ?? "Regulatory Return",
                SourceModule = row.GetValueOrDefault("module_code")?.ToString() ?? entities.ModuleCode ?? string.Empty,
                SourceField = row.GetValueOrDefault("field_code")?.ToString() ?? entities.FieldCodes.FirstOrDefault() ?? string.Empty,
                SourcePeriod = row.GetValueOrDefault("period_code")?.ToString() ?? entities.PeriodCode ?? string.Empty,
                InstitutionName = row.GetValueOrDefault("institution_name")?.ToString()
            })
            .DistinctBy(x => $"{x.SourceType}|{x.SourceModule}|{x.SourceField}|{x.SourcePeriod}|{x.InstitutionName}")
            .ToList();
    }

    private static List<string> BuildFollowUps(
        string intentCode,
        ComplianceIqExtractedEntities entities,
        bool regulatorContext)
    {
        return intentCode switch
        {
            "CURRENT_VALUE" => new List<string>
            {
                $"Show {(entities.FieldNames.FirstOrDefault() ?? entities.FieldCodes.FirstOrDefault() ?? "this metric")} trend over the last 8 quarters",
                "How does this compare to peers?",
                "Do we have any anomalies in the latest return?"
            },
            "TREND" => new List<string>
            {
                "What is the latest value?",
                "How does this compare to peers?",
                "What anomalies were flagged in the latest return?"
            },
            "COMPARISON_PEER" => new List<string>
            {
                "Show the historical trend for this metric",
                "What is our current compliance health score?",
                "Are there anomalies on this field?"
            },
            "DEADLINE" => new List<string>
            {
                "Show overdue filings",
                "What is our compliance health score?",
                "Do we have anomalies in our latest return?"
            },
            "ANOMALY_STATUS" => new List<string>
            {
                "What is our overall quality score?",
                "Show the latest flagged fields",
                "How does our data quality compare with peers?"
            },
            "COMPLIANCE_STATUS" => new List<string>
            {
                "When is our next filing due?",
                "Do we have any anomaly alerts?",
                "Show our CAR trend"
            },
            "RISK_RANKING" when regulatorContext => new List<string>
            {
                "What is aggregate CAR across commercial banks?",
                "Compare two institutions on NPL ratio",
                "Show sector anomaly distribution for the latest period"
            },
            _ => new List<string>
            {
                "What is our current CAR?",
                "When is our next filing due?",
                regulatorContext ? "Rank institutions by anomaly density" : "Show NPL trend over the last 8 quarters"
            }
        };
    }

    private async Task<int> RecordTurnAsync(
        MetadataDbContext db,
        ComplianceIqConversation conversation,
        ComplianceIqQueryRequest request,
        int turnNumber,
        ComplianceIqIntentClassification intent,
        ComplianceIqExtractedEntities entities,
        ComplianceIqQueryPlan? plan,
        ExecutionResult execution,
        ComplianceIqQueryResponse response,
        CancellationToken ct)
    {
        var turn = new ComplianceIqTurn
        {
            ConversationId = conversation.Id,
            TenantId = request.TenantId,
            UserId = request.UserId,
            UserRole = request.UserRole,
            TurnNumber = turnNumber,
            QueryText = request.Query.Trim(),
            IntentCode = intent.IntentCode,
            IntentConfidence = intent.Confidence,
            ExtractedEntitiesJson = JsonSerializer.Serialize(entities, JsonOptions),
            TemplateCode = plan?.TemplateCode ?? string.Empty,
            ResolvedParametersJson = JsonSerializer.Serialize(plan?.Parameters ?? new Dictionary<string, string>(), JsonOptions),
            ExecutedPlan = plan?.Explanation ?? string.Empty,
            RowCount = execution.Rows.Count,
            ExecutionTimeMs = execution.ExecutionTimeMs,
            ResponseText = response.Answer,
            ResponseDataJson = JsonSerializer.Serialize(execution.Rows, JsonOptions),
            VisualizationType = response.VisualizationType,
            ConfidenceLevel = response.ConfidenceLevel,
            CitationsJson = JsonSerializer.Serialize(response.Citations, JsonOptions),
            FollowUpSuggestionsJson = JsonSerializer.Serialize(response.FollowUpSuggestions, JsonOptions),
            TotalTimeMs = response.TotalTimeMs,
            ErrorMessage = response.ErrorMessage,
            CreatedAt = DateTime.UtcNow
        };

        db.ComplianceIqTurns.Add(turn);
        conversation.TurnCount = turnNumber;
        conversation.LastActivityAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return turn.Id;
    }

    private async Task AuditTurnAsync(
        int turnId,
        ComplianceIqQueryRequest request,
        ComplianceIqIntentClassification intent,
        ComplianceIqQueryPlan? plan,
        ComplianceIqQueryResponse response,
        CancellationToken ct)
    {
        try
        {
            await _auditLogger.Log(
                "ComplianceIqTurn",
                turnId,
                "NL_QUERY_PROCESSED",
                null,
                new
                {
                    request.Query,
                    Intent = intent.IntentCode,
                    Plan = plan?.TemplateCode,
                    response.ConfidenceLevel,
                    RowCount = response.Rows.Count,
                    response.ErrorMessage
                },
                request.UserId,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to audit ComplianceIQ turn {TurnId}", turnId);
        }
    }

    private async Task<List<SubmissionSnapshot>> LoadSnapshotsAsync(
        MetadataDbContext db,
        Guid? tenantId,
        string? moduleCode,
        CancellationToken ct)
    {
        var query = db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .ThenInclude(x => x!.Module)
            .Include(x => x.Institution)
            .Where(x => AnomalySupport.AcceptedStatuses.Contains(x.Status) && x.ReturnPeriod != null && x.ReturnPeriod.Module != null);

        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantId == tenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(moduleCode))
        {
            query = query.Where(x => x.ReturnPeriod!.Module!.ModuleCode == moduleCode);
        }

        var rows = await query.ToListAsync(ct);
        return rows.Select(ToSnapshot).ToList();
    }

    private static SubmissionSnapshot ToSnapshot(Submission submission)
    {
        var period = submission.ReturnPeriod!;
        var module = period.Module!;
        return new SubmissionSnapshot(
            submission.Id,
            submission.TenantId,
            submission.InstitutionId,
            submission.Institution?.InstitutionName ?? $"Institution {submission.InstitutionId}",
            module.ModuleCode,
            module.RegulatorCode,
            RegulatorAnalyticsSupport.FormatPeriodCode(period),
            PeriodSortKey(period),
            submission.SubmittedAt ?? default,
            AnomalySupport.ExtractSubmissionMetrics(submission.ParsedDataJson));
    }

    private static int PeriodSortKey(ReturnPeriod period)
    {
        if (period.Quarter is >= 1 and <= 4)
        {
            return (period.Year * 100) + (period.Quarter.Value * 3);
        }

        return (period.Year * 100) + period.Month;
    }

    private static Dictionary<string, object?> ToMetricRow(SubmissionSnapshot snapshot, string fieldCode)
    {
        var metric = snapshot.Metrics[fieldCode];
        return Row(
            ("field_code", metric.FieldCode),
            ("field_label", metric.FieldLabel),
            ("value", metric.Value),
            ("period_code", snapshot.PeriodCode),
            ("module_code", snapshot.ModuleCode),
            ("institution_name", snapshot.InstitutionName));
    }

    private static decimal GetMetricValue(SubmissionSnapshot snapshot, string fieldCode, decimal fallback = 0m) =>
        snapshot.Metrics.TryGetValue(fieldCode, out var metric) ? metric.Value : fallback;

    private async Task<string?> ResolveLicenceCategoryAsync(MetadataDbContext db, Guid tenantId, CancellationToken ct)
    {
        var licenceCode = await db.TenantLicenceTypes
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .Include(x => x.LicenceType)
            .Select(x => x.LicenceType!.Code)
            .FirstOrDefaultAsync(ct);

        if (!string.IsNullOrWhiteSpace(licenceCode))
        {
            return licenceCode;
        }

        return await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.LicenseType)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<Dictionary<Guid, string?>> ResolveLicenceCategoryMapAsync(
        MetadataDbContext db,
        IReadOnlyList<Guid> tenantIds,
        CancellationToken ct)
    {
        var licenceRows = await db.TenantLicenceTypes
            .AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId) && x.IsActive)
            .Include(x => x.LicenceType)
            .ToListAsync(ct);

        return licenceRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.LicenceType?.Code).FirstOrDefault());
    }

    private async Task<AnomalyPeerGroupStatistic?> BuildFallbackPeerStatsAsync(
        MetadataDbContext db,
        Guid currentTenantId,
        string fieldCode,
        string? moduleCode,
        string periodCode,
        string licenceCategory,
        CancellationToken ct)
    {
        var snapshots = await LoadSnapshotsAsync(db, null, moduleCode, ct);
        var licenceMap = await ResolveLicenceCategoryMapAsync(db, snapshots.Select(x => x.TenantId).Distinct().ToList(), ct);

        var values = snapshots
            .Where(x => x.TenantId != currentTenantId &&
                        x.PeriodCode == periodCode &&
                        x.Metrics.ContainsKey(fieldCode) &&
                        string.Equals(licenceMap.GetValueOrDefault(x.TenantId), licenceCategory, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Metrics[fieldCode].Value)
            .OrderBy(x => x)
            .ToList();

        if (values.Count < 3)
        {
            return null;
        }

        return new AnomalyPeerGroupStatistic
        {
            FieldCode = fieldCode,
            LicenceCategory = licenceCategory,
            InstitutionSizeBand = "ALL",
            PeerCount = values.Count,
            PeerMean = decimal.Round(values.Average(), 2),
            PeerMedian = decimal.Round(AnomalySupport.Median(values), 2),
            PeerQ1 = decimal.Round(AnomalySupport.Percentile(values, 25m), 2),
            PeerQ3 = decimal.Round(AnomalySupport.Percentile(values, 75m), 2),
            PeerMin = values.Min(),
            PeerMax = values.Max(),
            PeerStdDev = decimal.Round(AnomalySupport.StandardDeviation(values), 2),
            PeriodCode = periodCode,
            ModuleCode = moduleCode ?? string.Empty
        };
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] pairs)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            row[key] = value;
        }

        return row;
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> config, string key, int fallback) =>
        config.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static decimal ParseDecimal(IReadOnlyDictionary<string, string> config, string key, decimal fallback) =>
        config.TryGetValue(key, out var raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static bool ContainsAny(string input, params string[] patterns) =>
        patterns.Any(pattern => input.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static string ExtractSummary(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(content, @"\s+", " ").Trim();
        return normalized.Length <= 180 ? normalized : normalized[..177] + "...";
    }

    private static string FormatPeriodCode(int year, int month, int? quarter)
    {
        if (quarter is >= 1 and <= 4)
        {
            return $"{year}-Q{quarter.Value}";
        }

        return $"{year}-{month:D2}";
    }

    private static string FormatDate(object? value) =>
        value switch
        {
            DateTime dateTime => dateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? string.Empty
        };

    private static string FormatFieldValue(object? value, string? fieldCode)
    {
        if (value is null)
        {
            return "N/A";
        }

        if (value is bool boolean)
        {
            return boolean ? "Yes" : "No";
        }

        if (!decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return value.ToString() ?? "N/A";
        }

        return LooksLikeRatio(fieldCode)
            ? $"{decimalValue:N2}%"
            : LooksLikeCurrency(fieldCode)
                ? $"₦{decimalValue:N2}"
                : $"{decimalValue:N2}";
    }

    private static bool LooksLikeRatio(string? fieldCode)
    {
        var normalized = fieldCode?.ToLowerInvariant() ?? string.Empty;
        return normalized.Contains("ratio", StringComparison.Ordinal) ||
               normalized is "roa" or "roe" or "score" or "quality_score" or "overall_score" or "projected_car" or "current_car";
    }

    private static bool LooksLikeCurrency(string? fieldCode)
    {
        var normalized = fieldCode?.ToLowerInvariant() ?? string.Empty;
        return normalized.Contains("amount", StringComparison.Ordinal) ||
               normalized.Contains("assets", StringComparison.Ordinal) ||
               normalized.Contains("capital", StringComparison.Ordinal) ||
               normalized.Contains("deposits", StringComparison.Ordinal) ||
               normalized.Contains("loans", StringComparison.Ordinal) ||
               normalized.Contains("funds", StringComparison.Ordinal) ||
               normalized.Contains("premium", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[A-Z]{2,5}/[A-Z]{2,5}/\d{4}/\d{2,5}", RegexOptions.IgnoreCase)]
    private static partial Regex CircularReferenceRegex();

    [GeneratedRegex(@"(?:(\d{4})\s*[-/ ]\s*)?Q([1-4])", RegexOptions.IgnoreCase)]
    private static partial Regex QuarterPeriodRegex();

    [GeneratedRegex(@"(\d{4})[-/](0[1-9]|1[0-2])", RegexOptions.IgnoreCase)]
    private static partial Regex MonthPeriodRegex();

    [GeneratedRegex(@"(?:last|past|previous)\s+(\d+)\s+(?:quarter|quarters|period|periods|month|months)", RegexOptions.IgnoreCase)]
    private static partial Regex TrendLookbackRegex();

    private sealed record SubmissionSnapshot(
        int SubmissionId,
        Guid TenantId,
        int InstitutionId,
        string InstitutionName,
        string ModuleCode,
        string RegulatorCode,
        string PeriodCode,
        int PeriodSortKey,
        DateTime SubmittedAt,
        Dictionary<string, AnomalySupport.MetricPoint> Metrics);

    private sealed record ExecutionResult(List<Dictionary<string, object?>> Rows, int ExecutionTimeMs)
    {
        public static ExecutionResult Empty { get; } = new(new List<Dictionary<string, object?>>(), 0);
    }
}
