using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Builds the interbank exposure directed graph and computes network centrality metrics
/// (eigenvector, betweenness) to identify Domestic Systemically Important Banks (D-SIBs).
/// </summary>
public interface IContagionAnalyzer
{
    /// <summary>
    /// Computes contagion risk scores for all nodes in the interbank network.
    /// Persists results and triggers CONTAGION_DSIB_RISK EWI if warranted.
    /// </summary>
    Task<IReadOnlyList<ContagionNode>> AnalyzeAsync(
        string regulatorCode,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns persisted graph nodes and directed edges for rendering in the portal.
    /// </summary>
    Task<(IReadOnlyList<ContagionNode> Nodes, IReadOnlyList<ContagionEdge> Edges)>
        GetNetworkGraphAsync(
            string regulatorCode,
            string periodCode,
            CancellationToken ct = default);
}
