using System.Text;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class BulkUploadServiceTests
{
    private const string ReturnCode = "CAP_BUF";

    [Fact]
    public void BulkUploadError_DefaultCategory_IsEmpty()
    {
        var error = new BulkUploadError();

        error.Category.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessCsvUpload_WhenValueBreaksRange_ReturnsExpectedValue()
    {
        var tenantId = Guid.NewGuid();
        var template = BuildTemplate(new TemplateField
        {
            FieldName = "amount",
            DisplayName = "Amount",
            DataType = FieldDataType.Decimal,
            MinValue = "10",
            FieldOrder = 1
        });

        var sut = BuildSut(template, out _, out _, out _);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Amount\n5\n"));

        var result = await sut.ProcessCsvUpload(stream, tenantId, ReturnCode, 41, 202603);

        result.Success.Should().BeFalse();
        result.ErrorFile.Should().NotBeNullOrEmpty();
        result.Errors.Should().Contain(error =>
            error.FieldCode == "amount"
            && error.Category == BulkUploadErrorCategories.TypeRange
            && error.ExpectedValue == ">= 10");
    }

    [Fact]
    public async Task ProcessCsvUpload_WhenValueFailsTypeConversion_SetsExplicitCategoryAndExpectedValue()
    {
        var tenantId = Guid.NewGuid();
        var template = BuildTemplate(new TemplateField
        {
            FieldName = "amount",
            DisplayName = "Amount",
            DataType = FieldDataType.Decimal,
            FieldOrder = 1
        });

        var sut = BuildSut(template, out _, out _, out _);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Amount\nnot-a-number\n"));

        var result = await sut.ProcessCsvUpload(stream, tenantId, ReturnCode, 41, 202603);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.FieldCode == "amount"
            && error.Category == BulkUploadErrorCategories.TypeRange
            && error.ExpectedValue == "Numeric value");
    }

    private static BulkUploadService BuildSut(
        CachedTemplate template,
        out Mock<ISubmissionRepository> submissionRepository,
        out Mock<IFormulaEvaluator> formulaEvaluator,
        out Mock<ICrossSheetValidator> crossSheetValidator)
    {
        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache
            .Setup(x => x.GetPublishedTemplate(It.IsAny<Guid>(), ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        templateCache
            .Setup(x => x.GetPublishedTemplate(ReturnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var dataRepository = new Mock<IGenericDataRepository>(MockBehavior.Strict);
        submissionRepository = new Mock<ISubmissionRepository>();
        submissionRepository
            .Setup(x => x.Add(It.IsAny<Domain.Entities.Submission>(), It.IsAny<CancellationToken>()))
            .Callback<Domain.Entities.Submission, CancellationToken>((submission, _) => submission.Id = 321)
            .Returns(Task.CompletedTask);
        submissionRepository
            .Setup(x => x.Update(It.IsAny<Domain.Entities.Submission>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        formulaEvaluator = new Mock<IFormulaEvaluator>();
        formulaEvaluator
            .Setup(x => x.Evaluate(It.IsAny<Domain.DataRecord.ReturnDataRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        crossSheetValidator = new Mock<ICrossSheetValidator>();
        crossSheetValidator
            .Setup(x => x.Validate(
                It.IsAny<Domain.DataRecord.ReturnDataRecord>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var businessRuleEvaluator = new Mock<IBusinessRuleEvaluator>();
        businessRuleEvaluator
            .Setup(x => x.Evaluate(
                It.IsAny<Domain.DataRecord.ReturnDataRecord>(),
                It.IsAny<Domain.Entities.Submission>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var validationOrchestrator = new ValidationOrchestrator(
            templateCache.Object,
            formulaEvaluator.Object,
            crossSheetValidator.Object,
            businessRuleEvaluator.Object);

        return new BulkUploadService(
            templateCache.Object,
            dataRepository.Object,
            submissionRepository.Object,
            validationOrchestrator,
            Mock.Of<ILogger<BulkUploadService>>());
    }

    private static CachedTemplate BuildTemplate(params TemplateField[] fields)
    {
        return new CachedTemplate
        {
            ReturnCode = ReturnCode,
            Name = "Capital Buffer",
            StructuralCategory = "FixedRow",
            CurrentVersion = new CachedTemplateVersion
            {
                Id = 12,
                VersionNumber = 1,
                Fields = fields
            }
        };
    }
}
