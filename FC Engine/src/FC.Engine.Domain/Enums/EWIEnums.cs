namespace FC.Engine.Domain.Enums;

// CAMELS component score — 1 (Strong) through 5 (Unsatisfactory) following CBN methodology
public enum CAMELSComponentScore { Strong = 1, Satisfactory = 2, Fair = 3, Marginal = 4, Unsatisfactory = 5 }

// Four-band CAMELS composite risk classification
public enum RiskBand { Green, Amber, Red, Critical }

// Sector-level systemic risk classification
public enum SystemicRiskBand { Low, Moderate, High, Severe }

// Early Warning Indicator severity levels
public enum EWISeverity { Low, Medium, High, Critical }

// Types of supervisory enforcement actions
public enum SupervisoryActionType
{
    AdvisoryLetter,
    WarningLetter,
    ShowCause,
    RemediationPlan,
    Sanctions,
    Escalation
}

// Status lifecycle for supervisory actions
public enum SupervisoryActionStatus
{
    Draft,
    Issued,
    Acknowledged,
    InRemediation,
    Closed,
    Escalated
}
