namespace FC.Engine.Application.DTOs;

public class DirectSubmissionDto
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public string RegulatorCode { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RegulatorReference { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SignatureAlgorithm { get; set; }
    public string? CertificateThumbprint { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SubmitToRegulatorRequest
{
    public string RegulatorCode { get; set; } = string.Empty;
}

public class DirectSubmissionStatusDto
{
    public int DirectSubmissionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string? RegulatorReference { get; set; }
    public string? LatestMessage { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public List<ExaminerQueryDto> Queries { get; set; } = new();
}

public class ExaminerQueryDto
{
    public string QueryId { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime RaisedAt { get; set; }
}
