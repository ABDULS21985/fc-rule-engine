using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;

namespace FC.Engine.Application.Services;

public class FormulaService
{
    private readonly IFormulaRepository _formulaRepo;
    private readonly ITemplateRepository _templateRepo;
    private readonly IAuditLogger _audit;

    public FormulaService(
        IFormulaRepository formulaRepo,
        ITemplateRepository templateRepo,
        IAuditLogger audit)
    {
        _formulaRepo = formulaRepo;
        _templateRepo = templateRepo;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FormulaDto>> GetIntraSheetFormulas(int templateVersionId, CancellationToken ct = default)
    {
        var formulas = await _formulaRepo.GetIntraSheetFormulas(templateVersionId, ct);
        return formulas.Select(f => new FormulaDto
        {
            Id = f.Id,
            RuleCode = f.RuleCode,
            RuleName = f.RuleName,
            FormulaType = f.FormulaType.ToString(),
            TargetFieldName = f.TargetFieldName,
            TargetLineCode = f.TargetLineCode,
            OperandFields = f.OperandFields,
            CustomExpression = f.CustomExpression,
            ToleranceAmount = f.ToleranceAmount,
            TolerancePercent = f.TolerancePercent,
            Severity = f.Severity.ToString(),
            IsActive = f.IsActive
        }).ToList();
    }

    public async Task AddIntraSheetFormula(
        int templateId, int versionId,
        string ruleCode, string ruleName, FormulaType formulaType,
        string targetFieldName, string operandFields,
        string? customExpression, decimal toleranceAmount,
        ValidationSeverity severity, string createdBy,
        CancellationToken ct = default)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var version = template.GetVersion(versionId);
        if (version.Status != TemplateStatus.Draft)
            throw new InvalidOperationException("Formulas can only be added to Draft versions");

        var formula = new IntraSheetFormula
        {
            TemplateVersionId = versionId,
            RuleCode = ruleCode,
            RuleName = ruleName,
            FormulaType = formulaType,
            TargetFieldName = targetFieldName,
            OperandFields = operandFields,
            CustomExpression = customExpression,
            ToleranceAmount = toleranceAmount,
            Severity = severity,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        await _formulaRepo.AddIntraSheetFormula(formula, ct);
        await _audit.Log("IntraSheetFormula", formula.Id, "Created", null, formula, createdBy, ct);
    }

    public async Task AddCrossSheetRule(
        string ruleCode, string ruleName, string? description,
        List<CrossSheetRuleOperand> operands, CrossSheetRuleExpression expression,
        ValidationSeverity severity, string createdBy,
        CancellationToken ct = default)
    {
        var rule = new CrossSheetRule
        {
            RuleCode = ruleCode,
            RuleName = ruleName,
            Description = description,
            Severity = severity,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            Expression = expression
        };

        rule.SetOperands(operands);
        await _formulaRepo.AddCrossSheetRule(rule, ct);
        await _audit.Log("CrossSheetRule", rule.Id, "Created", null, rule, createdBy, ct);
    }

    public async Task AddBusinessRule(
        string ruleCode, string ruleName, string ruleType,
        string? expression, string? appliesToTemplates,
        ValidationSeverity severity, string createdBy,
        CancellationToken ct = default)
    {
        var rule = new BusinessRule
        {
            RuleCode = ruleCode,
            RuleName = ruleName,
            RuleType = ruleType,
            Expression = expression,
            AppliesToTemplates = appliesToTemplates,
            Severity = severity,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        await _formulaRepo.AddBusinessRule(rule, ct);
        await _audit.Log("BusinessRule", rule.Id, "Created", null, rule, createdBy, ct);
    }

    public async Task<IReadOnlyList<FormulaDto>> GetAllFormulas(CancellationToken ct = default)
    {
        var formulas = await _formulaRepo.GetAllIntraSheetFormulas(ct);
        return formulas.Select(MapToDto).ToList();
    }

    public async Task UpdateIntraSheetFormula(
        int formulaId, string ruleName, FormulaType formulaType,
        string targetFieldName, string? targetLineCode, string operandFields,
        string? customExpression, decimal toleranceAmount,
        ValidationSeverity severity, string updatedBy,
        CancellationToken ct = default)
    {
        var formula = await _formulaRepo.GetIntraSheetFormulaById(formulaId, ct)
            ?? throw new InvalidOperationException($"Formula {formulaId} not found");

        var oldState = MapToDto(formula);

        formula.RuleName = ruleName;
        formula.FormulaType = formulaType;
        formula.TargetFieldName = targetFieldName;
        formula.TargetLineCode = targetLineCode;
        formula.OperandFields = operandFields;
        formula.CustomExpression = customExpression;
        formula.ToleranceAmount = toleranceAmount;
        formula.Severity = severity;

        await _formulaRepo.UpdateIntraSheetFormula(formula, ct);
        await _audit.Log("IntraSheetFormula", formula.Id, "Updated", oldState, formula, updatedBy, ct);
    }

    public async Task DeleteIntraSheetFormula(int formulaId, string deletedBy, CancellationToken ct = default)
    {
        var formula = await _formulaRepo.GetIntraSheetFormulaById(formulaId, ct)
            ?? throw new InvalidOperationException($"Formula {formulaId} not found");

        await _formulaRepo.DeleteIntraSheetFormula(formulaId, ct);
        await _audit.Log("IntraSheetFormula", formulaId, "Deleted", formula, null, deletedBy, ct);
    }

    public async Task DeleteCrossSheetRule(int ruleId, string deletedBy, CancellationToken ct = default)
    {
        await _formulaRepo.DeleteCrossSheetRule(ruleId, ct);
        await _audit.Log("CrossSheetRule", ruleId, "Deleted", null, null, deletedBy, ct);
    }

    private static FormulaDto MapToDto(IntraSheetFormula f) => new()
    {
        Id = f.Id,
        RuleCode = f.RuleCode,
        RuleName = f.RuleName,
        FormulaType = f.FormulaType.ToString(),
        TargetFieldName = f.TargetFieldName,
        TargetLineCode = f.TargetLineCode,
        OperandFields = f.OperandFields,
        CustomExpression = f.CustomExpression,
        ToleranceAmount = f.ToleranceAmount,
        TolerancePercent = f.TolerancePercent,
        Severity = f.Severity.ToString(),
        IsActive = f.IsActive
    };
}
