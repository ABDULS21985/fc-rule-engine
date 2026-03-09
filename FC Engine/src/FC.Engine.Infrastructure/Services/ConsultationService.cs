using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class ConsultationService : IConsultationService
{
    private readonly MetadataDbContext _db;
    private readonly IPolicyAuditLogger _audit;
    private readonly ILogger<ConsultationService> _log;

    public ConsultationService(MetadataDbContext db, IPolicyAuditLogger audit, ILogger<ConsultationService> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<long> CreateConsultationAsync(
        long scenarioId, int regulatorId, string title, string? coverNote,
        DateOnly deadline, IReadOnlyList<ConsultationProvisionInput> provisions,
        int userId, CancellationToken ct = default)
    {
        var scenario = await _db.PolicyScenarios
            .FirstOrDefaultAsync(s => s.Id == scenarioId && s.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Policy scenario {scenarioId} not found.");

        var consultation = new ConsultationRound
        {
            ScenarioId = scenarioId,
            RegulatorId = regulatorId,
            Title = title,
            CoverNote = coverNote,
            DeadlineDate = deadline,
            Status = ConsultationStatus.Draft,
            TargetEntityTypes = scenario.TargetEntityTypes,
            CreatedByUserId = userId
        };

        _db.ConsultationRounds.Add(consultation);
        await _db.SaveChangesAsync(ct);

        foreach (var p in provisions)
        {
            _db.ConsultationProvisions.Add(new ConsultationProvision
            {
                ConsultationId = consultation.Id,
                ProvisionNumber = p.ProvisionNumber,
                ProvisionTitle = p.ProvisionTitle,
                ProvisionText = p.ProvisionText,
                RelatedParameterCode = p.RelatedParameterCode,
                DisplayOrder = p.ProvisionNumber
            });
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(scenarioId, regulatorId, Guid.NewGuid(),
            "CONSULTATION_CREATED", new { consultationId = consultation.Id, title, provisionCount = provisions.Count }, userId, ct);

        return consultation.Id;
    }

    public async Task PublishConsultationAsync(
        long consultationId, int regulatorId, int userId, CancellationToken ct = default)
    {
        var consultation = await _db.ConsultationRounds
            .FirstOrDefaultAsync(c => c.Id == consultationId && c.RegulatorId == regulatorId && c.Status == ConsultationStatus.Draft, ct)
            ?? throw new InvalidOperationException("Consultation not found or not in DRAFT status.");

        consultation.Status = ConsultationStatus.Published;
        consultation.PublishedAt = DateTime.UtcNow;
        consultation.UpdatedAt = DateTime.UtcNow;

        // Update parent scenario status
        var scenario = await _db.PolicyScenarios.FindAsync(new object[] { consultation.ScenarioId }, ct);
        if (scenario is not null)
        {
            scenario.Status = PolicyStatus.Consultation;
            scenario.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(consultation.ScenarioId, regulatorId, Guid.NewGuid(),
            "CONSULTATION_PUBLISHED", new { consultationId, deadline = consultation.DeadlineDate }, userId, ct);
    }

    public async Task CloseConsultationAsync(
        long consultationId, int regulatorId, int userId, CancellationToken ct = default)
    {
        var consultation = await _db.ConsultationRounds
            .FirstOrDefaultAsync(c => c.Id == consultationId && c.RegulatorId == regulatorId
                && (c.Status == ConsultationStatus.Published || c.Status == ConsultationStatus.Open), ct)
            ?? throw new InvalidOperationException("Consultation not found or not in an open status.");

        consultation.Status = ConsultationStatus.Closed;
        consultation.UpdatedAt = DateTime.UtcNow;

        // Update parent scenario
        var scenario = await _db.PolicyScenarios.FindAsync(new object[] { consultation.ScenarioId }, ct);
        if (scenario is not null)
        {
            scenario.Status = PolicyStatus.FeedbackClosed;
            scenario.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<FeedbackAggregationResult> AggregateFeedbackAsync(
        long consultationId, int regulatorId, int userId, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();

        var consultation = await _db.ConsultationRounds
            .FirstOrDefaultAsync(c => c.Id == consultationId && c.RegulatorId == regulatorId && c.Status == ConsultationStatus.Closed, ct)
            ?? throw new InvalidOperationException("Consultation not found, not owned, or not yet closed.");

        // Aggregate overall positions
        var feedbackEntries = await _db.ConsultationFeedback
            .Where(f => f.ConsultationId == consultationId)
            .ToListAsync(ct);

        var overallPositions = feedbackEntries
            .GroupBy(f => f.OverallPosition.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var totalFeedback = feedbackEntries.Count;

        // Aggregate per provision
        var provisions = await _db.ConsultationProvisions
            .Where(p => p.ConsultationId == consultationId)
            .OrderBy(p => p.ProvisionNumber)
            .ToListAsync(ct);

        var allProvisionFeedback = await _db.ProvisionFeedback
            .Include(pf => pf.Feedback)
            .Where(pf => pf.Feedback!.ConsultationId == consultationId)
            .ToListAsync(ct);

        var provisionAggregates = new List<FeedbackAggregateByProvision>();

        foreach (var provision in provisions)
        {
            var provFeedback = allProvisionFeedback.Where(pf => pf.ProvisionId == provision.Id).ToList();

            var support = provFeedback.Count(f => f.Position == ProvisionPosition.Support);
            var oppose = provFeedback.Count(f => f.Position == ProvisionPosition.Oppose);
            var neutral = provFeedback.Count(f => f.Position == ProvisionPosition.Neutral);
            var amend = provFeedback.Count(f => f.Position == ProvisionPosition.Amend);
            var provTotal = support + oppose + neutral + amend;

            // Breakdown by entity type
            var entityBreakdown = provFeedback
                .GroupBy(f => f.Feedback!.EntityType)
                .ToDictionary(
                    g => g.Key,
                    g => new EntityTypeBreakdown(
                        g.Count(f => f.Position == ProvisionPosition.Support),
                        g.Count(f => f.Position == ProvisionPosition.Oppose),
                        g.Count(f => f.Position == ProvisionPosition.Neutral),
                        g.Count(f => f.Position == ProvisionPosition.Amend),
                        g.Count()));

            var concerns = provFeedback
                .Where(f => f.Position is ProvisionPosition.Oppose or ProvisionPosition.Amend
                    && !string.IsNullOrEmpty(f.Reasoning))
                .Select(f => f.Reasoning!)
                .Take(10).ToList();

            var amendments = provFeedback
                .Where(f => f.Position == ProvisionPosition.Amend
                    && !string.IsNullOrEmpty(f.SuggestedAmendment))
                .Select(f => f.SuggestedAmendment!)
                .Take(5).ToList();

            var aggregate = new FeedbackAggregateByProvision(
                provision.Id, provision.ProvisionNumber, provision.ProvisionTitle,
                provTotal, support, oppose, neutral, amend,
                provTotal > 0 ? Math.Round((decimal)support / provTotal * 100, 2) : 0m,
                provTotal > 0 ? Math.Round((decimal)oppose / provTotal * 100, 2) : 0m,
                entityBreakdown, concerns, amendments);

            // Persist/update aggregation
            var existing = await _db.FeedbackAggregations
                .FirstOrDefaultAsync(a => a.ConsultationId == consultationId && a.ProvisionId == provision.Id, ct);

            if (existing is not null)
            {
                existing.TotalResponses = provTotal;
                existing.SupportCount = support;
                existing.OpposeCount = oppose;
                existing.NeutralCount = neutral;
                existing.AmendCount = amend;
                existing.SupportPercentage = aggregate.SupportPercentage;
                existing.OpposePercentage = aggregate.OpposePercentage;
                existing.ByEntityType = JsonSerializer.Serialize(entityBreakdown);
                existing.TopConcerns = JsonSerializer.Serialize(concerns);
                existing.TopSuggestedAmendments = JsonSerializer.Serialize(amendments);
                existing.ComputedAt = DateTime.UtcNow;
            }
            else
            {
                _db.FeedbackAggregations.Add(new FeedbackAggregation
                {
                    ConsultationId = consultationId,
                    ProvisionId = provision.Id,
                    TotalResponses = provTotal,
                    SupportCount = support,
                    OpposeCount = oppose,
                    NeutralCount = neutral,
                    AmendCount = amend,
                    SupportPercentage = aggregate.SupportPercentage,
                    OpposePercentage = aggregate.OpposePercentage,
                    ByEntityType = JsonSerializer.Serialize(entityBreakdown),
                    TopConcerns = JsonSerializer.Serialize(concerns),
                    TopSuggestedAmendments = JsonSerializer.Serialize(amendments)
                });
            }

            provisionAggregates.Add(aggregate);
        }

        // Update consultation status
        consultation.Status = ConsultationStatus.Aggregated;
        consultation.TotalFeedbackReceived = totalFeedback;
        consultation.AggregationCompletedAt = DateTime.UtcNow;
        consultation.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(consultation.ScenarioId, regulatorId, correlationId,
            "FEEDBACK_AGGREGATED",
            new { consultationId, totalFeedback, provisionCount = provisions.Count }, userId, ct);

        return new FeedbackAggregationResult(consultationId, totalFeedback, provisionAggregates, overallPositions);
    }

    public async Task<ConsultationDetail> GetConsultationAsync(
        long consultationId, int regulatorId, CancellationToken ct = default)
    {
        var consultation = await _db.ConsultationRounds
            .FirstOrDefaultAsync(c => c.Id == consultationId && c.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Consultation {consultationId} not found.");

        var provisions = await _db.ConsultationProvisions
            .Where(p => p.ConsultationId == consultationId)
            .OrderBy(p => p.ProvisionNumber)
            .ToListAsync(ct);

        var aggregations = await _db.FeedbackAggregations
            .Where(a => a.ConsultationId == consultationId)
            .ToDictionaryAsync(a => a.ProvisionId, ct);

        var provisionDetails = provisions.Select(p =>
        {
            FeedbackAggregateByProvision? agg = null;
            if (aggregations.TryGetValue(p.Id, out var aggRow))
            {
                agg = new FeedbackAggregateByProvision(
                    p.Id, p.ProvisionNumber, p.ProvisionTitle,
                    aggRow.TotalResponses, aggRow.SupportCount, aggRow.OpposeCount,
                    aggRow.NeutralCount, aggRow.AmendCount,
                    aggRow.SupportPercentage, aggRow.OpposePercentage,
                    JsonSerializer.Deserialize<Dictionary<string, EntityTypeBreakdown>>(aggRow.ByEntityType) ?? new(),
                    JsonSerializer.Deserialize<List<string>>(aggRow.TopConcerns ?? "[]") ?? [],
                    JsonSerializer.Deserialize<List<string>>(aggRow.TopSuggestedAmendments ?? "[]") ?? []);
            }
            return new ConsultationProvisionDetail(p.Id, p.ProvisionNumber, p.ProvisionTitle, p.ProvisionText, agg);
        }).ToList();

        return new ConsultationDetail(
            consultation.Id, consultation.Title, consultation.CoverNote,
            consultation.Status, consultation.DeadlineDate,
            consultation.TotalFeedbackReceived, provisionDetails);
    }

    public async Task<IReadOnlyList<ConsultationSummary>> GetOpenConsultationsAsync(
        int institutionId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var consultations = await _db.ConsultationRounds
            .Where(c => (c.Status == ConsultationStatus.Published || c.Status == ConsultationStatus.Open)
                && c.DeadlineDate >= today)
            .OrderBy(c => c.DeadlineDate)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.DeadlineDate,
                c.Status,
                HasSubmitted = _db.ConsultationFeedback
                    .Any(f => f.ConsultationId == c.Id && f.InstitutionId == institutionId)
            })
            .ToListAsync(ct);

        return consultations.Select(c => new ConsultationSummary(
            c.Id, c.Title, c.DeadlineDate, c.Status, c.HasSubmitted)).ToList();
    }

    public async Task<long> SubmitFeedbackAsync(
        long consultationId, int institutionId, FeedbackPosition overallPosition,
        string? generalComments, IReadOnlyList<ProvisionFeedbackInput> provisionFeedback,
        int submittedByUserId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var isOpen = await _db.ConsultationRounds
            .AnyAsync(c => c.Id == consultationId
                && (c.Status == ConsultationStatus.Published || c.Status == ConsultationStatus.Open)
                && c.DeadlineDate >= today, ct);

        if (!isOpen)
            throw new InvalidOperationException("Consultation is not open for feedback.");

        // Get institution details
        var institution = await _db.Set<Institution>()
            .Where(i => i.Id == institutionId)
            .Select(i => new { i.InstitutionCode, EntityType = i.LicenseType ?? "" })
            .FirstAsync(ct);

        var feedback = new ConsultationFeedback
        {
            ConsultationId = consultationId,
            InstitutionId = institutionId,
            InstitutionCode = institution.InstitutionCode,
            EntityType = institution.EntityType,
            SubmittedByUserId = submittedByUserId,
            OverallPosition = overallPosition,
            GeneralComments = generalComments
        };

        _db.ConsultationFeedback.Add(feedback);
        await _db.SaveChangesAsync(ct); // This will throw on duplicate due to unique constraint

        foreach (var pf in provisionFeedback)
        {
            _db.ProvisionFeedback.Add(new ProvisionFeedbackEntry
            {
                FeedbackId = feedback.Id,
                ProvisionId = pf.ProvisionId,
                Position = pf.Position,
                Reasoning = pf.Reasoning,
                SuggestedAmendment = pf.SuggestedAmendment,
                ImpactAssessment = pf.ImpactAssessment
            });
        }

        // Update feedback count
        var consultation = await _db.ConsultationRounds.FindAsync(new object[] { consultationId }, ct);
        if (consultation is not null)
        {
            consultation.TotalFeedbackReceived++;
            consultation.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return feedback.Id;
    }
}
