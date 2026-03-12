using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SanctionsStrDraftCatalogServiceTests
{
    [Fact]
    public async Task MaterializeAsync_Persists_Str_Drafts_And_Loads_Them_Back()
    {
        await using var db = CreateDb();
        var sut = new SanctionsStrDraftCatalogService(db);

        var input = new List<SanctionsStrDraftInput>
        {
            new()
            {
                DraftId = "STR-202603120915-01",
                Subject = "Boko Haram",
                MatchedName = "Boko Haram",
                SourceCode = "UN",
                SourceName = "UN Security Council",
                RiskLevel = "critical",
                Decision = "Confirm Match",
                Status = "Ready",
                Priority = "Critical",
                ScorePercent = 97.4m,
                FreezeRecommended = true,
                ScreenedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                ReviewDueAtUtc = DateTime.UtcNow.AddHours(24),
                SuspicionBasis = "Exact watchlist hit at critical risk level.",
                GoAmlPayloadSummary = "Prepare urgent STR review with freeze control package.",
                Narrative = "Automated screening found a critical sanctions hit that requires immediate compliance escalation.",
                RecommendedActions =
                [
                    "Confirm freeze decision.",
                    "Attach screening evidence.",
                    "Prepare goAML package."
                ]
            }
        };

        var materialized = await sut.MaterializeAsync(input);
        var loaded = await sut.LoadAsync();

        materialized.MaterializedAt.Should().NotBeNull();
        materialized.Drafts.Should().ContainSingle(x =>
            x.DraftId == "STR-202603120915-01"
            && x.Priority == "Critical"
            && x.FreezeRecommended);

        loaded.Drafts.Should().ContainSingle(x =>
            x.Subject == "Boko Haram"
            && x.RecommendedActions.Count == 3
            && x.GoAmlPayloadSummary.Contains("freeze", StringComparison.OrdinalIgnoreCase));

        (await db.SanctionsStrDrafts.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
