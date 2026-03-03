using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.DataRecord;

/// <summary>
/// Universal container for return data. Replaces all 103 per-template IReturnData classes.
/// For FixedRow: contains exactly one row.
/// For MultiRow: contains N rows, each identified by serial_no.
/// For ItemCoded: contains N rows, each identified by item_code.
/// </summary>
public class ReturnDataRecord
{
    public string ReturnCode { get; }
    public int TemplateVersionId { get; }
    public StructuralCategory Category { get; }
    private readonly List<ReturnDataRow> _rows = new();

    public IReadOnlyList<ReturnDataRow> Rows => _rows.AsReadOnly();

    public ReturnDataRecord(string returnCode, int templateVersionId, StructuralCategory category)
    {
        ReturnCode = returnCode;
        TemplateVersionId = templateVersionId;
        Category = category;
    }

    public void AddRow(ReturnDataRow row) => _rows.Add(row);

    /// <summary>
    /// For FixedRow templates: get the single data row.
    /// </summary>
    public ReturnDataRow SingleRow => Category == StructuralCategory.FixedRow
        ? _rows.Single()
        : throw new InvalidOperationException("SingleRow only valid for FixedRow templates");

    /// <summary>
    /// Get a field value from the single row (FixedRow) or by row key (MultiRow/ItemCoded).
    /// </summary>
    public object? GetValue(string fieldName, string? rowKey = null)
    {
        var row = rowKey == null ? SingleRow : _rows.FirstOrDefault(r => r.RowKey == rowKey);
        return row?.GetValue(fieldName);
    }

    public decimal? GetDecimal(string fieldName, string? rowKey = null)
    {
        var val = GetValue(fieldName, rowKey);
        return val switch
        {
            null => null,
            decimal d => d,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, out var d) => d,
            _ => null
        };
    }
}

/// <summary>
/// A single row of return data. Each field is stored by name.
/// </summary>
public class ReturnDataRow
{
    private readonly Dictionary<string, object?> _fields = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Row identity: serial_no for MultiRow, item_code for ItemCoded, null for FixedRow.
    /// </summary>
    public string? RowKey { get; set; }

    public void SetValue(string fieldName, object? value) => _fields[fieldName] = value;

    public object? GetValue(string fieldName) =>
        _fields.TryGetValue(fieldName, out var val) ? val : null;

    public bool HasField(string fieldName) => _fields.ContainsKey(fieldName);

    public IReadOnlyDictionary<string, object?> AllFields => _fields;

    public decimal? GetDecimal(string fieldName)
    {
        var val = GetValue(fieldName);
        return val switch
        {
            null => null,
            decimal d => d,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, out var d) => d,
            _ => null
        };
    }

    public string? GetString(string fieldName) => GetValue(fieldName)?.ToString();

    public DateTime? GetDateTime(string fieldName)
    {
        var val = GetValue(fieldName);
        return val switch
        {
            null => null,
            DateTime dt => dt,
            string s when DateTime.TryParse(s, out var dt) => dt,
            _ => null
        };
    }
}
