namespace FC.Engine.Domain.Enums;

public static class SubmissionStatusNames
{
    public const string Draft = nameof(SubmissionStatus.Draft);
    public const string Parsing = nameof(SubmissionStatus.Parsing);
    public const string Validating = nameof(SubmissionStatus.Validating);
    public const string Accepted = nameof(SubmissionStatus.Accepted);
    public const string AcceptedWithWarnings = nameof(SubmissionStatus.AcceptedWithWarnings);
    public const string Rejected = nameof(SubmissionStatus.Rejected);
    public const string PendingApproval = nameof(SubmissionStatus.PendingApproval);
    public const string ApprovalRejected = nameof(SubmissionStatus.ApprovalRejected);
    public const string Historical = nameof(SubmissionStatus.Historical);
    public const string SubmittedToRegulator = nameof(SubmissionStatus.SubmittedToRegulator);
    public const string RegulatorAcknowledged = nameof(SubmissionStatus.RegulatorAcknowledged);
    public const string RegulatorAccepted = nameof(SubmissionStatus.RegulatorAccepted);
    public const string RegulatorQueriesRaised = nameof(SubmissionStatus.RegulatorQueriesRaised);

    public static bool IsAcceptedLike(string? status) =>
        status is Accepted or AcceptedWithWarnings;

    public static bool IsRegulatorSubmittedLike(string? status) =>
        status is SubmittedToRegulator or RegulatorAcknowledged or RegulatorAccepted;
}
