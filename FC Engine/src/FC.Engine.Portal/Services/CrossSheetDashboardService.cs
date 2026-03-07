namespace FC.Engine.Portal.Services;

using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Validation;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

public class CrossSheetDashboardService
{
    private readonly IFormulaRepository _formulaRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public CrossSheetDashboardService(
        IFormulaRepository formulaRepo,
        ISubmissionRepository submissionRepo,
        ITemplateMetadataCache templateCache,
        IMemoryCache cache)
    {
        _formulaRepo = formulaRepo;
        _submissionRepo = submissionRepo;
        _templateCache = templateCache;
        _cache = cache;
    }

    public async Task<CrossSheetDashboardData> GetDashboardDataAsync(
        int institutionId, CancellationToken ct = default)
    {
        var cacheKey = $"xs-dashboard:{institutionId}";
        if (_cache.TryGetValue(cacheKey, out CrossSheetDashboardData? cached) && cached is not null)
            return cached;

        var data = await BuildDashboardAsync(institutionId, ct);
        _cache.Set(cacheKey, data, CacheDuration);
        return data;
    }

    public void InvalidateCache(int institutionId) =>
        _cache.Remove($"xs-dashboard:{institutionId}");

    private async Task<CrossSheetDashboardData> BuildDashboardAsync(
        int institutionId, CancellationToken ct)
    {
        var rules = await _formulaRepo.GetAllActiveCrossSheetRules(ct);
        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);
        var allTemplates = await _templateCache.GetAllPublishedTemplates(ct);

