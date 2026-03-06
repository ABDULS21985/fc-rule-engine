namespace FC.Engine.Domain.ValueObjects;

public class ReportDefinition
{
    public List<ReportFieldDef> Fields { get; set; } = new();
    public List<ReportFilterDef> Filters { get; set; } = new();
    public List<string> GroupBy { get; set; } = new();
    public List<ReportAggregationDef> Aggregations { get; set; } = new();
    public List<ReportSortDef> SortBy { get; set; } = new();
    public int Limit { get; set; } = 1000;
    public string DisplayMode { get; set; } = "Grid";
    public ReportChartConfig? ChartConfig { get; set; }
}

public class ReportFieldDef
{
    public string ModuleCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string? Alias { get; set; }
}

public class ReportFilterDef
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "=";
    public string Value { get; set; } = string.Empty;
}

public class ReportAggregationDef
{
    public string Field { get; set; } = string.Empty;
    public string Function { get; set; } = "SUM";
    public string? Alias { get; set; }
}

public class ReportSortDef
{
    public string Field { get; set; } = string.Empty;
    public string Direction { get; set; } = "ASC";
}

public class ReportChartConfig
{
    public string ChartType { get; set; } = "bar";
    public string LabelField { get; set; } = string.Empty;
    public string DataField { get; set; } = string.Empty;
}

public class ReportQueryResult
{
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRowCount { get; set; }
    public int QueryDurationMs { get; set; }
}

public class BoardPackSection
{
    public string ReportName { get; set; } = string.Empty;
    public List<string> ColumnNames { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public ReportChartConfig? ChartConfig { get; set; }
}
