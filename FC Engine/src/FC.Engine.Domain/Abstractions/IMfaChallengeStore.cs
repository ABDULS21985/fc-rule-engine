namespace FC.Engine.Domain.Abstractions;

public interface IMfaChallengeStore
{
    Task<string> CreateChallenge(MfaLoginChallenge challenge, CancellationToken ct = default);
    Task<MfaLoginChallenge?> GetChallenge(string challengeId, CancellationToken ct = default);
    Task RemoveChallenge(string challengeId, CancellationToken ct = default);
}

public class MfaLoginChallenge
{
    public int UserId { get; set; }
    public string UserType { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
    public bool MustChangePassword { get; set; }
}
