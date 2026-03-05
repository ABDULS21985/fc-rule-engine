namespace FC.Engine.Domain.Abstractions;

public interface IMfaService
{
    Task<MfaSetupResult> InitiateSetup(int userId, string userType, string email);
    Task<MfaActivationResult> ActivateWithVerification(int userId, string userType, string code);
    Task<bool> VerifyCode(int userId, string code, string? userType = null);
    Task<bool> VerifyBackupCode(int userId, string backupCode, string? userType = null);
    Task Disable(int userId, string userType);
    Task<bool> IsMfaEnabled(int userId, string userType);
    Task<bool> IsMfaRequired(Guid tenantId, string role);
}

public class MfaSetupResult
{
    public string SecretKey { get; set; } = string.Empty;
    public string QrCodeDataUri { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
}

public class MfaActivationResult
{
    public bool Success { get; set; }
    public List<string> BackupCodes { get; set; } = new();
}
