namespace FC.Engine.Domain.Enums;

public enum RegulatorReceiptStatus
{
    Received = 0,
    UnderReview = 1,
    Accepted = 2,
    FinalAccepted = 3,
    QueriesRaised = 4,
    ResponseReceived = 5
}

public enum ExaminerQueryStatus
{
    Open = 0,
    Responded = 1,
    Resolved = 2,
    Escalated = 3
}

public enum ExaminerQueryPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum ExaminationProjectStatus
{
    Draft = 0,
    InProgress = 1,
    Completed = 2,
    Archived = 3
}

public enum ExaminationWorkflowStatus
{
    ToReview = 0,
    InProgress = 1,
    FindingDocumented = 2,
    ManagementResponseRequired = 3,
    Closed = 4
}

public enum ExaminationRiskRating
{
    Low = 0,
    Medium = 1,
    High = 2
}

public enum ExaminationRemediationStatus
{
    Open = 0,
    AwaitingManagementResponse = 1,
    InRemediation = 2,
    PendingVerification = 3,
    Closed = 4,
    Overdue = 5,
    Escalated = 6
}

public enum ExaminationEvidenceRequestStatus
{
    Open = 0,
    Fulfilled = 1,
    Cancelled = 2
}

public enum ExaminationEvidenceKind
{
    SupportingDocument = 0,
    RequestedData = 1,
    RemediationEvidence = 2,
    ReportAttachment = 3
}

public enum ExaminationEvidenceUploaderRole
{
    Examiner = 0,
    Institution = 1,
    Management = 2
}

public enum EarlyWarningSeverity
{
    Amber = 0,
    Red = 1
}
