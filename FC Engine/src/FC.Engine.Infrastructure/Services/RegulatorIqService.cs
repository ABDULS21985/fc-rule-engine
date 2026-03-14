using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

public sealed class RegulatorIqService : IRegulatorIqService
{
    private static readonly ConcurrentDictionary<string, List<DateTime>> RateWindows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ConfidentialIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEMIC_DASHBOARD",
        "CONTAGION_QUERY",
        "STRESS_SCENARIOS",
        "SANCTIONS_EXPOSURE",
        "CROSS_BORDER",
        "POLICY_IMPACT",
        "SUPERVISORY_ACTIONS",
        "EXAMINATION_BRIEF"
    };

    private static readonly HashSet<string> UnclassifiedIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "HELP",
        "REGULATORY_LOOKUP",
        "DEADLINE"
    };

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IAuditLogger _auditLogger;
    private readonly IRegulatorIntentClassifier _intentClassifier;
    private readonly IRegulatorResponseGenerator _responseGenerator;
    private readonly IRegulatorIntelligenceService _regulatorIntelligenceService;
    private readonly ILlmService _llmService;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RegulatorIqService> _logger;

    public RegulatorIqService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IAuditLogger auditLogger,
        IRegulatorIntentClassifier intentClassifier,
        IRegulatorResponseGenerator responseGenerator,
        IRegulatorIntelligenceService regulatorIntelligenceService,
        ILlmService llmService,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RegulatorIqService> logger)
    {
        _dbFactory = dbFactory;
        _auditLogger = auditLogger;
        _intentClassifier = intentClassifier;
        _responseGenerator = responseGenerator;
        _regulatorIntelligenceService = regulatorIntelligenceService;
        _llmService = llmService;
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<RegulatorIqTurnResult> QueryAsync(
        RegulatorIqQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new RegulatorIqTurnResult
            {
                Response = new RegulatorIqResponse
                {
                    AnswerText = "Ask about an institution, a sector trend, systemic risk, filing status, or an examination briefing.",
                    AnswerFormat = "text",
                    ClassificationLevel = "UNCLASSIFIED",
                    ConfidenceLevel = "LOW"
                },
                IntentCode = "HELP",
                ErrorMessage = "EMPTY_QUERY"
            };
        }

        var stopwatch = Stopwatch.StartNew();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, request, request.RegulatorId, ct);
        var config = await LoadComplianceIqConfigMapAsync(db, ct);
        var rateLimit = CheckRateLimit(context, config);
        if (rateLimit.IsExceeded)
        {
            return new RegulatorIqTurnResult
            {
                Response = new RegulatorIqResponse
                {
                    AnswerText = $"You have reached the RegulatorIQ query limit for the {rateLimit.ExceededWindow} window. Please try again in {rateLimit.RetryAfterSeconds} seconds.",
                    AnswerFormat = "text",
                    ClassificationLevel = "UNCLASSIFIED",
                    ConfidenceLevel = "HIGH",
                    FollowUpSuggestions = new List<string> { "What can I ask?" }
                },
                IntentCode = "HELP",
                TotalTimeMs = (int)stopwatch.ElapsedMilliseconds,
                ErrorMessage = "RATE_LIMITED"
            };
        }

        var conversation = await ResolveConversationAsync(db, context, request, ct);
        var regulatorContext = await BuildContextAsync(db, conversation, context, ct);
        var classifiedIntent = await _intentClassifier.ClassifyAsync(request.Query.Trim(), regulatorContext, ct);

        RegulatorIqResponse response;
        if (classifiedIntent.NeedsDisambiguation && classifiedIntent.DisambiguationOptions is { Count: > 0 })
        {
            response = new RegulatorIqResponse
            {
                AnswerText = "I found more than one grounded interpretation for that request. Choose one of the options below to continue.",
                AnswerFormat = "text",
                ClassificationLevel = ResolveClassificationLevel(classifiedIntent.IntentCode),
                ConfidenceLevel = ResolveConfidence(classifiedIntent.Confidence),
                FollowUpSuggestions = classifiedIntent.DisambiguationOptions.ToList()
            };
        }
        else
        {
            response = await _responseGenerator.GenerateAsync(request.Query.Trim(), classifiedIntent, regulatorContext, ct);
        }

        stopwatch.Stop();

        var turnId = await RecordTurnAsync(
            db,
            conversation,
            context,
            request,
            classifiedIntent,
            response,
            (int)stopwatch.ElapsedMilliseconds,
            ct);

        RecordRateLimit(context);
        await AppendAccessLogAsync(db, conversation.Id, turnId, context, request.Query.Trim(), response, ct);
        await AuditTurnAsync(turnId, context, request.Query.Trim(), classifiedIntent, response, ct);

        _logger.LogInformation(
            "RegulatorIQ query processed for regulator tenant {TenantId}: intent={IntentCode}, turn={TurnId}, entities={EntityCount}, classification={ClassificationLevel}",
            context.RegulatorTenantId,
            classifiedIntent.IntentCode,
            turnId,
            response.EntitiesAccessed.Count,
            response.ClassificationLevel);

        return new RegulatorIqTurnResult
        {
            ConversationId = conversation.Id,
            TurnId = turnId,
            IntentCode = classifiedIntent.IntentCode,
            NeedsDisambiguation = classifiedIntent.NeedsDisambiguation,
            DisambiguationOptions = classifiedIntent.DisambiguationOptions?.ToList() ?? new List<string>(),
            Response = response,
            TotalTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
    }

    public async Task<List<ComplianceIqTurn>> GetConversationHistoryAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, null, null, ct);

        return await db.ComplianceIqTurns
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.TenantId == context.RegulatorTenantId && x.UserId == context.RegulatorId)
            .OrderBy(x => x.TurnNumber)
            .ToListAsync(ct);
    }

    public async Task<Guid> StartExaminationSessionAsync(
        string regulatorId,
        Guid targetTenantId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, null, regulatorId, ct);
        var title = await BuildExaminationTitleAsync(db, targetTenantId, ct);

        var conversation = new ComplianceIqConversation
        {
            Id = Guid.NewGuid(),
            TenantId = context.RegulatorTenantId,
            UserId = context.RegulatorId,
            UserRole = context.UserRole,
            IsRegulatorContext = true,
            ExaminationTargetTenantId = targetTenantId,
            IsExaminationSession = true,
            Scope = "ENTITY",
            Title = title,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            IsActive = true,
            TurnCount = 0
        };

        db.ComplianceIqConversations.Add(conversation);
        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ComplianceIqConversation",
            0,
            "REGULATORIQ_EXAMINATION_STARTED",
            null,
            new { conversation.Id, targetTenantId },
            context.RegulatorId,
            ct);

        return conversation.Id;
    }

    public async Task EndExaminationSessionAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, null, null, ct);

        var conversation = await db.ComplianceIqConversations
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.TenantId == context.RegulatorTenantId && x.UserId == context.RegulatorId, ct)
            ?? throw new InvalidOperationException($"RegulatorIQ conversation {conversationId} was not found.");

        conversation.IsExaminationSession = false;
        conversation.ExaminationTargetTenantId = null;
        conversation.Scope = "SECTOR";
        conversation.LastActivityAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ComplianceIqConversation",
            0,
            "REGULATORIQ_EXAMINATION_ENDED",
            null,
            new { conversation.Id },
            context.RegulatorId,
            ct);
    }

    public async Task<byte[]> ExportConversationPdfAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, null, null, ct);

        var conversation = await db.ComplianceIqConversations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversationId && x.TenantId == context.RegulatorTenantId && x.UserId == context.RegulatorId, ct)
            ?? throw new InvalidOperationException($"RegulatorIQ conversation {conversationId} was not found.");

        var turns = await db.ComplianceIqTurns
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.TenantId == context.RegulatorTenantId && x.UserId == context.RegulatorId)
            .OrderBy(x => x.TurnNumber)
            .ToListAsync(ct);

        var tenantName = await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == context.RegulatorTenantId)
            .Select(x => x.TenantName)
            .FirstOrDefaultAsync(ct)
            ?? "Unknown regulator";

        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = new ConversationExportDocument(conversation, turns, tenantName).GeneratePdf();

        await _auditLogger.Log(
            "ComplianceIqConversation",
            0,
            "REGULATORIQ_CONVERSATION_EXPORTED",
            null,
            new { conversationId, turnCount = turns.Count },
            context.RegulatorId,
            ct);

        return pdf;
    }

    public async Task<byte[]> GenerateExaminationBriefingPdfAsync(
        Guid targetTenantId,
        string regulatorCode,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, null, null, ct);

        var briefing = await _regulatorIntelligenceService.GenerateExaminationBriefingAsync(targetTenantId, regulatorCode, ct);
        briefing.FocusAreas = await EnrichFocusAreasAsync(briefing, ct);

        QuestPDF.Settings.License = LicenseType.Community;
        var pdf = new ExaminationBriefingDocument(briefing, context.RegulatorName).GeneratePdf();

        await _auditLogger.Log(
            "ExaminationBriefing",
            0,
            "REGULATORIQ_BRIEFING_EXPORTED",
            null,
            new { targetTenantId, regulatorCode },
            context.RegulatorId,
            ct);

        return pdf;
    }

    public async Task SubmitFeedbackAsync(
        int turnId,
        int rating,
        string? feedbackText,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var context = await ResolveExecutionContextAsync(db, null, null, ct);

        var turn = await db.ComplianceIqTurns
            .FirstOrDefaultAsync(x => x.Id == turnId && x.TenantId == context.RegulatorTenantId && x.UserId == context.RegulatorId, ct)
            ?? throw new InvalidOperationException($"RegulatorIQ turn {turnId} was not found.");

        var safeRating = (short)Math.Clamp(rating, 1, 5);
        db.ComplianceIqFeedback.Add(new ComplianceIqFeedback
        {
            TurnId = turn.Id,
            UserId = context.RegulatorId,
            Rating = safeRating,
            FeedbackText = string.IsNullOrWhiteSpace(feedbackText) ? null : feedbackText.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ComplianceIqTurn",
            turn.Id,
            "REGULATORIQ_FEEDBACK_SUBMITTED",
            null,
            new { turn.Id, safeRating, feedbackText },
            context.RegulatorId,
            ct);
    }

    private async Task<ComplianceIqConversation> ResolveConversationAsync(
        MetadataDbContext db,
        ResolvedExecutionContext context,
        RegulatorIqQueryRequest request,
        CancellationToken ct)
    {
        if (request.ConversationId.HasValue)
        {
            var existing = await db.ComplianceIqConversations
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ConversationId.Value &&
                    x.TenantId == context.RegulatorTenantId &&
                    x.UserId == context.RegulatorId,
                    ct);

            if (existing is not null)
            {
                if (request.ExaminationTargetTenantId.HasValue)
                {
                    existing.ExaminationTargetTenantId = request.ExaminationTargetTenantId;
                    existing.IsExaminationSession = true;
                    existing.Scope = "ENTITY";
                }

                return existing;
            }
        }

        var conversation = new ComplianceIqConversation
        {
            Id = request.ConversationId ?? Guid.NewGuid(),
            TenantId = context.RegulatorTenantId,
            UserId = context.RegulatorId,
            UserRole = context.UserRole,
            IsRegulatorContext = true,
            ExaminationTargetTenantId = request.ExaminationTargetTenantId,
            IsExaminationSession = request.ExaminationTargetTenantId.HasValue,
            Scope = request.ExaminationTargetTenantId.HasValue ? "ENTITY" : request.Scope ?? "SECTOR",
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

    private async Task<RegulatorContext> BuildContextAsync(
        MetadataDbContext db,
        ComplianceIqConversation conversation,
        ResolvedExecutionContext context,
        CancellationToken ct)
    {
        var recentTurns = await db.ComplianceIqTurns
            .AsNoTracking()
            .Where(x => x.ConversationId == conversation.Id)
            .OrderByDescending(x => x.TurnNumber)
            .Take(10)
            .ToListAsync(ct);

        var recentEntityIds = recentTurns
            .SelectMany(x => ParseGuidArray(x.EntitiesAccessedJson))
            .Distinct()
            .Take(5)
            .ToList();

        var entityNames = await db.Institutions
            .AsNoTracking()
            .Where(x => recentEntityIds.Contains(x.TenantId))
            .Select(x => new { x.TenantId, x.InstitutionName })
            .ToListAsync(ct);

        var recentEntities = recentEntityIds
            .Select(id =>
            {
                var name = entityNames.FirstOrDefault(x => x.TenantId == id)?.InstitutionName ?? id.ToString("D");
                return (id, name);
            })
            .ToList();

        return new RegulatorContext
        {
            RegulatorTenantId = context.RegulatorTenantId,
            RegulatorCode = context.RegulatorCode,
            RegulatorName = context.RegulatorName,
            CurrentExaminationEntityId = conversation.ExaminationTargetTenantId,
            CurrentScope = conversation.Scope ?? "SECTOR",
            RecentEntities = recentEntities,
            RecentTurns = recentTurns
                .OrderBy(x => x.TurnNumber)
                .Select(x => (x.QueryText, x.IntentCode))
                .ToList()
        };
    }

    private async Task<int> RecordTurnAsync(
        MetadataDbContext db,
        ComplianceIqConversation conversation,
        ResolvedExecutionContext context,
        RegulatorIqQueryRequest request,
        RegulatorIntentResult intent,
        RegulatorIqResponse response,
        int totalTimeMs,
        CancellationToken ct)
    {
        var turnNumber = conversation.TurnCount + 1;
        var templateCode = await ResolveTemplateCodeAsync(db, intent.IntentCode, ct);
        var now = DateTime.UtcNow;

        var turn = new ComplianceIqTurn
        {
            ConversationId = conversation.Id,
            TenantId = context.RegulatorTenantId,
            UserId = context.RegulatorId,
            UserRole = context.UserRole,
            TurnNumber = turnNumber,
            QueryText = request.Query.Trim(),
            IntentCode = intent.IntentCode,
            IntentConfidence = intent.Confidence,
            ExtractedEntitiesJson = JsonSerializer.Serialize(new
            {
                intent.ResolvedEntityNames,
                intent.ResolvedEntityIds,
                intent.PeriodCode,
                intent.FieldCode,
                intent.LicenceCategory,
                intent.ExtractedParameters
            }, JsonOptions),
            TemplateCode = templateCode,
            ResolvedParametersJson = JsonSerializer.Serialize(BuildResolvedParameterBag(intent), JsonOptions),
            ExecutedPlan = BuildExecutedPlan(intent, response),
            RowCount = DetermineRowCount(response),
            ExecutionTimeMs = totalTimeMs,
            ResponseText = response.AnswerText,
            ResponseDataJson = SerializeStructuredData(response.StructuredData),
            VisualizationType = response.AnswerFormat,
            ConfidenceLevel = response.ConfidenceLevel,
            CitationsJson = JsonSerializer.Serialize(response.Citations, JsonOptions),
            FollowUpSuggestionsJson = JsonSerializer.Serialize(response.FollowUpSuggestions, JsonOptions),
            EntitiesAccessedJson = JsonSerializer.Serialize(response.EntitiesAccessed.Distinct().ToList(), JsonOptions),
            DataSourcesUsed = string.Join(",", response.DataSourcesUsed.Distinct(StringComparer.OrdinalIgnoreCase)),
            ClassificationLevel = string.IsNullOrWhiteSpace(response.ClassificationLevel)
                ? ResolveClassificationLevel(intent.IntentCode)
                : response.ClassificationLevel,
            RegulatorAgency = context.RegulatorCode,
            TotalTimeMs = totalTimeMs,
            ErrorMessage = null,
            CreatedAt = now
        };

        db.ComplianceIqTurns.Add(turn);

        conversation.TurnCount = turnNumber;
        conversation.LastActivityAt = now;
        conversation.Scope = ResolveScope(intent.IntentCode, response.EntitiesAccessed.Count, conversation.IsExaminationSession);
        if (request.ExaminationTargetTenantId.HasValue)
        {
            conversation.ExaminationTargetTenantId = request.ExaminationTargetTenantId;
            conversation.IsExaminationSession = true;
            conversation.Scope = "ENTITY";
        }

        await db.SaveChangesAsync(ct);
        return turn.Id;
    }

    private async Task AppendAccessLogAsync(
        MetadataDbContext db,
        Guid conversationId,
        int turnId,
        ResolvedExecutionContext context,
        string query,
        RegulatorIqResponse response,
        CancellationToken ct)
    {
        var accessLog = new RegIqAccessLog
        {
            RegulatorTenantId = context.RegulatorTenantId,
            ConversationId = conversationId,
            TurnId = turnId,
            RegulatorId = context.RegulatorId,
            RegulatorAgency = context.RegulatorCode,
            RegulatorRole = context.UserRole,
            QueryText = query,
            ResponseSummary = Truncate(response.AnswerText, 500),
            ClassificationLevel = response.ClassificationLevel,
            EntitiesAccessedJson = JsonSerializer.Serialize(response.EntitiesAccessed.Distinct().ToList(), JsonOptions),
            PrimaryEntityTenantId = response.EntitiesAccessed.Count > 0 ? response.EntitiesAccessed[0] : null,
            DataSourcesAccessedJson = JsonSerializer.Serialize(response.DataSourcesUsed.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), JsonOptions),
            FilterContextJson = JsonSerializer.Serialize(new { context.RegulatorCode }, JsonOptions),
            IpAddress = context.IpAddress,
            SessionId = context.SessionId,
            AccessedAt = DateTime.UtcNow,
            RetainUntil = DateTime.UtcNow.AddYears(7)
        };

        db.RegIqAccessLogs.Add(accessLog);
        await db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "RegIqAccessLog",
            accessLog.Id > int.MaxValue ? 0 : (int)accessLog.Id,
            "REGIQ_ACCESS",
            null,
            new
            {
                accessLog.ConversationId,
                accessLog.TurnId,
                accessLog.ClassificationLevel,
                response.DataSourcesUsed,
                response.EntitiesAccessed
            },
            context.RegulatorId,
            ct);
    }

    private async Task AuditTurnAsync(
        int turnId,
        ResolvedExecutionContext context,
        string query,
        RegulatorIntentResult intent,
        RegulatorIqResponse response,
        CancellationToken ct)
    {
        try
        {
            await _auditLogger.Log(
                "ComplianceIqTurn",
                turnId,
                "REGULATORIQ_QUERY_PROCESSED",
                null,
                new
                {
                    Query = query,
                    Intent = intent.IntentCode,
                    ClassificationLevel = response.ClassificationLevel,
                    response.AnswerFormat,
                    response.DataSourcesUsed,
                    response.EntitiesAccessed
                },
                context.RegulatorId,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to audit RegulatorIQ turn {TurnId}", turnId);
        }
    }

    private async Task<List<RegIqFocusArea>> EnrichFocusAreasAsync(
        ExaminationBriefing briefing,
        CancellationToken ct)
    {
        var fallback = briefing.FocusAreas
            .DistinctBy(x => $"{x.Area}:{x.Priority}", StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        try
        {
            var prompt = BuildFocusAreaPrompt(briefing);
            var structured = await _llmService.CompleteStructuredAsync<FocusAreaSuggestionSet>(
                new LlmRequest
                {
                    SystemPrompt = "Return strict JSON with a focusAreas array. Each item must have area, reason, and priority. Use Nigerian supervisory language and concise examination focus areas.",
                    UserMessage = prompt,
                    Temperature = 0.1m,
                    MaxTokens = 600,
                    ResponseFormat = "json"
                },
                ct);

            if (structured.FocusAreas.Count == 0)
            {
                return fallback;
            }

            return structured.FocusAreas
                .Concat(fallback)
                .Where(x => !string.IsNullOrWhiteSpace(x.Area) && !string.IsNullOrWhiteSpace(x.Reason))
                .DistinctBy(x => $"{x.Area}:{x.Priority}", StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falling back to deterministic focus areas for entity {TenantId}.", briefing.TenantId);
            return fallback;
        }
    }

    private static string BuildFocusAreaPrompt(ExaminationBriefing briefing)
    {
        var profile = briefing.Profile;
        return $"""
Generate supervisory examination focus areas for {briefing.InstitutionName} ({briefing.LicenceCategory}).
Current CAR: {GetMetricValue(profile, "carratio")}
Current NPL: {GetMetricValue(profile, "nplratio")}
Current Liquidity Ratio: {GetMetricValue(profile, "liquidityratio")}
CHS Score: {(profile.ComplianceHealth is null ? "N/A" : profile.ComplianceHealth.OverallScore.ToString("N1"))}
Anomaly Findings: {profile.Anomaly?.TotalFindings ?? 0}
Filing Overdues: {profile.FilingTimeliness?.OverdueFilings ?? 0}
Sanctions Matches: {profile.SanctionsExposure?.MatchCount ?? 0}
Early Warning Flags: {profile.EarlyWarningFlags.Count}
Existing deterministic focus areas:
{string.Join(Environment.NewLine, briefing.FocusAreas.Select(x => $"- {x.Area}: {x.Reason} ({x.Priority})"))}
""";
    }

    private static string? GetMetricValue(EntityIntelligenceProfile profile, string metricCode) =>
        profile.KeyMetrics.FirstOrDefault(x => string.Equals(x.MetricCode, metricCode, StringComparison.OrdinalIgnoreCase))?.Value?.ToString("N2");

    private static Dictionary<string, string> BuildResolvedParameterBag(RegulatorIntentResult intent)
    {
        var parameters = new Dictionary<string, string>(intent.ExtractedParameters, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(intent.PeriodCode))
        {
            parameters["periodCode"] = intent.PeriodCode;
        }

        if (!string.IsNullOrWhiteSpace(intent.FieldCode))
        {
            parameters["fieldCode"] = intent.FieldCode;
        }

        if (!string.IsNullOrWhiteSpace(intent.LicenceCategory))
        {
            parameters["licenceCategory"] = intent.LicenceCategory;
        }

        if (intent.ResolvedEntityIds.Count > 0)
        {
            parameters["entityIds"] = string.Join(",", intent.ResolvedEntityIds);
        }

        return parameters;
    }

    private static int DetermineRowCount(RegulatorIqResponse response)
    {
        return response.StructuredData switch
        {
            RegulatorTableData table => table.Rows.Count,
            RegulatorRankingData ranking => ranking.Items.Count,
            RegulatorComparisonData comparison => comparison.Rows.Count,
            RegulatorChartData chart => chart.Labels.Count,
            ExaminationBriefing => 1,
            RegulatorProfileData => 1,
            null => 0,
            _ => 1
        };
    }

    private static string SerializeStructuredData(object? structuredData)
    {
        if (structuredData is null)
        {
            return "null";
        }

        return JsonSerializer.Serialize(structuredData, structuredData.GetType(), JsonOptions);
    }

    private static string BuildExecutedPlan(RegulatorIntentResult intent, RegulatorIqResponse response)
    {
        var sources = response.DataSourcesUsed.Count == 0
            ? "none"
            : string.Join(", ", response.DataSourcesUsed.Distinct(StringComparer.OrdinalIgnoreCase));
        return $"RegulatorIQ orchestrator classified {intent.IntentCode}, routed through the regulator response generator, and grounded the answer using {sources}.";
    }

    private async Task<string> ResolveTemplateCodeAsync(
        MetadataDbContext db,
        string intentCode,
        CancellationToken ct)
    {
        return await db.ComplianceIqTemplates
            .AsNoTracking()
            .Where(x => x.IntentCode == intentCode && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.TemplateCode)
            .FirstOrDefaultAsync(ct)
            ?? intentCode;
    }

    private static string BuildConversationTitle(string query)
    {
        var trimmed = query.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..77] + "...";
    }

    private async Task<string> BuildExaminationTitleAsync(
        MetadataDbContext db,
        Guid targetTenantId,
        CancellationToken ct)
    {
        var institutionName = await db.Institutions
            .AsNoTracking()
            .Where(x => x.TenantId == targetTenantId)
            .Select(x => x.InstitutionName)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrWhiteSpace(institutionName)
            ? "RegulatorIQ examination session"
            : $"Examination: {institutionName}";
    }

    private static ComplianceIqRateLimitResult CheckRateLimit(
        ResolvedExecutionContext context,
        IReadOnlyDictionary<string, string> config)
    {
        var perMinute = ParseInt(config, "rate.regulator_queries_per_minute", 30);
        var perHour = ParseInt(config, "rate.regulator_queries_per_hour", 300);
        var perDay = ParseInt(config, "rate.regulator_queries_per_day", 1500);
        var now = DateTime.UtcNow;
        var key = $"{context.RegulatorTenantId:N}:{context.RegulatorId}".ToLowerInvariant();
        var timestamps = RateWindows.GetOrAdd(key, _ => new List<DateTime>());

        lock (timestamps)
        {
            timestamps.RemoveAll(x => x < now.AddDays(-1));

            var minuteCount = timestamps.Count(x => x > now.AddMinutes(-1));
            if (minuteCount >= perMinute)
            {
                return new ComplianceIqRateLimitResult
                {
                    UserId = context.RegulatorId,
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
                    UserId = context.RegulatorId,
                    IsExceeded = true,
                    ExceededWindow = "hour",
                    RetryAfterSeconds = 3600
                };
            }

            if (timestamps.Count >= perDay)
            {
                return new ComplianceIqRateLimitResult
                {
                    UserId = context.RegulatorId,
                    IsExceeded = true,
                    ExceededWindow = "day",
                    RetryAfterSeconds = 86400
                };
            }
        }

        return new ComplianceIqRateLimitResult { UserId = context.RegulatorId };
    }

    private static void RecordRateLimit(ResolvedExecutionContext context)
    {
        var key = $"{context.RegulatorTenantId:N}:{context.RegulatorId}".ToLowerInvariant();
        var timestamps = RateWindows.GetOrAdd(key, _ => new List<DateTime>());
        lock (timestamps)
        {
            timestamps.Add(DateTime.UtcNow);
        }
    }

    private static async Task<Dictionary<string, string>> LoadComplianceIqConfigMapAsync(MetadataDbContext db, CancellationToken ct)
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

    private async Task<ResolvedExecutionContext> ResolveExecutionContextAsync(
        MetadataDbContext db,
        RegulatorIqQueryRequest? request,
        string? explicitRegulatorId,
        CancellationToken ct)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var regulatorTenantId = request?.RegulatorTenantId
            ?? _tenantContext.CurrentTenantId
            ?? TryParseGuid(principal?.FindFirst("TenantId")?.Value)
            ?? throw new InvalidOperationException("Regulator tenant context is unavailable.");

        var regulatorCode = request?.RegulatorCode
            ?? principal?.FindFirst("RegulatorCode")?.Value
            ?? throw new InvalidOperationException("Regulator code is unavailable for this request.");

        var regulatorId = request?.RegulatorId
            ?? explicitRegulatorId
            ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal?.Identity?.Name
            ?? throw new InvalidOperationException("Regulator identifier is unavailable for this request.");

        var userRole = request?.UserRole
            ?? principal?.FindFirst(ClaimTypes.Role)?.Value
            ?? "Regulator";

        var regulatorName = await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == regulatorTenantId && x.TenantType == TenantType.Regulator)
            .Select(x => x.TenantName)
            .FirstOrDefaultAsync(ct)
            ?? throw new UnauthorizedAccessException("RegulatorIQ is restricted to regulator tenants.");

        return new ResolvedExecutionContext(
            regulatorTenantId,
            regulatorId,
            userRole,
            regulatorCode,
            regulatorName,
            request?.IpAddress ?? _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            request?.SessionId ?? _httpContextAccessor.HttpContext?.TraceIdentifier);
    }

    private static Guid? TryParseGuid(string? raw)
    {
        return Guid.TryParse(raw, out var value) ? value : null;
    }

    private static int ParseInt(IReadOnlyDictionary<string, string> config, string key, int fallback)
    {
        return config.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static string ResolveConfidence(decimal confidence)
    {
        return confidence switch
        {
            >= 0.85m => "HIGH",
            >= 0.60m => "MEDIUM",
            _ => "LOW"
        };
    }

    private static string ResolveClassificationLevel(string? intentCode)
    {
        if (string.IsNullOrWhiteSpace(intentCode))
        {
            return "RESTRICTED";
        }

        if (ConfidentialIntents.Contains(intentCode))
        {
            return "CONFIDENTIAL";
        }

        return UnclassifiedIntents.Contains(intentCode) ? "UNCLASSIFIED" : "RESTRICTED";
    }

    private static string ResolveScope(string intentCode, int entityCount, bool examinationSession)
    {
        if (examinationSession || entityCount > 0)
        {
            return "ENTITY";
        }

        return intentCode.ToUpperInvariant() switch
        {
            "SYSTEMIC_DASHBOARD" or "CONTAGION_QUERY" or "STRESS_SCENARIOS" or "SUPERVISORY_ACTIONS" => "SYSTEMIC",
            _ => "SECTOR"
        };
    }

    private static List<Guid> ParseGuidArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value[..maxLength];
    }

    private sealed record ResolvedExecutionContext(
        Guid RegulatorTenantId,
        string RegulatorId,
        string UserRole,
        string RegulatorCode,
        string RegulatorName,
        string? IpAddress,
        string? SessionId);

    private sealed class FocusAreaSuggestionSet
    {
        public List<RegIqFocusArea> FocusAreas { get; set; } = new();
    }
}
