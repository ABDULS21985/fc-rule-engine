using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FC.Engine.Infrastructure.Tests.Services;

public class CaaSServiceTests
{
    private readonly Mock<ITemplateMetadataCache> _cacheMock = new();
    private readonly Mock<IFilingCalendarService> _filingMock = new();
    private readonly Mock<IEntitlementService> _entitlementMock = new();
    private readonly Mock<ISubmissionRepository> _submissionRepoMock = new();
    private readonly Mock<IDataFeedService> _dataFeedMock = new();
    private readonly Mock<ILogger<CaaSService>> _loggerMock = new();
    private readonly Mock<IFormulaEvaluator> _formulaEvalMock = new();
    private readonly Mock<ICrossSheetValidator> _crossSheetMock = new();
    private readonly Mock<IBusinessRuleEvaluator> _businessRuleMock = new();

    private CaaSService CreateService()
    {
        var orchestrator = new ValidationOrchestrator(
            _cacheMock.Object,
            _formulaEvalMock.Object,
            _crossSheetMock.Object,
            _businessRuleMock.Object);

        return new CaaSService(
            _cacheMock.Object,
            orchestrator,
            _filingMock.Object,
            _entitlementMock.Object,
            _submissionRepoMock.Object,
            _dataFeedMock.Object,
            _loggerMock.Object);
    }

    private CachedTemplate CreateTestTemplate(string returnCode = "CBN_300", string module = "PSP_FINTECH")
    {
        return new CachedTemplate
        {
            TemplateId = 1,
            ReturnCode = returnCode,
            Name = "Test Template",
            ModuleCode = module,
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = "FixedRow",
            CurrentVersion = new CachedTemplateVersion
            {
                Id = 1,
                VersionNumber = 1,
                Fields = new List<TemplateField>
                {
                    new()
                    {
                        FieldName = "TotalAssets",
                        DisplayName = "Total Assets",
                        DataType = FieldDataType.Money,
                        IsRequired = true
                    },
                    new()
                    {
                        FieldName = "TotalLiabilities",
                        DisplayName = "Total Liabilities",
                        DataType = FieldDataType.Money,
                        IsRequired = false
                    }
                },
                IntraSheetFormulas = Array.Empty<IntraSheetFormula>()
            }
        };
    }

