using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Validation;
using FluentAssertions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Validation;

public class FormulaEvaluatorTests
{
    private readonly Mock<ITemplateMetadataCache> _cacheMock = new();
    private readonly FormulaEvaluator _evaluator;

    public FormulaEvaluatorTests()
    {
        _evaluator = new FormulaEvaluator(_cacheMock.Object);
    }

    [Fact]
    public async Task Evaluate_SumFormula_Passing_ShouldReturnNoErrors()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["cash_notes"] = 100m,
            ["cash_coins"] = 50m,
            ["total_cash"] = 150m
        });

        SetupCache("MFCR 300", CreateSumFormula("total_cash", ["cash_notes", "cash_coins"]));

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_SumFormula_Failing_ShouldReturnError()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["cash_notes"] = 100m,
            ["cash_coins"] = 50m,
            ["total_cash"] = 999m  // Wrong total
        });

        SetupCache("MFCR 300", CreateSumFormula("total_cash", ["cash_notes", "cash_coins"]));

        var errors = await _evaluator.Evaluate(record);

        errors.Should().HaveCount(1);
        errors[0].Category.Should().Be(ValidationCategory.IntraSheet);
        errors[0].Field.Should().Be("total_cash");
    }

    [Fact]
    public async Task Evaluate_SumFormula_WithTolerance_ShouldPass()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["a"] = 100m,
            ["b"] = 50m,
            ["total"] = 150.5m  // Off by 0.5, within tolerance of 1
        });

        var formula = CreateSumFormula("total", ["a", "b"]);
        formula.ToleranceAmount = 1m;
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_DifferenceFormula_Passing()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["gross"] = 200m,
            ["deductions"] = 50m,
            ["net"] = 150m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "DIFF-001",
            FormulaType = FormulaType.Difference,
            TargetFieldName = "net",
            OperandFields = "[\"gross\", \"deductions\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_DifferenceFormula_Failing()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["gross"] = 200m,
            ["deductions"] = 50m,
            ["net"] = 999m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "DIFF-001",
            FormulaType = FormulaType.Difference,
            TargetFieldName = "net",
            OperandFields = "[\"gross\", \"deductions\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task Evaluate_EqualsFormula()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["field_a"] = 100m,
            ["field_b"] = 100m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "EQ-001",
            FormulaType = FormulaType.Equals,
            TargetFieldName = "field_a",
            OperandFields = "[\"field_b\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_GreaterThan()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["assets"] = 500m,
            ["liabilities"] = 300m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "GT-001",
            FormulaType = FormulaType.GreaterThan,
            TargetFieldName = "assets",
            OperandFields = "[\"liabilities\"]",
            Severity = ValidationSeverity.Warning,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_BetweenFormula_WithinRange()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["ratio"] = 15m,
            ["lower_bound"] = 10m,
            ["upper_bound"] = 20m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "BTW-001",
            FormulaType = FormulaType.Between,
            TargetFieldName = "ratio",
            OperandFields = "[\"lower_bound\", \"upper_bound\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_BetweenFormula_OutOfRange()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["ratio"] = 25m,
            ["lower_bound"] = 10m,
            ["upper_bound"] = 20m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "BTW-001",
            FormulaType = FormulaType.Between,
            TargetFieldName = "ratio",
            OperandFields = "[\"lower_bound\", \"upper_bound\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().HaveCount(1);
    }

    [Fact]
    public async Task Evaluate_RatioFormula()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["numerator"] = 100m,
            ["denominator"] = 400m,
            ["ratio_result"] = 0.25m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "RAT-001",
            FormulaType = FormulaType.Ratio,
            TargetFieldName = "ratio_result",
            OperandFields = "[\"numerator\", \"denominator\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_RatioFormula_DivisionByZero_ShouldSkip()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["numerator"] = 100m,
            ["denominator"] = 0m,
            ["ratio_result"] = 999m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "RAT-001",
            FormulaType = FormulaType.Ratio,
            TargetFieldName = "ratio_result",
            OperandFields = "[\"numerator\", \"denominator\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty(); // Division by zero is skipped
    }

    [Fact]
    public async Task Evaluate_CustomFormula()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["total_assets"] = 500m,
            ["cash"] = 100m,
            ["investments"] = 200m,
            ["loans"] = 200m
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "CUST-001",
            FormulaType = FormulaType.Custom,
            TargetFieldName = "total_assets",
            OperandFields = "[]",
            CustomExpression = "total_assets = cash + investments + loans",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_RequiredFormula_MissingField_ShouldReturnError()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["field_a"] = 100m,
            ["field_b"] = null
        });

        var formula = new IntraSheetFormula
        {
            RuleCode = "REQ-001",
            FormulaType = FormulaType.Required,
            TargetFieldName = "field_b",
            OperandFields = "[\"field_a\", \"field_b\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().HaveCount(1);
        errors[0].Field.Should().Be("field_b");
    }

    [Fact]
    public async Task Evaluate_MultiRow_ShouldCheckEachRow()
    {
        var record = new ReturnDataRecord("MFCR 360", 1, StructuralCategory.MultiRow);

        var row1 = new ReturnDataRow { RowKey = "1" };
        row1.SetValue("amount_a", 100m);
        row1.SetValue("amount_b", 50m);
        row1.SetValue("total", 150m);  // Correct

        var row2 = new ReturnDataRow { RowKey = "2" };
        row2.SetValue("amount_a", 200m);
        row2.SetValue("amount_b", 100m);
        row2.SetValue("total", 999m);  // Wrong

        record.AddRow(row1);
        record.AddRow(row2);

        SetupCache("MFCR 360", CreateSumFormula("total", ["amount_a", "amount_b"]),
            StructuralCategory.MultiRow);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().HaveCount(1);
        errors[0].Field.Should().Contain("row: 2");
    }

    [Fact]
    public async Task Evaluate_NoFormulas_ShouldReturnEmpty()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?> { ["a"] = 1m });
        SetupCache("MFCR 300");

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_PercentageTolerance_ShouldPass()
    {
        var record = CreateFixedRowRecord(new Dictionary<string, object?>
        {
            ["a"] = 100m,
            ["b"] = 50m,
            ["total"] = 153m  // 3 off from 150, 2% within 5% tolerance
        });

        var formula = CreateSumFormula("total", ["a", "b"]);
        formula.TolerancePercent = 5m;
        SetupCache("MFCR 300", formula);

        var errors = await _evaluator.Evaluate(record);

        errors.Should().BeEmpty();
    }

    private ReturnDataRecord CreateFixedRowRecord(Dictionary<string, object?> fields)
    {
        var record = new ReturnDataRecord("MFCR 300", 1, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        foreach (var (k, v) in fields) row.SetValue(k, v);
        record.AddRow(row);
        return record;
    }

    private static IntraSheetFormula CreateSumFormula(string target, string[] operands)
    {
        return new IntraSheetFormula
        {
            RuleCode = "SUM-001",
            FormulaType = FormulaType.Sum,
            TargetFieldName = target,
            OperandFields = $"[\"{string.Join("\", \"", operands)}\"]",
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
    }

    private void SetupCache(string returnCode, IntraSheetFormula? formula = null,
        StructuralCategory category = StructuralCategory.FixedRow)
    {
        var fields = new List<TemplateField>
        {
            new() { FieldName = "cash_notes", FieldOrder = 1, DataType = FieldDataType.Money }
        }.AsReadOnly();

        var formulas = formula != null
            ? new List<IntraSheetFormula> { formula }.AsReadOnly()
            : new List<IntraSheetFormula>().AsReadOnly();

        var cached = new CachedTemplate
        {
            TemplateId = 1,
            ReturnCode = returnCode,
            Name = returnCode,
            StructuralCategory = category.ToString(),
            PhysicalTableName = returnCode.ToLowerInvariant().Replace(" ", "_"),
            XmlRootElement = returnCode.Replace(" ", ""),
            XmlNamespace = $"urn:cbn:dfis:fc:{returnCode.Replace(" ", "").ToLowerInvariant()}",
            CurrentVersion = new CachedTemplateVersion
            {
                Id = 1,
                VersionNumber = 1,
                Fields = fields,
                ItemCodes = new List<TemplateItemCode>().AsReadOnly(),
                IntraSheetFormulas = formulas
            }
        };

        _cacheMock.Setup(c => c.GetPublishedTemplate(returnCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);
    }
}
