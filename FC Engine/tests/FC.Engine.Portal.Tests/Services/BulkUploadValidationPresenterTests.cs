using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Portal.Services;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Portal.Tests.Services;

public class BulkUploadValidationPresenterTests
{
    [Fact]
    public void BuildSubmissionResult_Preserves_ExpectedValue_For_Modal_Rendering()
    {
        var bulkResult = new BulkUploadResult
        {
            Status = "Rejected",
            Errors =
            [
                new BulkUploadError
                {
                    FieldCode = "amount",
                    Message = "Amount is below the minimum.",
                    Severity = "Error",
                    Category = BulkUploadErrorCategories.TypeRange,
                    ExpectedValue = ">= 10"
                }
            ]
        };

        var result = BulkUploadValidationPresenter.BuildSubmissionResult("CAP_BUF", bulkResult);

        result.ValidationReport.Should().NotBeNull();
        result.ValidationReport!.Errors.Should().ContainSingle();
        result.ValidationReport.Errors[0].ExpectedValue.Should().Be(">= 10");
    }

    [Fact]
    public void BuildErrorReportCsv_Uses_ExpectedValue_When_Available()
    {
        var csv = BulkUploadValidationPresenter.BuildErrorReportCsv(
        [
            new BulkUploadValidationExportFile
            {
                FileName = "cap_buf_bad.csv",
                ReturnCode = "CAP_BUF",
                Errors =
                [
                    new ValidationErrorDto
                    {
                        RuleId = "BULK",
                        Field = "amount",
                        Message = "Amount is below the minimum.",
                        Severity = "Error",
                        Category = BulkUploadErrorCategories.TypeRange,
                        ExpectedValue = ">= 10"
                    }
                ]
            }
        ]);

        csv.Should().Contain("ExpectedFormat");
        csv.Should().Contain(">= 10");
    }

    [Fact]
    public void BuildErrorReportCsv_Uses_Category_Fallback_When_ExpectedValue_Is_Missing()
    {
        var csv = BulkUploadValidationPresenter.BuildErrorReportCsv(
        [
            new BulkUploadValidationExportFile
            {
                FileName = "cap_buf_bad.csv",
                ReturnCode = "CAP_BUF",
                Errors =
                [
                    new ValidationErrorDto
                    {
                        RuleId = "BULK",
                        Field = "effective_date",
                        Message = "Date format is invalid.",
                        Severity = "Error",
                        Category = BulkUploadErrorCategories.Format
                    }
                ]
            }
        ]);

        csv.Should().Contain("Expected date/code format per schema");
    }
}
