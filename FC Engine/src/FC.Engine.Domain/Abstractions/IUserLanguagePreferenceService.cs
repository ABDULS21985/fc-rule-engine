namespace FC.Engine.Domain.Abstractions;

public interface IUserLanguagePreferenceService
{
    Task<string> GetCurrentLanguage(CancellationToken ct = default);
}
