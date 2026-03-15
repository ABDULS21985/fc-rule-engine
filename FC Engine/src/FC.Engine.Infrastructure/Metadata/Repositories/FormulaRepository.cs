using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class FormulaRepository : IFormulaRepository
{
    private readonly MetadataDbContext _db;

    public FormulaRepository(MetadataDbContext db) => _db = db;

    public async Task<IReadOnlyList<IntraSheetFormula>> GetIntraSheetFormulas(
        int templateVersionId, CancellationToken ct = default)
    {
        return await _db.IntraSheetFormulas
            .Where(f => f.TemplateVersionId == templateVersionId && f.IsActive)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CrossSheetRule>> GetCrossSheetRulesForTemplate(
        string returnCode, CancellationToken ct = default)
    {
        return await _db.CrossSheetRules
            .Include(r => r.Operands)
            .Include(r => r.Expression)
            .Where(r => r.IsActive && r.Operands.Any(o => o.TemplateReturnCode == returnCode))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CrossSheetRule>> GetAllActiveCrossSheetRules(CancellationToken ct = default)
    {
        return await _db.CrossSheetRules
            .Include(r => r.Operands)
            .Include(r => r.Expression)
            .Where(r => r.IsActive)
            .ToListAsync(ct);
    }

    public async Task<CrossSheetRule?> GetCrossSheetRuleById(int id, CancellationToken ct = default)
    {
        return await _db.CrossSheetRules
            .Include(r => r.Operands)
            .Include(r => r.Expression)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<BusinessRule>> GetActiveBusinessRules(CancellationToken ct = default)
    {
        return await _db.BusinessRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BusinessRule>> GetBusinessRulesForTemplate(
        string returnCode, CancellationToken ct = default)
    {
        // AppliesToTemplates stores either "*" (all templates) or a JSON array like ["MFCR 300","BSL 100"].
        // SQL Server has no native JSON containment operator, so we load all active rules (a small
        // regulatory-metadata set) and perform exact token matching in memory via JSON deserialization.
        // This handles all JSON formatting variations (spaces, case) with no false positives.
        var activeRules = await _db.BusinessRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        return activeRules
            .Where(r => r.AppliesToTemplates == "*" || MatchesTemplateCode(r.AppliesToTemplates, returnCode))
            .ToList()
            .AsReadOnly();
    }

    private static bool MatchesTemplateCode(string? json, string returnCode)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var codes = JsonSerializer.Deserialize<string[]>(json);
            return codes?.Contains(returnCode, StringComparer.OrdinalIgnoreCase) == true;
        }
        catch
        {
            // Fallback for malformed JSON: quoted-token boundary check
            return json.Contains($"\"{returnCode}\"", StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task AddIntraSheetFormula(IntraSheetFormula formula, CancellationToken ct = default)
    {
        _db.IntraSheetFormulas.Add(formula);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddCrossSheetRule(CrossSheetRule rule, CancellationToken ct = default)
    {
        _db.CrossSheetRules.Add(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddBusinessRule(BusinessRule rule, CancellationToken ct = default)
    {
        _db.BusinessRules.Add(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateIntraSheetFormula(IntraSheetFormula formula, CancellationToken ct = default)
    {
        _db.IntraSheetFormulas.Update(formula);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IntraSheetFormula?> GetIntraSheetFormulaById(int id, CancellationToken ct = default)
    {
        return await _db.IntraSheetFormulas.FindAsync(new object[] { id }, ct);
    }

    public async Task UpdateCrossSheetRule(CrossSheetRule rule, CancellationToken ct = default)
    {
        _db.CrossSheetRules.Update(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteIntraSheetFormula(int id, CancellationToken ct = default)
    {
        var formula = await _db.IntraSheetFormulas.FindAsync(new object[] { id }, ct);
        if (formula != null)
        {
            formula.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DeleteCrossSheetRule(int id, CancellationToken ct = default)
    {
        var rule = await _db.CrossSheetRules.FindAsync(new object[] { id }, ct);
        if (rule != null)
        {
            rule.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<IntraSheetFormula>> GetAllIntraSheetFormulas(CancellationToken ct = default)
    {
        return await _db.IntraSheetFormulas
            .Where(f => f.IsActive)
            .OrderBy(f => f.TemplateVersionId)
            .ThenBy(f => f.SortOrder)
            .ToListAsync(ct);
    }
}