    [Fact]
    public async Task ValidateAsync_WithValidData_ReturnsIsValid()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var template = CreateTestTemplate();
        _cacheMock.Setup(c => c.GetPublishedTemplate(tenantId, "CBN_300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _formulaEvalMock.Setup(f => f.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());
        _businessRuleMock.Setup(b => b.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<Submission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());

        var request = new CaaSValidateRequest
        {
            ModuleCode = "PSP_FINTECH",
            ReturnCode = "CBN_300",
            Records = new List<Dictionary<string, object?>>
            {
                new() { { "TotalAssets", 1000000m } }
            }
        };

        var service = CreateService();

        // Act
        var result = await service.ValidateAsync(tenantId, request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ErrorCount.Should().Be(0);
        result.ComplianceScorePreview.Should().Be(100);
    }

    [Fact]
    public async Task ValidateAsync_WithMissingRequiredField_ReturnsErrors()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var template = CreateTestTemplate();
        _cacheMock.Setup(c => c.GetPublishedTemplate(tenantId, "CBN_300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _formulaEvalMock.Setup(f => f.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());
        _businessRuleMock.Setup(b => b.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<Submission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());

        var request = new CaaSValidateRequest
        {
            ModuleCode = "PSP_FINTECH",
            ReturnCode = "CBN_300",
            Records = new List<Dictionary<string, object?>>
            {
                new() { { "TotalLiabilities", 500000m } }
                // Missing TotalAssets which is required
            }
        };

        var service = CreateService();

        // Act
        var result = await service.ValidateAsync(tenantId, request);

        // Assert — ValidateRelaxed downgrades all errors to warnings,
        // so IsValid is true but WarningCount should be > 0
        result.IsValid.Should().BeTrue();
        result.WarningCount.Should().BeGreaterThan(0);
        result.ComplianceScorePreview.Should().BeLessThan(100);
    }

    [Fact]
    public async Task GetTemplateStructureAsync_ReturnsFieldsAndFormulas()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var template = CreateTestTemplate();
        _cacheMock.Setup(c => c.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate> { template });

        var service = CreateService();

        // Act
        var result = await service.GetTemplateStructureAsync(tenantId, "PSP_FINTECH");

        // Assert
        result.Should().NotBeNull();
        result!.ModuleCode.Should().Be("PSP_FINTECH");
        result.Returns.Should().HaveCount(1);
        result.Returns[0].ReturnCode.Should().Be("CBN_300");
        result.Returns[0].Fields.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTemplateStructureAsync_NonExistentModule_ReturnsNull()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _cacheMock.Setup(c => c.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedTemplate>());

        var service = CreateService();

        // Act
        var result = await service.GetTemplateStructureAsync(tenantId, "NONEXISTENT");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDeadlinesAsync_MapsRagStatusCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _filingMock.Setup(f => f.GetRagStatus(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Models.RagItem>
            {
                new()
                {
                    ModuleCode = "PSP_FINTECH",
                    ModuleName = "PSP Fintech",
                    PeriodLabel = "2025-12",
                    Deadline = DateTime.UtcNow.AddDays(10),
                    Color = Domain.Models.RagColor.Green
                }
            });

        var service = CreateService();

        // Act
        var result = await service.GetDeadlinesAsync(tenantId);

        // Assert
        result.Should().HaveCount(1);
        result[0].ModuleCode.Should().Be("PSP_FINTECH");
        result[0].RagStatus.Should().Be("Green");
        result[0].DaysRemaining.Should().BePositive();
    }

    [Fact]
    public async Task GetComplianceScoreAsync_WithNoSubmissions_ReturnsDefaultScore()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        _submissionRepoMock.Setup(r => r.GetByInstitution(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Submission>());

        var service = CreateService();

        // Act
        var result = await service.GetComplianceScoreAsync(tenantId, 1, new CaaSScoreRequest());

        // Assert
        result.OverallScore.Should().BeGreaterOrEqualTo(0);
        result.Rating.Should().NotBeNullOrEmpty();
        result.Breakdown.TotalSubmissions.Should().Be(0);
    }

    [Fact]
    public async Task GetRegulatoryChangesAsync_FiltersOnModule()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var templates = new List<CachedTemplate>
        {
            CreateTestTemplate("CBN_300", "PSP_FINTECH"),
            new CachedTemplate
            {
                TemplateId = 2,
                ReturnCode = "CBN_400",
                Name = "Other Template",
                ModuleCode = "OTHER_MODULE",
                CurrentVersion = new CachedTemplateVersion
                {
                    Id = 2,
                    VersionNumber = 2,
                    Fields = Array.Empty<TemplateField>(),
                    IntraSheetFormulas = Array.Empty<IntraSheetFormula>()
                }
            }
        };

        _cacheMock.Setup(c => c.GetAllPublishedTemplates(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var service = CreateService();

        // Act
        var result = await service.GetRegulatoryChangesAsync(tenantId, "OTHER_MODULE");

        // Assert
        result.Should().HaveCount(1);
        result[0].ModuleCode.Should().Be("OTHER_MODULE");
        result[0].ToVersion.Should().Be(2);
    }

    [Fact]
    public async Task SimulateAsync_AppliesOverrides()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var template = CreateTestTemplate();
        _cacheMock.Setup(c => c.GetPublishedTemplate(tenantId, "CBN_300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _formulaEvalMock.Setup(f => f.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());
        _businessRuleMock.Setup(b => b.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<Submission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());

        var request = new CaaSSimulateRequest
        {
            ReturnCode = "CBN_300",
            ScenarioName = "Test Scenario",
            Records = new List<Dictionary<string, object?>>
            {
                new() { { "TotalAssets", 1000000m } }
            },
            Overrides = new Dictionary<string, object?>
            {
                { "TotalLiabilities", 600000m }
            }
        };

        var service = CreateService();

        // Act
        var result = await service.SimulateAsync(tenantId, request);

        // Assert
        result.ScenarioName.Should().Be("Test Scenario");
        result.ProjectedComplianceScore.Should().NotBeNull();
        result.Recommendations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SubmitReturnAsync_DelegatesToDataFeedService()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var template = CreateTestTemplate();

        _dataFeedMock.Setup(d => d.ProcessFeed(
                tenantId, "CBN_300", It.IsAny<DataFeedRequest>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataFeedResult
            {
                Success = true,
                SubmissionId = 42,
                Status = "Accepted",
                ReturnCode = "CBN_300"
            });

        _cacheMock.Setup(c => c.GetPublishedTemplate(tenantId, "CBN_300", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _formulaEvalMock.Setup(f => f.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());
        _businessRuleMock.Setup(b => b.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<Submission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ValidationError>());

        var request = new CaaSSubmitRequest
        {
            ReturnCode = "CBN_300",
            PeriodCode = "2025-12",
            Records = new List<Dictionary<string, object?>>
            {
                new() { { "TotalAssets", 1000000m } }
            }
        };

        var service = CreateService();

        // Act
        var result = await service.SubmitReturnAsync(tenantId, 1, request);

        // Assert
        result.Success.Should().BeTrue();
        result.SubmissionId.Should().Be(42);
        result.Status.Should().Be("Accepted");
        result.ProcessingDurationMs.Should().BeGreaterOrEqualTo(0);
    }
}
