using System.Security.Cryptography;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ExaminationWorkspaceServiceTests
{
    [Fact]
    public async Task CreateProject_CarriesForward_Open_Findings_From_Previous_Examination()
    {
        var regulatorTenantId = Guid.NewGuid();

        await using var db = CreateDb(nameof(CreateProject_CarriesForward_Open_Findings_From_Previous_Examination));
        var priorProject = new ExaminationProject
        {
            Id = 11,
            TenantId = regulatorTenantId,
            Name = "2025 On-Site",
            Scope = "Previous exam",
            EntityIdsJson = "[10]",
            ModuleCodesJson = "[\"CAR\"]",
            TeamAssignmentsJson = "[]",
            TimelineJson = "[]",
            Status = ExaminationProjectStatus.Completed,
            CreatedBy = 99,
            CreatedAt = DateTime.UtcNow.AddMonths(-6),
            UpdatedAt = DateTime.UtcNow.AddMonths(-6)
        };

        db.ExaminationProjects.Add(priorProject);
        db.ExaminationFindings.AddRange(
            new ExaminationFinding
            {
                Id = 100,
                TenantId = regulatorTenantId,
                ProjectId = priorProject.Id,
                InstitutionId = 10,
                Title = "Capital adequacy deterioration",
                RiskArea = "Capital",
                Observation = "CAR weakened.",
                Recommendation = "Submit remediation plan.",
                RiskRating = ExaminationRiskRating.High,
                Status = ExaminationWorkflowStatus.FindingDocumented,
                RemediationStatus = ExaminationRemediationStatus.Open,
                CreatedBy = 99,
                CreatedAt = DateTime.UtcNow.AddMonths(-6),
                UpdatedAt = DateTime.UtcNow.AddDays(-10)
            },
            new ExaminationFinding
            {
                Id = 101,
                TenantId = regulatorTenantId,
                ProjectId = priorProject.Id,
                InstitutionId = 10,
                Title = "Closed item",
                RiskArea = "Liquidity",
                Observation = "Already closed.",
                Recommendation = "None.",
                RiskRating = ExaminationRiskRating.Low,
                Status = ExaminationWorkflowStatus.Closed,
                RemediationStatus = ExaminationRemediationStatus.Closed,
                CreatedBy = 99,
                CreatedAt = DateTime.UtcNow.AddMonths(-6),
                UpdatedAt = DateTime.UtcNow.AddMonths(-5)
            });
        await db.SaveChangesAsync();

        var sut = CreateService(db);

        var created = await sut.CreateProject(
            regulatorTenantId,
            7,
            new ExaminationProjectCreateRequest
            {
                Name = "2026 On-Site",
                Scope = "Current exam",
                InstitutionIds = new List<int> { 10 },
                ModuleCodes = new List<string> { "CAR" }
            });

        var carried = await db.ExaminationFindings
            .Where(x => x.ProjectId == created.Id)
            .ToListAsync();

        carried.Should().ContainSingle();
        carried[0].IsCarriedForward.Should().BeTrue();
        carried[0].CarriedForwardFromFindingId.Should().Be(100);
        carried[0].Status.Should().Be(ExaminationWorkflowStatus.ToReview);
    }

    [Fact]
    public async Task UploadEvidence_Hashes_File_Fulfills_Request_And_Updates_Finding()
    {
        var regulatorTenantId = Guid.NewGuid();
        var storage = new InMemoryStorage();

        await using var db = CreateDb(nameof(UploadEvidence_Hashes_File_Fulfills_Request_And_Updates_Finding));
        var project = CreateProject(regulatorTenantId, 21, "[10]");
        var finding = new ExaminationFinding
        {
            Id = 201,
            TenantId = regulatorTenantId,
            ProjectId = project.Id,
            InstitutionId = 10,
            Title = "AML control weakness",
            RiskArea = "AML",
            Observation = "Weak KYC evidence.",
            Recommendation = "Upload remediation evidence.",
            RiskRating = ExaminationRiskRating.Medium,
            Status = ExaminationWorkflowStatus.FindingDocumented,
            RemediationStatus = ExaminationRemediationStatus.Open,
            ManagementResponseDeadline = DateTime.UtcNow.AddDays(14),
            CreatedBy = 9,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var request = new ExaminationEvidenceRequest
        {
            Id = 301,
            TenantId = regulatorTenantId,
            ProjectId = project.Id,
            FindingId = finding.Id,
            InstitutionId = 10,
            Title = "Provide remediation pack",
            RequestText = "Upload the updated KYC policy.",
            RequestedBy = 9,
            RequestedAt = DateTime.UtcNow,
            Status = ExaminationEvidenceRequestStatus.Open
        };

        db.ExaminationProjects.Add(project);
        db.ExaminationFindings.Add(finding);
        db.ExaminationEvidenceRequests.Add(request);
        await db.SaveChangesAsync();

        var sut = CreateService(db, storage: storage);
        var payload = new MemoryStream("remediation-doc"u8.ToArray());

        var uploaded = await sut.UploadEvidence(
            regulatorTenantId,
            project.Id,
            finding.Id,
            request.Id,
            null,
            10,
            "kyc-policy.pdf",
            "application/pdf",
            payload.Length,
            payload,
            ExaminationEvidenceKind.RemediationEvidence,
            ExaminationEvidenceUploaderRole.Institution,
            "Updated policy",
            55);

        var expectedHash = Convert.ToHexStringLower(SHA256.HashData("remediation-doc"u8.ToArray()));
        uploaded.FileHash.Should().Be(expectedHash);
        storage.Files.Should().ContainKey(uploaded.StoragePath);

        var refreshedRequest = await db.ExaminationEvidenceRequests.SingleAsync(x => x.Id == request.Id);
        refreshedRequest.Status.Should().Be(ExaminationEvidenceRequestStatus.Fulfilled);

        var refreshedFinding = await db.ExaminationFindings.SingleAsync(x => x.Id == finding.Id);
        refreshedFinding.RemediationStatus.Should().Be(ExaminationRemediationStatus.PendingVerification);
        refreshedFinding.EvidenceReference.Should().Contain("kyc-policy.pdf");

        var downloaded = await sut.DownloadEvidence(regulatorTenantId, project.Id, uploaded.Id);
        downloaded.Should().NotBeNull();
        downloaded!.Content.Should().Equal("remediation-doc"u8.ToArray());
    }

    [Fact]
    public async Task GetIntelligencePack_Returns_Quarterly_Trends_Warnings_And_Prior_Findings()
    {
        var regulatorTenantId = Guid.NewGuid();
        var institutionTenantId = Guid.NewGuid();
        const int institutionId = 10;

        await using var db = CreateDb(nameof(GetIntelligencePack_Returns_Quarterly_Trends_Warnings_And_Prior_Findings));

        db.Institutions.Add(new Institution
        {
            Id = institutionId,
            TenantId = institutionTenantId,
            InstitutionCode = "BANK10",
            InstitutionName = "Sample Bank",
            LicenseType = "DMB",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddYears(-4)
        });

        db.Modules.Add(new Module
        {
            Id = 1,
            ModuleCode = "CAR",
            ModuleName = "Capital Adequacy",
            RegulatorCode = "CBN",
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        });

        db.ReturnPeriods.Add(new ReturnPeriod
        {
            Id = 1,
            TenantId = institutionTenantId,
            ModuleId = 1,
            Year = 2026,
            Quarter = 1,
            Month = 3,
            Frequency = "Quarterly",
            ReportingDate = new DateTime(2026, 3, 31),
            DeadlineDate = new DateTime(2026, 5, 15),
            IsOpen = false,
            CreatedAt = DateTime.UtcNow.AddMonths(-2)
        });

        db.Submissions.Add(new Submission
        {
            Id = 1,
            TenantId = institutionTenantId,
            InstitutionId = institutionId,
            ReturnPeriodId = 1,
            ReturnCode = "CAR",
            Status = SubmissionStatus.Accepted,
            SubmittedAt = new DateTime(2026, 4, 10),
            ParsedDataJson = "{\"car\":14.3,\"npl\":4.1}",
            CreatedAt = new DateTime(2026, 4, 10)
        });

        db.ChsScoreSnapshots.AddRange(
            Snapshot(institutionTenantId, new DateTime(2025, 3, 31), 71m),
            Snapshot(institutionTenantId, new DateTime(2025, 6, 30), 68m),
            Snapshot(institutionTenantId, new DateTime(2025, 9, 30), 66m),
            Snapshot(institutionTenantId, new DateTime(2025, 12, 31), 64m));

        db.ExaminationProjects.AddRange(
            CreateProject(regulatorTenantId, 40, "[10]"),
            new ExaminationProject
            {
                Id = 41,
                TenantId = regulatorTenantId,
                Name = "2025 Exam",
                Scope = "Previous",
                EntityIdsJson = "[10]",
                ModuleCodesJson = "[\"CAR\"]",
                TeamAssignmentsJson = "[]",
                TimelineJson = "[]",
                Status = ExaminationProjectStatus.Completed,
                CreatedBy = 5,
                CreatedAt = DateTime.UtcNow.AddMonths(-9),
                UpdatedAt = DateTime.UtcNow.AddMonths(-3)
            });

        db.ExaminationFindings.Add(new ExaminationFinding
        {
            Id = 401,
            TenantId = regulatorTenantId,
            ProjectId = 41,
            InstitutionId = institutionId,
            Title = "Outstanding governance weakness",
            RiskArea = "Governance",
            Observation = "Board oversight still weak.",
            Recommendation = "Submit governance remediation.",
            RiskRating = ExaminationRiskRating.High,
            Status = ExaminationWorkflowStatus.ManagementResponseRequired,
            RemediationStatus = ExaminationRemediationStatus.InRemediation,
            CreatedBy = 5,
            CreatedAt = DateTime.UtcNow.AddMonths(-8),
            UpdatedAt = DateTime.UtcNow.AddDays(-7)
        });

        await db.SaveChangesAsync();

        var warnings = new List<EarlyWarningFlag>
        {
            new()
            {
                InstitutionId = institutionId,
                InstitutionName = "Sample Bank",
                Severity = EarlyWarningSeverity.Red,
                FlagCode = "CAPITAL_BELOW_MINIMUM",
                Message = "Capital is trending down."
            }
        };

        var benchmark = new EntityBenchmarkResult
        {
            InstitutionId = institutionId,
            InstitutionName = "Sample Bank",
            CarValue = 14.3m,
            CarPeerAverage = 15.6m,
            NplValue = 4.1m,
            NplPeerAverage = 3.6m,
            DataQualityScore = 92m,
            DataQualityPeerAverage = 88m
        };

        var sut = CreateService(
            db,
            warnings: warnings,
            benchmarkFactory: id => id == institutionId ? benchmark : null);

        var pack = await sut.GetIntelligencePack(regulatorTenantId, "CBN", 40);

        pack.Should().NotBeNull();
        pack!.Institutions.Should().ContainSingle();
        pack.Institutions[0].ChsTrend.Should().HaveCount(4);
        pack.Institutions[0].ActiveWarnings.Should().ContainSingle();
        pack.Institutions[0].OutstandingPreviousFindings.Should().ContainSingle(x => x.Id == 401);
        pack.KeyRiskAreas.Should().Contain(x => x.Contains("Governance", StringComparison.OrdinalIgnoreCase)
                                                || x.Contains("CAPITAL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetWorkspace_AutoEscalates_Overdue_Remediation_Items()
    {
        var regulatorTenantId = Guid.NewGuid();

        await using var db = CreateDb(nameof(GetWorkspace_AutoEscalates_Overdue_Remediation_Items));
        db.Institutions.Add(new Institution
        {
            Id = 10,
            TenantId = Guid.NewGuid(),
            InstitutionCode = "BANK10",
            InstitutionName = "Sample Bank",
            LicenseType = "DMB",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddYears(-2)
        });

        var project = CreateProject(regulatorTenantId, 51, "[10]");
        db.ExaminationProjects.Add(project);
        db.ExaminationFindings.Add(new ExaminationFinding
        {
            Id = 501,
            TenantId = regulatorTenantId,
            ProjectId = project.Id,
            InstitutionId = 10,
            Title = "Overdue remediation",
            RiskArea = "Liquidity",
            Observation = "Response overdue.",
            Recommendation = "Respond immediately.",
            RiskRating = ExaminationRiskRating.High,
            Status = ExaminationWorkflowStatus.FindingDocumented,
            RemediationStatus = ExaminationRemediationStatus.AwaitingManagementResponse,
            ManagementResponseDeadline = DateTime.UtcNow.AddDays(-3),
            CreatedBy = 5,
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-4)
        });
        await db.SaveChangesAsync();

        var sut = CreateService(db);
        var workspace = await sut.GetWorkspace(regulatorTenantId, "CBN", project.Id);

        workspace.Should().NotBeNull();
        var finding = await db.ExaminationFindings.SingleAsync(x => x.Id == 501);
        finding.Status.Should().Be(ExaminationWorkflowStatus.ManagementResponseRequired);
        finding.RemediationStatus.Should().Be(ExaminationRemediationStatus.Escalated);
        finding.EscalatedAt.Should().NotBeNull();
    }

    private static ExaminationWorkspaceService CreateService(
        MetadataDbContext db,
        InMemoryStorage? storage = null,
        IReadOnlyList<EarlyWarningFlag>? warnings = null,
        Func<int, EntityBenchmarkResult?>? benchmarkFactory = null)
    {
        var benchmarking = new Mock<IEntityBenchmarkingService>();
        benchmarking
            .Setup(x => x.GetEntityBenchmark(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int institutionId, string? _, CancellationToken _) => benchmarkFactory?.Invoke(institutionId));

        var branding = new Mock<ITenantBrandingService>();
        branding
            .Setup(x => x.GetBrandingConfig(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrandingConfig());

        var earlyWarning = new Mock<IEarlyWarningService>();
        earlyWarning
            .Setup(x => x.ComputeFlags(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(warnings?.ToList() ?? new List<EarlyWarningFlag>());

        var audit = new Mock<IAuditLogger>();
        storage ??= new InMemoryStorage();

        return new ExaminationWorkspaceService(
            db,
            benchmarking.Object,
            branding.Object,
            earlyWarning.Object,
            storage,
            audit.Object);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }

    private static ExaminationProject CreateProject(Guid tenantId, int id, string entityIdsJson)
        => new()
        {
            Id = id,
            TenantId = tenantId,
            Name = $"Project {id}",
            Scope = "Test scope",
            EntityIdsJson = entityIdsJson,
            ModuleCodesJson = "[\"CAR\"]",
            TeamAssignmentsJson = "[]",
            TimelineJson = "[]",
            Status = ExaminationProjectStatus.InProgress,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };

    private static ChsScoreSnapshot Snapshot(Guid tenantId, DateTime computedAt, decimal overallScore)
        => new()
        {
            TenantId = tenantId,
            PeriodLabel = $"{computedAt.Year}-Q{((computedAt.Month - 1) / 3) + 1}",
            ComputedAt = computedAt,
            OverallScore = overallScore,
            Rating = 2,
            FilingTimeliness = overallScore,
            DataQuality = overallScore,
            RegulatoryCapital = overallScore,
            AuditGovernance = overallScore,
            Engagement = overallScore
        };

    private sealed class InMemoryStorage : IFileStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
            => Save(path, content, overwrite: true, ct);

        public Task<string> UploadImmutableAsync(string path, Stream content, string contentType, CancellationToken ct = default)
            => Save(path, content, overwrite: false, ct);

        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
        {
            if (!Files.TryGetValue(path, out var bytes))
            {
                throw new FileNotFoundException(path);
            }

            Stream stream = new MemoryStream(bytes, writable: false);
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            Files.Remove(path);
            return Task.CompletedTask;
        }

        public string GetPublicUrl(string path) => path;

        private async Task<string> Save(string path, Stream content, bool overwrite, CancellationToken ct)
        {
            if (!overwrite && Files.ContainsKey(path))
            {
                throw new InvalidOperationException("Immutable path already exists.");
            }

            await using var memory = new MemoryStream();
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            await content.CopyToAsync(memory, ct);
            Files[path] = memory.ToArray();
            return path;
        }
    }
}
