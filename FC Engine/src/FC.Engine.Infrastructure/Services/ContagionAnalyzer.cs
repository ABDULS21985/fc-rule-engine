using System.Data;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Builds the directed interbank exposure graph.
/// Computes eigenvector centrality (power iteration) and betweenness centrality (Brandes)
/// to identify Domestic Systemically Important Banks (D-SIBs).
/// </summary>
public sealed class ContagionAnalyzer : IContagionAnalyzer
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ContagionAnalyzer> _log;

    public ContagionAnalyzer(IDbConnectionFactory db, ILogger<ContagionAnalyzer> log)
    {
        _db = db; _log = log;
    }

    public async Task<IReadOnlyList<ContagionNode>> AnalyzeAsync(
        string regulatorCode, string periodCode,
        Guid computationRunId, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var edges = (await conn.QueryAsync<InterbankEdgeRow>(
            """
            SELECT LendingInstitutionId, BorrowingInstitutionId,
                   ExposureAmount, ExposureType
            FROM   meta.interbank_exposures
            WHERE  RegulatorCode = @Regulator AND PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        if (edges.Count == 0)
        {
            _log.LogInformation(
                "No interbank exposures for {Regulator}/{Period} — skipping contagion analysis.",
                regulatorCode, periodCode);
            return Array.Empty<ContagionNode>();
        }

        var nodeIds = edges
            .SelectMany(e => new[] { e.LendingInstitutionId, e.BorrowingInstitutionId })
            .Distinct().OrderBy(x => x).ToList();

        var n      = nodeIds.Count;
        var idxMap = nodeIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        var matrix             = new double[n, n];
        var outbound           = new decimal[n];
        var inbound            = new decimal[n];
        var counterpartyCount  = new int[n];

        foreach (var edge in edges)
        {
            var li = idxMap[edge.LendingInstitutionId];
            var bi = idxMap[edge.BorrowingInstitutionId];
            matrix[li, bi] += (double)edge.ExposureAmount;
            outbound[li]   += edge.ExposureAmount;
            inbound[bi]    += edge.ExposureAmount;
            counterpartyCount[li]++;
        }

        var eigenvector  = ComputeEigenvectorCentrality(matrix, n, iterations: 50);
        var betweenness  = ComputeBetweennessCentrality(matrix, n);

        // Load institution names
        var instNames = (await conn.QueryAsync<(int Id, string Name, string Type)>(
            """
            SELECT Id, InstitutionName AS Name, ISNULL(LicenseType,'UNKNOWN') AS Type
            FROM   institutions
            WHERE  Id IN @Ids
            """,
            new { Ids = nodeIds }))
            .ToDictionary(x => x.Id, x => (x.Name, x.Type));

        // D-SIB threshold: top 20% by eigenvector centrality
        var eigSorted     = eigenvector.OrderByDescending(v => v).ToArray();
        var dsibThreshold = eigSorted.Length > 0
            ? eigSorted[(int)(eigSorted.Length * 0.2)]
            : 0.0;

        var nodes = new List<ContagionNode>();
        var maxOutbound = outbound.Length > 0 ? (double)outbound.Max() : 1.0;

        for (int i = 0; i < n; i++)
        {
            var institutionId = nodeIds[i];
            var (name, type)  = instNames.TryGetValue(institutionId, out var t)
                ? t : ($"Institution {institutionId}", "UNKNOWN");

            var contagionScore = Math.Min(100.0, Math.Round(
                eigenvector[i] * 40 +
                betweenness[i] * 40 +
                (maxOutbound > 0 ? (double)outbound[i] / maxOutbound : 0) * 20, 1));

            var isDSIB = eigenvector[i] >= dsibThreshold && contagionScore >= 50;

            nodes.Add(new ContagionNode(
                InstitutionId: institutionId,
                InstitutionName: name,
                InstitutionType: type,
                TotalOutbound: outbound[i],
                TotalInbound: inbound[i],
                EigenvectorCentrality: Math.Round(eigenvector[i], 8),
                BetweennessCentrality: Math.Round(betweenness[i], 8),
                ContagionRiskScore: contagionScore,
                IsSystemicallyImportant: isDSIB));
        }

        // Persist results
        foreach (var node in nodes)
        {
            var cc = edges.Count(e =>
                e.LendingInstitutionId  == node.InstitutionId ||
                e.BorrowingInstitutionId == node.InstitutionId);

            await conn.ExecuteAsync(
                """
                MERGE meta.contagion_analysis_results AS t
                USING (VALUES (@InstId, @Period)) AS s (InstitutionId, PeriodCode)
                ON t.InstitutionId = s.InstitutionId AND t.PeriodCode = s.PeriodCode
                WHEN MATCHED THEN
                    UPDATE SET EigenvectorCentrality=@Eig, BetweennessCentrality=@Bet,
                               TotalOutboundExposure=@Out, TotalInboundExposure=@In,
                               DirectCounterparties=@CC, ContagionRiskScore=@Score,
                               IsSystemicallyImportant=@DSIB,
                               RegulatorCode=@Regulator, ComputationRunId=@RunId,
                               ComputedAt=SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (InstitutionId, RegulatorCode, PeriodCode,
                            EigenvectorCentrality, BetweennessCentrality,
                            TotalOutboundExposure, TotalInboundExposure,
                            DirectCounterparties, ContagionRiskScore,
                            IsSystemicallyImportant, ComputationRunId)
                    VALUES (@InstId, @Regulator, @Period, @Eig, @Bet,
                            @Out, @In, @CC, @Score, @DSIB, @RunId);
                """,
                new { InstId = node.InstitutionId, Regulator = regulatorCode,
                      Period = periodCode, Eig = node.EigenvectorCentrality,
                      Bet = node.BetweennessCentrality, Out = node.TotalOutbound,
                      In  = node.TotalInbound, CC = cc,
                      Score = node.ContagionRiskScore,
                      DSIB = node.IsSystemicallyImportant, RunId = computationRunId });
        }

        var dsibCount = nodes.Count(nd => nd.IsSystemicallyImportant);
        _log.LogInformation(
            "Contagion analysis complete: {Regulator}/{Period} Nodes={N} D-SIBs={D}",
            regulatorCode, periodCode, nodes.Count, dsibCount);

        // Trigger CONTAGION_DSIB_RISK EWI if any D-SIB has critical contagion score
        var criticalDSIBs = nodes.Where(nd => nd.IsSystemicallyImportant &&
                                               nd.ContagionRiskScore >= 70).ToList();
        if (criticalDSIBs.Count > 0)
        {
            await conn.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM meta.ewi_triggers
                    WHERE  EWICode='CONTAGION_DSIB_RISK' AND RegulatorCode=@Regulator
                      AND  PeriodCode=@Period AND IsActive=1
                )
                INSERT INTO meta.ewi_triggers
                    (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                     Severity, TriggerValue, ThresholdValue, IsActive, IsSystemic, ComputationRunId)
                VALUES ('CONTAGION_DSIB_RISK', 0, @Regulator, @Period,
                        'CRITICAL', @Score, 70, 1, 1, @RunId)
                """,
                new { Regulator = regulatorCode, Period = periodCode,
                      Score = (decimal)criticalDSIBs.Max(nd => nd.ContagionRiskScore),
                      RunId = computationRunId });
        }

        return nodes;
    }

    public async Task<(IReadOnlyList<ContagionNode> Nodes, IReadOnlyList<ContagionEdge> Edges)>
        GetNetworkGraphAsync(string regulatorCode, string periodCode, CancellationToken ct)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);

        var nodes = (await conn.QueryAsync<ContagionNode>(
            """
            SELECT car.InstitutionId,
                   ISNULL(i.InstitutionName, CAST(car.InstitutionId AS NVARCHAR)) AS InstitutionName,
                   ISNULL(i.LicenseType, 'UNKNOWN') AS InstitutionType,
                   car.TotalOutboundExposure   AS TotalOutbound,
                   car.TotalInboundExposure    AS TotalInbound,
                   CAST(car.EigenvectorCentrality AS float) AS EigenvectorCentrality,
                   CAST(car.BetweennessCentrality AS float) AS BetweennessCentrality,
                   CAST(car.ContagionRiskScore    AS float) AS ContagionRiskScore,
                   car.IsSystemicallyImportant
            FROM   meta.contagion_analysis_results car
            LEFT JOIN institutions i ON i.Id = car.InstitutionId
            WHERE  car.RegulatorCode = @Regulator AND car.PeriodCode = @Period
            ORDER BY car.ContagionRiskScore DESC
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        var edges = (await conn.QueryAsync<ContagionEdge>(
            """
            SELECT LendingInstitutionId, BorrowingInstitutionId,
                   ExposureAmount, ExposureType
            FROM   meta.interbank_exposures
            WHERE  RegulatorCode = @Regulator AND PeriodCode = @Period
            ORDER BY ExposureAmount DESC
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        return (nodes, edges);
    }

    // ── Graph algorithms ──────────────────────────────────────────────────────

    private static double[] ComputeEigenvectorCentrality(double[,] matrix, int n, int iterations)
    {
        var centrality = Enumerable.Repeat(1.0 / n, n).ToArray();

        for (int iter = 0; iter < iterations; iter++)
        {
            var next = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    next[i] += matrix[j, i] * centrality[j];

            var norm = Math.Sqrt(next.Sum(v => v * v));
            if (norm < 1e-12) break;
            for (int i = 0; i < n; i++)
                centrality[i] = next[i] / norm;
        }

        var max = centrality.Length > 0 ? centrality.Max() : 0.0;
        if (max > 0)
            for (int i = 0; i < n; i++)
                centrality[i] /= max;

        return centrality;
    }

    private static double[] ComputeBetweennessCentrality(double[,] matrix, int n)
    {
        var betweenness = new double[n];

        for (int s = 0; s < n; s++)
        {
            var stack  = new Stack<int>();
            var pred   = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
            var sigma  = new double[n];
            var dist   = Enumerable.Repeat(-1, n).ToArray();
            sigma[s]   = 1;
            dist[s]    = 0;

            var queue = new Queue<int>();
            queue.Enqueue(s);

            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                stack.Push(v);
                for (int w = 0; w < n; w++)
                {
                    if (matrix[v, w] <= 0) continue;
                    if (dist[w] < 0)
                    {
                        queue.Enqueue(w);
                        dist[w] = dist[v] + 1;
                    }
                    if (dist[w] == dist[v] + 1)
                    {
                        sigma[w] += sigma[v];
                        pred[w].Add(v);
                    }
                }
            }

            var delta = new double[n];
            while (stack.Count > 0)
            {
                var w = stack.Pop();
                foreach (var v in pred[w])
                {
                    delta[v] += sigma[v] / Math.Max(sigma[w], 1) * (1 + delta[w]);
                    if (w != s) betweenness[w] += delta[w];
                }
            }
        }

        var maxB = betweenness.Length > 0 ? betweenness.Max() : 0.0;
        if (maxB > 0)
            for (int i = 0; i < n; i++)
                betweenness[i] /= maxB;

        return betweenness;
    }

    private sealed record InterbankEdgeRow(
        int LendingInstitutionId, int BorrowingInstitutionId,
        decimal ExposureAmount, string ExposureType);
}
