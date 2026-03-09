using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Scores institutions using the CBN CAMELS framework (C20/A20/M15/E20/L15/S10 weighting).
/// Persists ratings to camels_ratings table.
/// </summary>
public interface ICAMELSScorer
{
    /// <summary>Scores a single institution for the given period.</summary>
    Task<CAMELSResult> ScoreInstitutionAsync(
        int institutionId,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);

    /// <summary>Scores all institutions of a given type within a regulator's jurisdiction.</summary>
    Task<IReadOnlyList<CAMELSResult>> ScoreSectorAsync(
        string regulatorCode,
        string institutionType,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);
}
