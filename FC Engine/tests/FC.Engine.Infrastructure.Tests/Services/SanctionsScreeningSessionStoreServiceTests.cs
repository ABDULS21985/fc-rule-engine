using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Tests.Services;

public class SanctionsScreeningSessionStoreServiceTests
{
    [Fact]
    public async Task RecordAndLoadLatestAsync_RoundTrips_Batch_Run_And_Transaction_Check()
    {
        await using var db = CreateDb();
        var sut = new SanctionsScreeningSessionStoreService(db);

        var run = new SanctionsStoredScreeningRun
        {
            ThresholdPercent = 86d,
            ScreenedAt = new DateTime(2026, 3, 11, 10, 30, 0, DateTimeKind.Utc),
            TotalSubjects = 2,
            MatchCount = 1,
            Results =
            [
                new SanctionsStoredScreeningResult
                {
                    Subject = "Sample Bureau De Change Limited FULL",
                    Disposition = "Clear",
                    MatchScore = 0,
                    MatchedName = "No material match",
                    SourceCode = "N/A",
                    SourceName = "N/A",
                    Category = "clear",
                    RiskLevel = "low"
                },
                new SanctionsStoredScreeningResult
                {
                    Subject = "Boko Haram",
                    Disposition = "True Match",
                    MatchScore = 100,
                    MatchedName = "BOKO HARAM",
                    SourceCode = "OFAC",
                    SourceName = "OFAC SDN List",
                    Category = "entity",
                    RiskLevel = "critical"
                }
            ]
        };

        var transaction = new SanctionsStoredTransactionCheck
        {
            TransactionReference = "TXN-001",
            Amount = 12500000m,
            Currency = "NGN",
            Channel = "Wire",
            ThresholdPercent = 82d,
            HighRisk = true,
            ControlDecision = "Block",
            Narrative = "Material sanctions hit found.",
            RequiresStrDraft = true,
            PartyResults =
            [
                new SanctionsStoredTransactionPartyResult
                {
                    PartyRole = "Beneficiary",
                    PartyName = "Boko Haram",
                    Disposition = "True Match",
                    MatchScore = 100d,
                    MatchedName = "BOKO HARAM",
                    SourceCode = "OFAC",
                    RiskLevel = "critical"
                }
            ]
        };

        await sut.RecordBatchRunAsync(run);
        await sut.RecordTransactionCheckAsync(transaction);

        var loaded = await sut.LoadLatestAsync();

        loaded.LatestRun.Should().NotBeNull();
        loaded.LatestRun!.Results.Should().HaveCount(2);
        loaded.LatestRun.MatchCount.Should().Be(1);
        loaded.LatestRun.Results.Count(x => x.Disposition == "True Match").Should().Be(1);

        loaded.LatestTransaction.Should().NotBeNull();
        loaded.LatestTransaction!.TransactionReference.Should().Be("TXN-001");
        loaded.LatestTransaction.PartyResults.Should().ContainSingle(x => x.PartyRole == "Beneficiary");

        (await db.SanctionsScreeningRuns.CountAsync()).Should().Be(1);
        (await db.SanctionsScreeningResults.CountAsync()).Should().Be(2);
        (await db.SanctionsTransactionChecks.CountAsync()).Should().Be(1);
        (await db.SanctionsTransactionPartyResults.CountAsync()).Should().Be(1);
    }

    private static MetadataDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MetadataDbContext(options);
    }
}
