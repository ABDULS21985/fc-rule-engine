using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services.DataProtection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class RootCauseAnalysisServiceTests
{
    [Fact]
    public async Task SecurityAlert_Rca_Builds_Five_Step_Causal_Chain()
    {
        await using var db = CreateDb(nameof(SecurityAlert_Rca_Builds_Five_Step_Causal_Chain));
        var tenantId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var alertId = SeedSecurityScenario(db, tenantId, assetId, 3);
        var sut = CreateService(db);

        var analysis = await sut.AnalyzeAsync(tenantId, RcaIncidentType.SecurityAlert, alertId);

        analysis.CausalChain.Should().HaveCount(5);
    }

    [Fact]
    public async Task SecurityAlert_Rca_Walks_Mitre_To_Earliest_Phase()
    {
        await using var db = CreateDb(nameof(SecurityAlert_Rca_Walks_Mitre_To_Earliest_Phase));
        var tenantId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var alertId = SeedSecurityScenario(db, tenantId, assetId, 3);
        var sut = CreateService(db);

        var analysis = await sut.AnalyzeAsync(tenantId, RcaIncidentType.SecurityAlert, alertId);

        analysis.RootCauseType.Should().Be("exposed_service");
        analysis.RootCauseSummary.Should().Contain("Initial compromise");
    }

    [Fact]
    public async Task PipelineFailure_Rca_Points_To_Upstream_Failure()
    {
        await using var db = CreateDb(nameof(PipelineFailure_Rca_Points_To_Upstream_Failure));
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        SeedSource(db, tenantId, sourceId, "raw");
        SeedSource(db, tenantId, targetId, "warehouse");

        var upstreamId = Guid.NewGuid();
        var downstreamId = Guid.NewGuid();
        db.DataPipelineDefinitions.Add(new DataPipelineDefinition
        {
            Id = upstreamId,
            TenantId = tenantId,
            PipelineName = "upstream-pipeline",
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            UpstreamPipelineIdsJson = "[]"
        });
        db.DataPipelineDefinitions.Add(new DataPipelineDefinition
        {
            Id = downstreamId,
            TenantId = tenantId,
            PipelineName = "downstream-pipeline",
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            UpstreamPipelineIdsJson = JsonSerializer.Serialize(new[] { upstreamId })
        });

        db.DataPipelineExecutions.Add(new DataPipelineExecution
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PipelineId = upstreamId,
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            Status = "failed",
            ErrorMessage = "Source connection timeout",
            StartedAt = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 3, 1, 8, 5, 0, DateTimeKind.Utc)
        });

        var incidentId = Guid.NewGuid();
        db.DataPipelineExecutions.Add(new DataPipelineExecution
        {
            Id = incidentId,
            TenantId = tenantId,
            PipelineId = downstreamId,
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            Status = "failed",
            ErrorMessage = "Downstream pipeline could not load upstream output",
            StartedAt = new DateTime(2026, 3, 1, 8, 10, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 3, 1, 8, 15, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
        var sut = CreateService(db);

        var analysis = await sut.AnalyzeAsync(tenantId, RcaIncidentType.PipelineFailure, incidentId);

        analysis.RootCauseType.Should().Be("upstream_failure");
        analysis.RootCauseSummary.Should().Contain("upstream-pipeline");
    }

    [Fact]
    public async Task PipelineFailure_Rca_Detects_Schema_Drift()
    {
        await using var db = CreateDb(nameof(PipelineFailure_Rca_Detects_Schema_Drift));
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        SeedSource(db, tenantId, sourceId, "source-system");
        SeedSource(db, tenantId, targetId, "target-system");

        var pipelineId = Guid.NewGuid();
        db.DataPipelineDefinitions.Add(new DataPipelineDefinition
        {
            Id = pipelineId,
            TenantId = tenantId,
            PipelineName = "schema-sensitive-pipeline",
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            UpstreamPipelineIdsJson = "[]"
        });

        var earlierScan = new DspmScanRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDataSourceId = sourceId,
            Trigger = "at_rest",
            Status = "completed",
            StartedAt = new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 3, 1, 6, 1, 0, DateTimeKind.Utc)
        };
        var laterScan = new DspmScanRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDataSourceId = sourceId,
            Trigger = "at_rest",
            Status = "completed",
            StartedAt = new DateTime(2026, 3, 1, 7, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 3, 1, 7, 1, 0, DateTimeKind.Utc)
        };
        db.DspmScanRecords.AddRange(earlierScan, laterScan);
        db.DspmColumnFindings.Add(new DspmColumnFinding
        {
            Id = Guid.NewGuid(),
            ScanId = earlierScan.Id,
            TableName = "customers",
            ColumnName = "customer_id",
            DataType = "int",
            DetectedPiiTypesJson = "[]",
            ComplianceTagsJson = "[]"
        });
        db.DspmColumnFindings.Add(new DspmColumnFinding
        {
            Id = Guid.NewGuid(),
            ScanId = laterScan.Id,
            TableName = "customers",
            ColumnName = "customer_id",
            DataType = "nvarchar",
            DetectedPiiTypesJson = "[]",
            ComplianceTagsJson = "[]"
        });

        var incidentId = Guid.NewGuid();
        db.DataPipelineExecutions.Add(new DataPipelineExecution
        {
            Id = incidentId,
            TenantId = tenantId,
            PipelineId = pipelineId,
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            Status = "failed",
            ErrorMessage = "Schema mismatch on column customer_id",
            StartedAt = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 3, 1, 8, 5, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
        var sut = CreateService(db);

        var analysis = await sut.AnalyzeAsync(tenantId, RcaIncidentType.PipelineFailure, incidentId);

        analysis.RootCauseType.Should().Be("schema_drift");
        analysis.RootCauseSummary.Should().Contain("changed from int to nvarchar");
    }

    [Fact]
    public async Task PipelineFailure_Rca_Detects_Connectivity_Issue()
    {
        await using var db = CreateDb(nameof(PipelineFailure_Rca_Detects_Connectivity_Issue));
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        SeedSource(db, tenantId, sourceId, "payments-api");
        SeedSource(db, tenantId, targetId, "warehouse");

        var pipelineId = Guid.NewGuid();
        var incidentId = Guid.NewGuid();
        db.DataPipelineDefinitions.Add(new DataPipelineDefinition
        {
            Id = pipelineId,
            TenantId = tenantId,
            PipelineName = "payments-pipeline",
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            UpstreamPipelineIdsJson = "[]"
        });
        db.DataPipelineExecutions.Add(new DataPipelineExecution
        {
            Id = incidentId,
            TenantId = tenantId,
            PipelineId = pipelineId,
            SourceDataSourceId = sourceId,
            TargetDataSourceId = targetId,
            Status = "failed",
            ErrorMessage = "Connection timeout while contacting source endpoint",
            StartedAt = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
        var sut = CreateService(db);

        var analysis = await sut.AnalyzeAsync(tenantId, RcaIncidentType.PipelineFailure, incidentId);

        analysis.RootCauseType.Should().Be("connectivity");
        analysis.RootCauseSummary.Should().Contain("payments-api");
    }

    [Fact]
    public async Task SecurityAlert_Rca_Assesses_Impact_Using_Dependencies()
    {
        await using var db = CreateDb(nameof(SecurityAlert_Rca_Assesses_Impact_Using_Dependencies));
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        SeedSource(db, tenantId, sourceId, "warehouse");

        var asset1 = new CyberAsset { Id = Guid.NewGuid(), TenantId = tenantId, AssetKey = "api-1", DisplayName = "API 1", AssetType = "service", Criticality = "critical", LinkedDataSourceId = sourceId, DataClassificationsJson = JsonSerializer.Serialize(new[] { "email" }) };
        var asset2 = new CyberAsset { Id = Guid.NewGuid(), TenantId = tenantId, AssetKey = "api-2", DisplayName = "API 2", AssetType = "service", Criticality = "high", LinkedDataSourceId = sourceId, DataClassificationsJson = JsonSerializer.Serialize(new[] { "name" }) };
        var dep1 = new CyberAsset { Id = Guid.NewGuid(), TenantId = tenantId, AssetKey = "db-1", DisplayName = "DB 1", AssetType = "database", Criticality = "high", DataClassificationsJson = "[]" };
        var dep2 = new CyberAsset { Id = Guid.NewGuid(), TenantId = tenantId, AssetKey = "queue-1", DisplayName = "Queue 1", AssetType = "queue", Criticality = "medium", DataClassificationsJson = "[]" };
        var dep3 = new CyberAsset { Id = Guid.NewGuid(), TenantId = tenantId, AssetKey = "worker-1", DisplayName = "Worker 1", AssetType = "worker", Criticality = "medium", DataClassificationsJson = "[]" };
        db.CyberAssets.AddRange(asset1, asset2, dep1, dep2, dep3);
        db.CyberAssetDependencies.AddRange(
            new CyberAssetDependency { Id = Guid.NewGuid(), TenantId = tenantId, AssetId = dep1.Id, DependsOnAssetId = asset1.Id },
            new CyberAssetDependency { Id = Guid.NewGuid(), TenantId = tenantId, AssetId = dep2.Id, DependsOnAssetId = asset1.Id },
            new CyberAssetDependency { Id = Guid.NewGuid(), TenantId = tenantId, AssetId = dep3.Id, DependsOnAssetId = asset2.Id });

        var scan = new DspmScanRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceDataSourceId = sourceId,
            Trigger = "at_rest",
            Status = "completed",
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = DateTime.UtcNow.AddHours(-2)
        };
        db.DspmScanRecords.Add(scan);
        db.DspmColumnFindings.Add(new DspmColumnFinding
        {
            Id = Guid.NewGuid(),
            ScanId = scan.Id,
            TableName = "customers",
            ColumnName = "email",
            DataType = "nvarchar",
            PrimaryPiiType = "email",
            DetectedPiiTypesJson = JsonSerializer.Serialize(new[] { "email" }),
            ComplianceTagsJson = "[]"
        });

        var alertId = Guid.NewGuid();
        db.SecurityAlerts.Add(new SecurityAlert
        {
            Id = alertId,
            TenantId = tenantId,
            AlertType = "suspicious_activity",
            Severity = "high",
            Title = "Alert",
            Description = "Potential compromise",
            AffectedAssetIdsJson = JsonSerializer.Serialize(new[] { asset1.Id, asset2.Id }),
            SourceIp = "203.0.113.1",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        var sut = CreateService(db);

        var analysis = await sut.AnalyzeAsync(tenantId, RcaIncidentType.SecurityAlert, alertId);

        analysis.Impact.TotalAffectedAssets.Should().Be(5);
    }

    private static Guid SeedSecurityScenario(MetadataDbContext db, Guid tenantId, Guid assetId, int eventCount)
    {
        var now = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        db.CyberAssets.Add(new CyberAsset
        {
            Id = assetId,
            TenantId = tenantId,
            AssetKey = "asset-1",
            DisplayName = "Asset 1",
            AssetType = "service",
            Criticality = "high",
            DataClassificationsJson = "[]"
        });

        var alertId = Guid.NewGuid();
        db.SecurityAlerts.Add(new SecurityAlert
        {
            Id = alertId,
            TenantId = tenantId,
            AlertType = "security_alert",
            Severity = "high",
            Title = "Security Alert",
            Description = "Correlated attack activity",
            AffectedAssetIdsJson = JsonSerializer.Serialize(new[] { assetId }),
            UserId = "42",
            Username = "ada",
            SourceIp = "198.51.100.10",
            MitreTechnique = "T1041",
            CreatedAt = now
        });

        var techniques = new[] { "T1190", "T1059", "T1021", "T1041" };
        for (var i = 0; i < eventCount; i++)
        {
            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EventSource = "cyber",
                EventType = $"evt-{i}",
                AssetId = assetId,
                UserId = "42",
                Username = "ada",
                SourceIp = "198.51.100.10",
                MitreTechnique = techniques[i],
                Description = $"Event {i}",
                OccurredAt = now.AddMinutes(-(eventCount - i) * 10)
            });
        }

        db.LoginAttempts.Add(new LoginAttempt
        {
            Id = 1,
            TenantId = tenantId,
            UserId = 42,
            Username = "ada",
            IpAddress = "198.51.100.10",
            Succeeded = true,
            AttemptedAt = now.AddMinutes(-5)
        });

        db.SaveChanges();
        return alertId;
    }

    private static void SeedSource(MetadataDbContext db, Guid tenantId, Guid sourceId, string name)
    {
        db.DataSourceRegistrations.Add(new DataSourceRegistration
        {
            Id = sourceId,
            TenantId = tenantId,
            SourceName = name,
            SourceType = "sql",
            SchemaJson = "{\"tables\":[]}"
        });
    }

    private static RootCauseAnalysisService CreateService(MetadataDbContext db)
        => new(db, NullLogger<RootCauseAnalysisService>.Instance);

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}