        var templateNameLookup = allTemplates.ToDictionary(
            t => t.ReturnCode,
            t => t.Name,
            StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonth.AddMonths(1).AddDays(-1);

        // Current period submissions (latest per return code, non-rejected)
        var currentSubmissions = submissions
            .Where(s => s.SubmittedAt >= currentMonth && s.SubmittedAt <= currentMonthEnd)
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .GroupBy(s => s.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => s.SubmittedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        // Build set of submitted return codes
        var submittedCodes = currentSubmissions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Collect cross-sheet validation errors from current submissions, keyed by RuleId
        var crossSheetErrors = currentSubmissions.Values
            .Where(s => s.ValidationReport is not null)
            .SelectMany(s => s.ValidationReport!.Errors)
            .Where(e => e.Category == ValidationCategory.CrossSheet)
            .GroupBy(e => e.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Evaluate each rule
        var ruleStatuses = new List<CrossSheetRuleStatusItem>();
        var allInvolvedTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            var involvedCodes = rule.Operands
                .Select(o => o.TemplateReturnCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var code in involvedCodes)
                allInvolvedTemplates.Add(code);

            var missing = involvedCodes.Where(c => !submittedCodes.Contains(c)).ToList();
            var allSubmitted = missing.Count == 0;

            RuleEvalStatus status;
            string? failureDetail = null;

            if (!allSubmitted)
            {
                status = RuleEvalStatus.NotEvaluated;
                foreach (var m in missing)
                    missingTemplates.Add(m);
            }
            else if (crossSheetErrors.TryGetValue(rule.RuleCode, out var errors) && errors.Count > 0)
            {
                status = RuleEvalStatus.Fail;
                var firstError = errors[0];
                failureDetail = firstError.Message;
            }
            else
            {
                status = RuleEvalStatus.Pass;
            }

            // Determine last evaluated timestamp from the most recent submission
            DateTime? lastEvaluated = null;
            if (allSubmitted)
            {
                lastEvaluated = involvedCodes
                    .Where(currentSubmissions.ContainsKey)
                    .Select(c => currentSubmissions[c].SubmittedAt)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
            }

            // Build operand detail for expanded view — include best-effort actual values
            var operandDetails = rule.Operands.Select(op =>
            {
                var opSubmitted = submittedCodes.Contains(op.TemplateReturnCode);
                decimal? actualValue = null;
                if (status == RuleEvalStatus.Fail && !string.IsNullOrEmpty(failureDetail))
                    actualValue = TryExtractOperandValue(failureDetail, op.OperandAlias);
                return new OperandDetail
                {
                    Alias = op.OperandAlias,
                    TemplateReturnCode = op.TemplateReturnCode,
                    TemplateName = templateNameLookup.GetValueOrDefault(op.TemplateReturnCode, op.TemplateReturnCode),
                    FieldName = op.FieldName,
                    AggregateFunction = op.AggregateFunction,
                    IsSubmitted = opSubmitted,
                    ActualValue = actualValue
                };
            }).ToList();

            ruleStatuses.Add(new CrossSheetRuleStatusItem
            {
                RuleCode = rule.RuleCode,
                RuleName = rule.RuleName,
                Description = rule.Description,
                Severity = rule.Severity,
                Status = status,
                FailureDetail = failureDetail,
                InvolvedTemplates = involvedCodes
                    .Select(c => new TemplateChip
                    {
                        ReturnCode = c,
                        Name = templateNameLookup.GetValueOrDefault(c, c),
                        IsSubmitted = submittedCodes.Contains(c)
                    }).ToList(),
                Expression = rule.Expression?.Expression ?? "",
                ToleranceAmount = rule.Expression?.ToleranceAmount ?? 0,
                TolerancePercent = rule.Expression?.TolerancePercent,
                LastEvaluated = lastEvaluated,
                Operands = operandDetails
            });
        }

        // Build dependency map
        var dependencyNodes = allInvolvedTemplates.Select(code => new DependencyNode
        {
            ReturnCode = code,
            Name = templateNameLookup.GetValueOrDefault(code, code),
            IsSubmitted = submittedCodes.Contains(code)
        }).OrderBy(n => n.ReturnCode).ToList();

        var dependencyEdges = rules.SelectMany(rule =>
        {
            var codes = rule.Operands
                .Select(o => o.TemplateReturnCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var edges = new List<DependencyEdge>();
            for (int i = 0; i < codes.Count - 1; i++)
            {
                for (int j = i + 1; j < codes.Count; j++)
                {
                    var ruleStatus = ruleStatuses.FirstOrDefault(r => r.RuleCode == rule.RuleCode);
                    edges.Add(new DependencyEdge
                    {
                        SourceCode = codes[i],
                        TargetCode = codes[j],
                        RuleCode = rule.RuleCode,
                        Status = ruleStatus?.Status ?? RuleEvalStatus.NotEvaluated
                    });
                }
            }
            return edges;
        }).ToList();

        // Missing dependencies
        var missingDeps = missingTemplates.Select(code => new MissingDependency
        {
            ReturnCode = code,
            Name = templateNameLookup.GetValueOrDefault(code, code),
            RequiredByRules = ruleStatuses
                .Where(r => r.InvolvedTemplates.Any(t => t.ReturnCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.RuleCode)
                .ToList()
        }).OrderBy(m => m.ReturnCode).ToList();

        // Historical trend (last 6 months)
        var trend = new List<CrossSheetTrendItem>();
        for (int i = 5; i >= 0; i--)
        {
            var monthStart = currentMonth.AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var monthSubmissions = submissions
                .Where(s => s.SubmittedAt >= monthStart && s.SubmittedAt <= monthEnd)
                .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
                .GroupBy(s => s.ReturnCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(s => s.SubmittedAt).First(),
                    StringComparer.OrdinalIgnoreCase);

            var monthSubmittedCodes = monthSubmissions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var monthCrossSheetErrors = monthSubmissions.Values
                .Where(s => s.ValidationReport is not null)
                .SelectMany(s => s.ValidationReport!.Errors)
                .Where(e => e.Category == ValidationCategory.CrossSheet)
                .Select(e => e.RuleId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int passing = 0, failing = 0, notEvaluated = 0;
            foreach (var rule in rules)
            {
                var involvedCodes = rule.Operands
                    .Select(o => o.TemplateReturnCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (involvedCodes.Any(c => !monthSubmittedCodes.Contains(c)))
                    notEvaluated++;
                else if (monthCrossSheetErrors.Contains(rule.RuleCode))
                    failing++;
                else
                    passing++;
            }

            trend.Add(new CrossSheetTrendItem
            {
                MonthLabel = monthStart.ToString("MMM"),
                Year = monthStart.Year,
                TotalRules = rules.Count,
                Passing = passing,
                Failing = failing,
                NotEvaluated = notEvaluated,
                PassRate = rules.Count > 0
                    ? Math.Round((passing + failing) > 0 ? passing * 100m / (passing + failing) : 0, 1)
                    : 0
            });
        }

        var totalRules = rules.Count;
        var passingCount = ruleStatuses.Count(r => r.Status == RuleEvalStatus.Pass);
        var failingCount = ruleStatuses.Count(r => r.Status == RuleEvalStatus.Fail);
        var notEvalCount = ruleStatuses.Count(r => r.Status == RuleEvalStatus.NotEvaluated);

        return new CrossSheetDashboardData
        {
            TotalRules = totalRules,
            PassingCount = passingCount,
            FailingCount = failingCount,
            NotEvaluatedCount = notEvalCount,
            CurrentPeriod = currentMonth.ToString("MMMM yyyy"),
            Rules = ruleStatuses.OrderBy(r => r.Status).ThenBy(r => r.RuleCode).ToList(),
            DependencyNodes = dependencyNodes,
            DependencyEdges = dependencyEdges,
            MissingDependencies = missingDeps,
            Trend = trend
        };
    }

    /// <summary>
    /// Best-effort extraction of a numeric value for the given operand alias from a failure message.
    /// The CrossSheetValidator typically produces messages containing alias–value pairs.
    /// </summary>
    private static decimal? TryExtractOperandValue(string? message, string alias)
    {
        if (string.IsNullOrEmpty(message)) return null;
        // Match patterns: "A: 12450", "A = 12450", "A (12,450)", "[A] 12450", "A=12450"
        var pattern = $@"\b{Regex.Escape(alias)}\s*(?:[:=({{\[])\s*([-]?\d[\d,]*\.?\d*)";
        var m = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
        if (m.Success &&
            decimal.TryParse(m.Groups[1].Value.Replace(",", ""),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var val))
            return val;
        return null;
    }
}

// ── View Models ──────────────────────────────────────────────────

public enum RuleEvalStatus
{
    Fail,
    NotEvaluated,
    Pass
}

public class CrossSheetDashboardData
{
    public int TotalRules { get; set; }
    public int PassingCount { get; set; }
    public int FailingCount { get; set; }
    public int NotEvaluatedCount { get; set; }
    public string CurrentPeriod { get; set; } = "";
    public List<CrossSheetRuleStatusItem> Rules { get; set; } = new();
    public List<DependencyNode> DependencyNodes { get; set; } = new();
    public List<DependencyEdge> DependencyEdges { get; set; } = new();
    public List<MissingDependency> MissingDependencies { get; set; } = new();
    public List<CrossSheetTrendItem> Trend { get; set; } = new();
}

public class CrossSheetRuleStatusItem
{
    public string RuleCode { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string? Description { get; set; }
    public ValidationSeverity Severity { get; set; }
    public RuleEvalStatus Status { get; set; }
    public string? FailureDetail { get; set; }
    public List<TemplateChip> InvolvedTemplates { get; set; } = new();
    public string Expression { get; set; } = "";
    public decimal ToleranceAmount { get; set; }
    public decimal? TolerancePercent { get; set; }
    public DateTime? LastEvaluated { get; set; }
    public List<OperandDetail> Operands { get; set; } = new();
}

public class TemplateChip
{
    public string ReturnCode { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSubmitted { get; set; }
}

public class OperandDetail
{
    public string Alias { get; set; } = "";
    public string TemplateReturnCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string? AggregateFunction { get; set; }
    public bool IsSubmitted { get; set; }
    /// <summary>Best-effort resolved field value from the validation error message.</summary>
    public decimal? ActualValue { get; set; }
}

public class DependencyNode
{
    public string ReturnCode { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsSubmitted { get; set; }
}

public class DependencyEdge
{
    public string SourceCode { get; set; } = "";
    public string TargetCode { get; set; } = "";
    public string RuleCode { get; set; } = "";
    public RuleEvalStatus Status { get; set; }
}

public class MissingDependency
{
    public string ReturnCode { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> RequiredByRules { get; set; } = new();
}

public class CrossSheetTrendItem
{
    public string MonthLabel { get; set; } = "";
    public int Year { get; set; }
    public int TotalRules { get; set; }
    public int Passing { get; set; }
    public int Failing { get; set; }
    public int NotEvaluated { get; set; }
    public decimal PassRate { get; set; }
}
