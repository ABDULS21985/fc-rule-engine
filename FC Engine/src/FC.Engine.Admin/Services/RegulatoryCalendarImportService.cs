using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

// ── Models ──────────────────────────────────────────────────────────────────

/// <summary>A single row parsed from a regulatory calendar CSV import file.</summary>
public class RegulatoryCalendarRow
{
    public int    RowNumber  { get; set; }
    public string ReturnCode { get; set; } = "";
    /// <summary>Period in yyyy-MM format e.g. "2025-03".</summary>
    public string Period     { get; set; } = "";
    /// <summary>The regulatory deadline date for this return/period combination.</summary>
    public DateTime Deadline { get; set; }
    /// <summary>Action that will be taken on import: Create | Update | Skip.</summary>
    public string Action     { get; set; } = "Create";
    /// <summary>Validation error message if row is invalid.</summary>
    public string? Error     { get; set; }
    public bool IsValid      => Error is null;
}

/// <summary>Result summary after executing a regulatory calendar import.</summary>
public class ImportResult
{
    public int          Created { get; set; }
    public int          Updated { get; set; }
    public int          Skipped { get; set; }
    public List<string> Errors  { get; set; } = new();
    public bool         HasErrors => Errors.Count > 0;
}

// ── Service ─────────────────────────────────────────────────────────────────

/// <summary>
/// Parses a CBN regulatory calendar CSV and applies deadline overrides to all tenant return periods.
/// Expected CSV format (with header): ReturnCode,Period,Deadline
/// Example row: BSL001,2025-03,2025-04-07
/// </summary>
public class RegulatoryCalendarImportService(IServiceProvider serviceProvider)
{

    /// <summary>
    /// Parses the CSV stream into a list of calendar rows for preview.
    /// Does NOT apply any changes to the database.
    /// </summary>
    public async Task<List<RegulatoryCalendarRow>> ParseCsvAsync(
        Stream csv,
        CancellationToken ct = default)
    {
        var rows = new List<RegulatoryCalendarRow>();
        using var reader = new System.IO.StreamReader(csv, leaveOpen: true);

        string? headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return rows;

        // Validate header
        var headerCols = headerLine.Split(',').Select(c => c.Trim().ToLowerInvariant()).ToArray();
        bool hasHeader = headerCols.Contains("returncode") || headerCols.Contains("period");

        // If no recognisable header, treat first line as data
        if (!hasHeader)
        {
            var parsed = ParseRow(headerLine, 1);
            if (parsed is not null) rows.Add(parsed);
        }

        var lineNum = 2;
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line)) { lineNum++; continue; }

            var row = ParseRow(line, lineNum);
            if (row is not null) rows.Add(row);
            lineNum++;
        }

        return rows;
    }

    /// <summary>
    /// Applies the preview rows to the database: sets <c>DeadlineOverrideDate</c> on matching
    /// <c>ReturnPeriod</c> records across all tenants. Rows with errors are skipped.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        List<RegulatoryCalendarRow> rows,
        string importedByUserId,
        CancellationToken ct = default)
    {
        var result = new ImportResult();
        var validRows = rows.Where(r => r.IsValid).ToList();

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        foreach (var row in validRows)
        {
            if (!int.TryParse(row.Period.Split('-')[1], out var month) ||
                !int.TryParse(row.Period.Split('-')[0], out var year))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                var periods = await db.ReturnPeriods
                    .Where(p => p.Year == year && p.Month == month)
                    .ToListAsync(ct);

                if (!periods.Any())
                {
                    result.Skipped++;
                    continue;
                }

                foreach (var period in periods)
                {
                    bool isNew = period.DeadlineOverrideDate is null;
                    period.DeadlineOverrideDate   = row.Deadline;
                    period.DeadlineOverrideReason = "CBN regulatory calendar import";
                    if (isNew) result.Created++;
                    else       result.Updated++;
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Row {row.RowNumber} ({row.ReturnCode} {row.Period}): {ex.Message}");
            }
        }

        return result;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static RegulatoryCalendarRow? ParseRow(string line, int rowNum)
    {
        var parts = line.Split(',');
        if (parts.Length < 3) return null;

        var row = new RegulatoryCalendarRow
        {
            RowNumber  = rowNum,
            ReturnCode = parts[0].Trim(),
            Period     = parts[1].Trim()
        };

        if (string.IsNullOrEmpty(row.ReturnCode))
        {
            row.Error = "ReturnCode is required";
            return row;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(row.Period, @"^\d{4}-\d{2}$"))
        {
            row.Error = $"Period '{row.Period}' must be yyyy-MM format";
            return row;
        }

        if (!DateTime.TryParse(parts[2].Trim(), out var deadline))
        {
            row.Error = $"Deadline '{parts[2].Trim()}' is not a valid date";
            return row;
        }

        row.Deadline = deadline.Date;
        return row;
    }
}
