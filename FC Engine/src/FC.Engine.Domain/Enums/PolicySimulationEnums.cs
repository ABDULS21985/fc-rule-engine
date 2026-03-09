namespace FC.Engine.Domain.Enums;

public enum PolicyDomain
{
    CapitalAdequacy = 0,
    Liquidity = 1,
    Leverage = 2,
    FX = 3,
    RiskManagement = 4
}

public enum PolicyStatus
{
    Draft = 0,
    ParametersSet = 1,
    Simulated = 2,
    Consultation = 3,
    FeedbackClosed = 4,
    DecisionPending = 5,
    Enacted = 6,
    Archived = 7,
    Withdrawn = 8
}

public enum ImpactRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public enum ImpactCategory
{
    CurrentlyCompliant = 0,
    WouldBreach = 1,
    AlreadyBreaching = 2,
    NotAffected = 3
}

public enum FeedbackPosition
{
    Support = 0,
    PartialSupport = 1,
    Oppose = 2
}

public enum ProvisionPosition
{
    Support = 0,
    Oppose = 1,
    Neutral = 2,
    Amend = 3
}

public enum DecisionType
{
    Enact = 0,
    EnactAmended = 1,
    Defer = 2,
    Withdraw = 3
}

public enum ParameterUnit
{
    Percentage = 0,
    Ratio = 1,
    Absolute = 2,
    Bps = 3
}

public enum ConsultationStatus
{
    Draft = 0,
    Published = 1,
    Open = 2,
    Closed = 3,
    Aggregated = 4
}
