using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;

namespace FC.Engine.Domain.Abstractions;

public interface IFormulaRepository
{
    Task<IReadOnlyList<IntraSheetFormula>> GetIntraSheetFormulas(int templateVersionId, CancellationToken ct = default);
    Task<IReadOnlyList<CrossSheetRule>> GetCrossSheetRulesForTemplate(string returnCode, CancellationToken ct = default);
    Task<IReadOnlyList<CrossSheetRule>> GetAllActiveCrossSheetRules(CancellationToken ct = default);
    Task<IReadOnlyList<BusinessRule>> GetActiveBusinessRules(CancellationToken ct = default);
    Task<IReadOnlyList<BusinessRule>> GetBusinessRulesForTemplate(string returnCode, CancellationToken ct = default);
    Task AddIntraSheetFormula(IntraSheetFormula formula, CancellationToken ct = default);
    Task AddCrossSheetRule(CrossSheetRule rule, CancellationToken ct = default);
    Task AddBusinessRule(BusinessRule rule, CancellationToken ct = default);
    Task<IntraSheetFormula?> GetIntraSheetFormulaById(int id, CancellationToken ct = default);
    Task UpdateIntraSheetFormula(IntraSheetFormula formula, CancellationToken ct = default);
    Task DeleteIntraSheetFormula(int id, CancellationToken ct = default);
    Task UpdateCrossSheetRule(CrossSheetRule rule, CancellationToken ct = default);
    Task DeleteCrossSheetRule(int id, CancellationToken ct = default);
    Task<IReadOnlyList<IntraSheetFormula>> GetAllIntraSheetFormulas(CancellationToken ct = default);
}
