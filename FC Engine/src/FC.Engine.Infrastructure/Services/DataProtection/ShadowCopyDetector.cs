using System.Security.Cryptography;
using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed record DetectedShadowCopy(
    Guid SourceDataSourceId,
    Guid TargetDataSourceId,
    string SourceName,
    string TargetName,
    string SourceTable,
    string TargetTable,
    string DetectionType,
    string Fingerprint,
    decimal SimilarityScore,
    bool IsLegitimate,
    bool RequiresReview,
    string EvidenceJson);

public sealed class ShadowCopyDetector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SchemaFingerprintService _fingerprints;

    public ShadowCopyDetector(SchemaFingerprintService fingerprints) => _fingerprints = fingerprints;

    public IReadOnlyList<DetectedShadowCopy> Detect(
        IReadOnlyList<DataSourceRegistration> sources,
        IReadOnlyList<DataPipelineDefinition> pipelines,
        ContinuousDspmOptions options)
    {
        var results = new List<DetectedShadowCopy>();

        foreach (var tenantGroup in sources.GroupBy(s => s.TenantId))
        {
            var sourceTables = tenantGroup
                .Select(source => new
                {
                    Source = source,
                    Schema = DeserializeSchema(source.SchemaJson),
                    Tables = DeserializeSchema(source.SchemaJson).Tables
                })
                .ToList();

            for (var i = 0; i < sourceTables.Count; i++)
            {
                for (var j = i + 1; j < sourceTables.Count; j++)
                {
                    var left = sourceTables[i];
                    var right = sourceTables[j];
                    var lineageTracked = HasLineage(pipelines, left.Source.Id, right.Source.Id);

                    foreach (var leftTable in left.Tables)
                    {
                        foreach (var rightTable in right.Tables)
                        {
                            var leftFingerprint = _fingerprints.ComputeFingerprint(leftTable);
                            var rightFingerprint = _fingerprints.ComputeFingerprint(rightTable);

                            if (leftFingerprint == rightFingerprint)
                            {
                                results.Add(new DetectedShadowCopy(
                                    left.Source.Id,
                                    right.Source.Id,
                                    left.Source.SourceName,
                                    right.Source.SourceName,
                                    leftTable.TableName,
                                    rightTable.TableName,
                                    "fingerprint_match",
                                    leftFingerprint,
                                    100m,
                                    lineageTracked,
                                    false,
                                    JsonSerializer.Serialize(new
                                    {
                                        leftTable = leftTable.TableName,
                                        rightTable = rightTable.TableName,
                                        lineageTracked
                                    }, JsonOptions)));
                                continue;
                            }

                            var nameDistance = _fingerprints.EditDistance(leftTable.TableName, rightTable.TableName);
                            if (nameDistance < options.NearDuplicateEditDistance
                                && leftTable.Columns.Count == rightTable.Columns.Count)
                            {
                                results.Add(new DetectedShadowCopy(
                                    left.Source.Id,
                                    right.Source.Id,
                                    left.Source.SourceName,
                                    right.Source.SourceName,
                                    leftTable.TableName,
                                    rightTable.TableName,
                                    "near_match",
                                    leftFingerprint,
                                    _fingerprints.CalculateSimilarity(leftTable, rightTable),
                                    lineageTracked,
                                    true,
                                    JsonSerializer.Serialize(new
                                    {
                                        leftTable = leftTable.TableName,
                                        rightTable = rightTable.TableName,
                                        editDistance = nameDistance,
                                        lineageTracked
                                    }, JsonOptions)));
                            }
                        }
                    }
                }
            }

            foreach (var source in tenantGroup.Where(s => s.SourceType.Equals("hdfs", StringComparison.OrdinalIgnoreCase)
                                                          && !string.IsNullOrWhiteSpace(s.FilesystemRootPath)
                                                          && Directory.Exists(s.FilesystemRootPath)))
            {
                var files = Directory.EnumerateFiles(source.FilesystemRootPath!, "*", SearchOption.AllDirectories)
                    .Take(Math.Max(1, options.HdfsMaxFilesPerSource))
                    .Select(path => new FileInfo(path))
                    .Where(info => info.Exists)
                    .Select(info => new
                    {
                        info.FullName,
                        info.Length,
                        Hash = ComputeFileHash(info.FullName)
                    })
                    .GroupBy(x => $"{x.Length}:{x.Hash}", StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1);

                foreach (var duplicateGroup in files)
                {
                    var ordered = duplicateGroup.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList();
                    for (var i = 0; i < ordered.Count - 1; i++)
                    {
                        results.Add(new DetectedShadowCopy(
                            source.Id,
                            source.Id,
                            source.SourceName,
                            source.SourceName,
                            ordered[i].FullName,
                            ordered[i + 1].FullName,
                            "hdfs_duplicate",
                            duplicateGroup.Key,
                            100m,
                            false,
                            false,
                            JsonSerializer.Serialize(new { duplicateGroup = ordered.Select(x => x.FullName).ToArray() }, JsonOptions)));
                    }
                }
            }
        }

        return results;
    }

    private static DataSourceSchema DeserializeSchema(string payload)
        => JsonSerializer.Deserialize<DataSourceSchema>(payload, JsonOptions) ?? new DataSourceSchema();

    private static bool HasLineage(IEnumerable<DataPipelineDefinition> pipelines, Guid leftSourceId, Guid rightSourceId)
        => pipelines.Any(p => (p.SourceDataSourceId == leftSourceId && p.TargetDataSourceId == rightSourceId)
                              || (p.SourceDataSourceId == rightSourceId && p.TargetDataSourceId == leftSourceId));

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
