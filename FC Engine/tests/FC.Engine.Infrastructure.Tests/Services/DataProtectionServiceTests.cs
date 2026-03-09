using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Events;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services.DataProtection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Tests.Services;

public class DataProtectionServiceTests
{
    [Fact]
    public async Task PipelineWatcher_WhenPipelineCompletes_TriggersScan_And_Raises_NewPiiAlert()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(PipelineWatcher_WhenPipelineCompletes_TriggersScan_And_Raises_NewPiiAlert));
        var sut = CreateService(db);

        var source = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "raw-corebank",
            SourceType = "postgres",
            Schema = Schema(("customers", new[] { ("customer_id", "int", new[] { "1" }) }))
        });

        var target = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "warehouse",
            SourceType = "sqlserver",
            Schema = Schema(("customers", new[] { ("customer_id", "int", new[] { "1" }) }))
        });

        await sut.RunAtRestScanAsync(tenantId);

        await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceId = target.SourceId,
            SourceName = "warehouse",
            SourceType = "sqlserver",
            Schema = Schema(("customers", new[]
            {
                ("customer_id", "int", new[] { "1" }),
                ("email_address", "nvarchar", new[] { "ada@example.com" })
            }))
        });

        var pipeline = await sut.UpsertPipelineAsync(tenantId, new DataPipelineDefinitionRequest
        {
            PipelineName = "raw-to-warehouse",
            SourceDataSourceId = source.SourceId,
            TargetDataSourceId = target.SourceId,
            IsApproved = true,
            SourceTlsEnabled = true,
            TargetTlsEnabled = true
        });

        await sut.HandlePipelineLifecycleEventAsync(new DataPipelineLifecycleEvent(
            tenantId,
            pipeline.PipelineId,
            Guid.NewGuid(),
            pipeline.PipelineName,
            "completed",
            source.SourceId,
            target.SourceId,
            true,
            true,
            true,
            ["customers"],
            ["customers"],
            500,
            null,
            DateTime.UtcNow,
            Guid.NewGuid()));

        var scans = await sut.GetScanHistoryAsync(tenantId, target.SourceId);
        scans.Should().Contain(x => x.Trigger == "pipeline_completed" && x.NewPiiCount > 0);
        scans.SelectMany(x => x.Columns)
            .Should().Contain(x => x.ColumnName == "email_address" && x.DetectedPiiTypes.Contains(PiiCatalog.Email));

        var alerts = await sut.GetSecurityAlertsAsync(tenantId);
        alerts.Should().Contain(x => x.AlertType == "dspm_new_pii");
    }

    [Fact]
    public async Task TransitWatcher_WhenPipelineRunsWithoutTls_RaisesAlert()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(TransitWatcher_WhenPipelineRunsWithoutTls_RaisesAlert));
        var sut = CreateService(db);

        var source = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "source",
            SourceType = "mysql",
            Schema = Schema(("payments", new[] { ("amount", "decimal", new[] { "100" }) }))
        });
        var target = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "target",
            SourceType = "sqlserver",
            Schema = Schema(("payments", new[] { ("amount", "decimal", new[] { "100" }) }))
        });

        var pipeline = await sut.UpsertPipelineAsync(tenantId, new DataPipelineDefinitionRequest
        {
            PipelineName = "payments-sync",
            SourceDataSourceId = source.SourceId,
            TargetDataSourceId = target.SourceId,
            IsApproved = true,
            SourceTlsEnabled = false,
            TargetTlsEnabled = true
        });

        await sut.HandlePipelineLifecycleEventAsync(new DataPipelineLifecycleEvent(
            tenantId,
            pipeline.PipelineId,
            Guid.NewGuid(),
            pipeline.PipelineName,
            "running",
            source.SourceId,
            target.SourceId,
            false,
            true,
            true,
            ["payments"],
            ["payments"],
            25,
            null,
            DateTime.UtcNow,
            Guid.NewGuid()));

        var alerts = await sut.GetSecurityAlertsAsync(tenantId);
        alerts.Should().ContainSingle(x => x.AlertType == "transit_encryption" && x.Severity == "high");
    }

    [Fact]
    public async Task AtRestWatcher_Detects_Restricted_Drift()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(AtRestWatcher_Detects_Restricted_Drift));
        var sut = CreateService(db);

        var source = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "partner-drop",
            SourceType = "s3",
            Schema = Schema(("profiles", new[] { ("identifier", "nvarchar", new[] { "abc123" }) }))
        });

        await sut.RunAtRestScanAsync(tenantId);

        await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceId = source.SourceId,
            SourceName = "partner-drop",
            SourceType = "s3",
            Schema = Schema(("profiles", new[] { ("identifier", "nvarchar", new[] { "123-45-6789" }) }))
        });

        await sut.RunAtRestScanAsync(tenantId);

        var scans = await sut.GetScanHistoryAsync(tenantId, source.SourceId);
        scans.First().DriftCount.Should().BeGreaterThan(0);

        var alerts = await sut.GetSecurityAlertsAsync(tenantId);
        alerts.Should().Contain(x => x.AlertType == "dspm_classification_drift");
    }

    [Fact]
    public async Task ShadowDetector_Flags_Untracked_Fingerprint_Match()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ShadowDetector_Flags_Untracked_Fingerprint_Match));
        var sut = CreateService(db);

        await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "source-a",
            SourceType = "postgres",
            Schema = Schema(("accounts", new[]
            {
                ("account_no", "nvarchar", new[] { "001" }),
                ("balance", "decimal", new[] { "10" })
            }))
        });
        await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "source-b",
            SourceType = "postgres",
            Schema = Schema(("accounts", new[]
            {
                ("account_no", "nvarchar", new[] { "001" }),
                ("balance", "decimal", new[] { "10" })
            }))
        });

        await sut.RunShadowCopyDetectionAsync(tenantId);

        var shadowCopies = await sut.GetShadowCopiesAsync(tenantId);
        shadowCopies.Should().Contain(x => x.DetectionType == "fingerprint_match" && !x.IsLegitimate);

        var alerts = await sut.GetSecurityAlertsAsync(tenantId);
        alerts.Should().Contain(x => x.AlertType == "shadow_copy");
    }

    [Fact]
    public async Task ShadowDetector_DoesNot_Alert_When_Lineage_Exists()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(ShadowDetector_DoesNot_Alert_When_Lineage_Exists));
        var sut = CreateService(db);

        var sourceA = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "source-a",
            SourceType = "postgres",
            Schema = Schema(("accounts", new[]
            {
                ("account_no", "nvarchar", new[] { "001" }),
                ("balance", "decimal", new[] { "10" })
            }))
        });
        var sourceB = await sut.UpsertDataSourceAsync(tenantId, new DataSourceRegistrationRequest
        {
            SourceName = "source-b",
            SourceType = "postgres",
            Schema = Schema(("accounts", new[]
            {
                ("account_no", "nvarchar", new[] { "001" }),
                ("balance", "decimal", new[] { "10" })
            }))
        });

        await sut.UpsertPipelineAsync(tenantId, new DataPipelineDefinitionRequest
        {
            PipelineName = "tracked-copy",
            SourceDataSourceId = sourceA.SourceId,
            TargetDataSourceId = sourceB.SourceId,
            IsApproved = true,
            SourceTlsEnabled = true,
            TargetTlsEnabled = true
        });

        await sut.RunShadowCopyDetectionAsync(tenantId);

        var shadowCopies = await sut.GetShadowCopiesAsync(tenantId);
        shadowCopies.Should().Contain(x => x.IsLegitimate);

        var alerts = await sut.GetSecurityAlertsAsync(tenantId, "shadow_copy");
        alerts.Should().BeEmpty();
    }

    private static DataProtectionService CreateService(MetadataDbContext db)
    {
        var fingerprint = new SchemaFingerprintService();
        return new DataProtectionService(
            db,
            new PiiClassifier(),
            new ComplianceTagger(),
            new ShadowCopyDetector(fingerprint),
            Options.Create(new ContinuousDspmOptions()),
            NullLogger<DataProtectionService>.Instance);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }

    private static DataSourceSchema Schema(params (string Table, (string Column, string DataType, string[] Samples)[] Columns)[] tables)
    {
        return new DataSourceSchema
        {
            Tables = tables.Select(t => new DataTableSchema
            {
                TableName = t.Table,
                Columns = t.Columns.Select(c => new DataColumnSchema
                {
                    ColumnName = c.Column,
                    DataType = c.DataType,
                    SampleValues = c.Samples.ToList()
                }).ToList()
            }).ToList()
        };
    }
}
