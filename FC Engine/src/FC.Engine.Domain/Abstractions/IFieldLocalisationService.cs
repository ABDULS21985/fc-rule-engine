namespace FC.Engine.Domain.Abstractions;

public interface IFieldLocalisationService
{
    Task<IReadOnlyDictionary<int, FieldLocalisationValue>> GetLocalisations(
        IEnumerable<int> fieldIds,
        string languageCode,
        CancellationToken ct = default);
}

public sealed class FieldLocalisationValue
{
    public string Label { get; init; } = string.Empty;
    public string? HelpText { get; init; }
}
