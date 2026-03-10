namespace FC.Engine.Domain.Enums;

public enum ConsolidationMethod
{
    Full = 0,
    Proportional = 1,
    Equity = 2
}

public enum ConsolidationRunStatus
{
    Pending = 0,
    Collecting = 1,
    Converting = 2,
    Consolidating = 3,
    Adjusting = 4,
    Completed = 5,
    Failed = 6
}

public enum DataFlowTransformation
{
    Direct = 0,
    CurrencyConvert = 1,
    Formula = 2,
    Proportional = 3
}

public enum DivergenceType
{
    ThresholdChange = 0,
    FrameworkUpgrade = 1,
    NewRequirement = 2,
    CalculationMethodChange = 3,
    ReportingFrequencyChange = 4
}

public enum DivergenceSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum DivergenceStatus
{
    Open = 0,
    Acknowledged = 1,
    Tracking = 2,
    Resolved = 3,
    Superseded = 4
}

public enum FxRateType
{
    PeriodEnd = 0,
    Average = 1,
    Spot = 2
}

public enum AfcftaProtocolStatus
{
    Proposed = 0,
    Negotiating = 1,
    Agreed = 2,
    Enacted = 3,
    Effective = 4
}

public enum DeadlineStatus
{
    Upcoming = 0,
    DueSoon = 1,
    Overdue = 2,
    Submitted = 3,
    Completed = 4
}
