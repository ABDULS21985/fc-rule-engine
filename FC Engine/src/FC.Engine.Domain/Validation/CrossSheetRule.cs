using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Validation;

public class CrossSheetRule
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid? TenantId { get; set; }

    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    private readonly List<CrossSheetRuleOperand> _operands = new();
    public IReadOnlyList<CrossSheetRuleOperand> Operands => _operands.AsReadOnly();

    public CrossSheetRuleExpression? Expression { get; set; }

    public void AddOperand(CrossSheetRuleOperand operand)
    {
        operand.RuleId = Id;
        _operands.Add(operand);
    }

    public void SetOperands(IEnumerable<CrossSheetRuleOperand> operands)
    {
        _operands.Clear();
        _operands.AddRange(operands);
    }
}

public class CrossSheetRuleOperand
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public string OperandAlias { get; set; } = string.Empty;     // A, B, C
    public string TemplateReturnCode { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string? AggregateFunction { get; set; }               // SUM, COUNT, MAX, MIN, AVG
    public string? FilterItemCode { get; set; }
    public int SortOrder { get; set; }
}

public class CrossSheetRuleExpression
{
    public int Id { get; set; }
    public int RuleId { get; set; }
    public string Expression { get; set; } = string.Empty;       // "A = B" or "A >= B * 0.125"
    public decimal ToleranceAmount { get; set; }
    public decimal? TolerancePercent { get; set; }
    public string? ErrorMessage { get; set; }
}
