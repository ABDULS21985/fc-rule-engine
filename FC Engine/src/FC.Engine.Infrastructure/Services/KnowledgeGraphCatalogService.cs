using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class KnowledgeGraphCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MetadataDbContext _db;

    public KnowledgeGraphCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<KnowledgeGraphCatalogMaterializationResult> MaterializeAsync(
        KnowledgeGraphCatalogMaterializationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var nodes = BuildNodes(request, materializedAt);
        var edges = BuildEdges(request, materializedAt);

        await ClearExistingCatalogAsync(ct);

        _db.KnowledgeGraphNodes.AddRange(nodes);
        _db.KnowledgeGraphEdges.AddRange(edges);
        await _db.SaveChangesAsync(ct);

        return new KnowledgeGraphCatalogMaterializationResult
        {
            NodeCount = nodes.Count,
            EdgeCount = edges.Count,
            MaterializedAt = materializedAt,
            NodeTypes = nodes
                .GroupBy(x => x.NodeType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KnowledgeGraphCatalogTypeCount(x.Key, x.Count()))
                .ToList(),
            EdgeTypes = edges
                .GroupBy(x => x.EdgeType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KnowledgeGraphCatalogTypeCount(x.Key, x.Count()))
                .ToList()
        };
    }

    public async Task<KnowledgeGraphCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var nodes = await _db.KnowledgeGraphNodes
            .AsNoTracking()
            .OrderBy(x => x.NodeType)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);

        var edges = await _db.KnowledgeGraphEdges
            .AsNoTracking()
            .OrderBy(x => x.EdgeType)
            .ThenBy(x => x.SourceNodeKey)
            .ThenBy(x => x.TargetNodeKey)
            .ToListAsync(ct);

        var materializedAt = nodes.Select(x => (DateTime?)x.MaterializedAt)
            .Concat(edges.Select(x => (DateTime?)x.MaterializedAt))
            .OrderByDescending(x => x)
            .FirstOrDefault();

        return new KnowledgeGraphCatalogState
        {
            MaterializedAt = materializedAt,
            NodeCount = nodes.Count,
            EdgeCount = edges.Count,
            NodeTypes = nodes
                .GroupBy(x => x.NodeType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KnowledgeGraphCatalogTypeCount(x.Key, x.Count()))
                .ToList(),
            EdgeTypes = edges
                .GroupBy(x => x.EdgeType, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new KnowledgeGraphCatalogTypeCount(x.Key, x.Count()))
                .ToList(),
            Nodes = nodes
                .Select(x => new KnowledgeGraphCatalogNodeState
                {
                    NodeKey = x.NodeKey,
                    NodeType = x.NodeType,
                    DisplayName = x.DisplayName,
                    Code = x.Code,
                    RegulatorCode = x.RegulatorCode,
                    SourceReference = x.SourceReference,
                    MetadataJson = x.MetadataJson,
                    MaterializedAt = x.MaterializedAt
                })
                .ToList(),
            Edges = edges
                .Select(x => new KnowledgeGraphCatalogEdgeState
                {
                    EdgeKey = x.EdgeKey,
                    EdgeType = x.EdgeType,
                    SourceNodeKey = x.SourceNodeKey,
                    TargetNodeKey = x.TargetNodeKey,
                    RegulatorCode = x.RegulatorCode,
                    SourceReference = x.SourceReference,
                    Weight = x.Weight,
                    MetadataJson = x.MetadataJson,
                    MaterializedAt = x.MaterializedAt
                })
                .ToList()
        };
    }

    private async Task EnsureStoreAsync(CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
        {
            return;
        }

        const string sql = """
            IF SCHEMA_ID(N'meta') IS NULL
                EXEC(N'CREATE SCHEMA [meta]');

            IF OBJECT_ID(N'[meta].[kg_nodes]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[kg_nodes]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [NodeKey] NVARCHAR(200) NOT NULL,
                    [NodeType] NVARCHAR(60) NOT NULL,
                    [DisplayName] NVARCHAR(240) NOT NULL,
                    [Code] NVARCHAR(120) NULL,
                    [RegulatorCode] NVARCHAR(40) NULL,
                    [SourceReference] NVARCHAR(160) NULL,
                    [MetadataJson] NVARCHAR(MAX) NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_kg_nodes_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_kg_nodes_NodeKey] ON [meta].[kg_nodes]([NodeKey]);
                CREATE INDEX [IX_kg_nodes_NodeType] ON [meta].[kg_nodes]([NodeType]);
                CREATE INDEX [IX_kg_nodes_RegulatorCode] ON [meta].[kg_nodes]([RegulatorCode]);
            END;

            IF OBJECT_ID(N'[meta].[kg_edges]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[kg_edges]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [EdgeKey] NVARCHAR(320) NOT NULL,
                    [EdgeType] NVARCHAR(80) NOT NULL,
                    [SourceNodeKey] NVARCHAR(200) NOT NULL,
                    [TargetNodeKey] NVARCHAR(200) NOT NULL,
                    [RegulatorCode] NVARCHAR(40) NULL,
                    [SourceReference] NVARCHAR(160) NULL,
                    [Weight] INT NOT NULL CONSTRAINT [DF_kg_edges_Weight] DEFAULT 1,
                    [MetadataJson] NVARCHAR(MAX) NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_kg_edges_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_kg_edges_EdgeKey] ON [meta].[kg_edges]([EdgeKey]);
                CREATE INDEX [IX_kg_edges_EdgeType] ON [meta].[kg_edges]([EdgeType]);
                CREATE INDEX [IX_kg_edges_SourceNodeKey] ON [meta].[kg_edges]([SourceNodeKey]);
                CREATE INDEX [IX_kg_edges_TargetNodeKey] ON [meta].[kg_edges]([TargetNodeKey]);
                CREATE INDEX [IX_kg_edges_RegulatorCode] ON [meta].[kg_edges]([RegulatorCode]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM [meta].[kg_edges];
                DELETE FROM [meta].[kg_nodes];
                """,
                ct);
            return;
        }

        var existingEdges = await _db.KnowledgeGraphEdges.ToListAsync(ct);
        var existingNodes = await _db.KnowledgeGraphNodes.ToListAsync(ct);
        if (existingEdges.Count > 0)
        {
            _db.KnowledgeGraphEdges.RemoveRange(existingEdges);
        }

        if (existingNodes.Count > 0)
        {
            _db.KnowledgeGraphNodes.RemoveRange(existingNodes);
        }

        if (existingEdges.Count > 0 || existingNodes.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static List<KnowledgeGraphNode> BuildNodes(
        KnowledgeGraphCatalogMaterializationRequest request,
        DateTime materializedAt)
    {
        var nodes = new Dictionary<string, KnowledgeGraphNode>(StringComparer.OrdinalIgnoreCase);

        void AddNode(
            string nodeType,
            string rawKey,
            string displayName,
            string? code = null,
            string? regulatorCode = null,
            string? sourceReference = null,
            object? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            var nodeKey = BuildNodeKey(nodeType, rawKey);
            if (nodes.ContainsKey(nodeKey))
            {
                return;
            }

            nodes[nodeKey] = new KnowledgeGraphNode
            {
                NodeKey = nodeKey,
                NodeType = nodeType,
                DisplayName = displayName.Trim(),
                Code = code?.Trim(),
                RegulatorCode = regulatorCode?.Trim(),
                SourceReference = sourceReference?.Trim(),
                MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions),
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            };
        }

        foreach (var regulator in request.Regulators)
        {
            AddNode(
                "Regulator",
                regulator.RegulatorCode,
                regulator.DisplayName,
                regulator.RegulatorCode,
                regulator.RegulatorCode,
                metadata: new
                {
                    regulator.ModuleCount,
                    regulator.RequirementCount,
                    regulator.InstitutionCount
                });
        }

        foreach (var requirement in request.Requirements)
        {
            AddNode(
                "Requirement",
                requirement.RegulatoryReference,
                requirement.RegulatoryReference,
                requirement.RegulatoryReference,
                requirement.RegulatorCode,
                requirement.RegulatoryReference,
                new
                {
                    requirement.RegulationFamily,
                    requirement.ModuleCode,
                    requirement.FieldCount,
                    requirement.InstitutionCount,
                    requirement.NextDeadline
                });

            foreach (var returnCode in requirement.FiledViaReturns)
            {
                AddNode(
                    "Return",
                    returnCode,
                    returnCode,
                    returnCode,
                    requirement.RegulatorCode,
                    requirement.RegulatoryReference,
                    new
                    {
                        requirement.ModuleCode,
                        requirement.FrequencyProfile
                    });
            }
        }

        foreach (var lineage in request.Lineage)
        {
            AddNode(
                "Module",
                lineage.ModuleCode,
                lineage.ModuleCode,
                lineage.ModuleCode,
                lineage.RegulatorCode,
                lineage.RegulatoryReference);
            AddNode(
                "Return",
                lineage.ReturnCode,
                lineage.ReturnCode,
                lineage.ReturnCode,
                lineage.RegulatorCode,
                lineage.RegulatoryReference,
                new
                {
                    lineage.TemplateName
                });
            AddNode(
                "Field",
                $"{lineage.ReturnCode}:{lineage.FieldCode}",
                lineage.FieldName,
                lineage.FieldCode,
                lineage.RegulatorCode,
                lineage.RegulatoryReference,
                new
                {
                    lineage.ModuleCode,
                    lineage.ReturnCode,
                    lineage.TemplateName
                });
        }

        foreach (var obligation in request.Obligations)
        {
            AddNode(
                "Licence",
                obligation.LicenceType,
                obligation.LicenceType,
                obligation.LicenceType,
                obligation.RegulatorCode,
                metadata: new
                {
                    obligation.ModuleCode,
                    obligation.ReturnCode,
                    obligation.Frequency,
                    obligation.NextDeadline
                });
        }

        foreach (var institution in request.InstitutionObligations)
        {
            AddNode(
                "Institution",
                institution.InstitutionKey,
                institution.InstitutionName,
                institution.InstitutionKey,
                institution.RegulatorCode,
                metadata: new
                {
                    institution.LicenceType,
                    institution.ReturnCode,
                    institution.Status,
                    institution.NextDeadline
                });
        }

        return nodes.Values.ToList();
    }

    private static List<KnowledgeGraphEdge> BuildEdges(
        KnowledgeGraphCatalogMaterializationRequest request,
        DateTime materializedAt)
    {
        var edges = new Dictionary<string, KnowledgeGraphEdge>(StringComparer.OrdinalIgnoreCase);

        void AddEdge(
            string edgeType,
            string sourceNodeKey,
            string targetNodeKey,
            string? regulatorCode = null,
            string? sourceReference = null,
            int weight = 1,
            object? metadata = null)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeKey) || string.IsNullOrWhiteSpace(targetNodeKey))
            {
                return;
            }

            var edgeKey = $"{NormalizeKey(edgeType)}|{sourceNodeKey}|{targetNodeKey}";
            if (edges.TryGetValue(edgeKey, out var existing))
            {
                existing.Weight += Math.Max(weight, 1);
                return;
            }

            edges[edgeKey] = new KnowledgeGraphEdge
            {
                EdgeKey = edgeKey,
                EdgeType = edgeType,
                SourceNodeKey = sourceNodeKey,
                TargetNodeKey = targetNodeKey,
                RegulatorCode = regulatorCode?.Trim(),
                SourceReference = sourceReference?.Trim(),
                Weight = Math.Max(weight, 1),
                MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOptions),
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            };
        }

        foreach (var regulator in request.Regulators)
        {
            foreach (var moduleCode in regulator.ModuleCodes)
            {
                AddEdge(
                    "OVERSEES_MODULE",
                    BuildNodeKey("Regulator", regulator.RegulatorCode),
                    BuildNodeKey("Module", moduleCode),
                    regulator.RegulatorCode);
            }
        }

        foreach (var requirement in request.Requirements)
        {
            var requirementNodeKey = BuildNodeKey("Requirement", requirement.RegulatoryReference);

            AddEdge(
                "ISSUES_REQUIREMENT",
                BuildNodeKey("Regulator", requirement.RegulatorCode),
                requirementNodeKey,
                requirement.RegulatorCode,
                requirement.RegulatoryReference,
                metadata: new
                {
                    requirement.RegulationFamily
                });

            foreach (var returnCode in requirement.FiledViaReturns)
            {
                AddEdge(
                    "FILED_VIA",
                    requirementNodeKey,
                    BuildNodeKey("Return", returnCode),
                    requirement.RegulatorCode,
                    requirement.RegulatoryReference,
                    metadata: new
                    {
                        requirement.ModuleCode,
                        requirement.FrequencyProfile
                    });
            }
        }

        foreach (var lineage in request.Lineage)
        {
            var moduleNodeKey = BuildNodeKey("Module", lineage.ModuleCode);
            var returnNodeKey = BuildNodeKey("Return", lineage.ReturnCode);
            var fieldNodeKey = BuildNodeKey("Field", $"{lineage.ReturnCode}:{lineage.FieldCode}");
            var requirementNodeKey = BuildNodeKey("Requirement", lineage.RegulatoryReference);

            AddEdge(
                "IMPLEMENTS_RETURN",
                moduleNodeKey,
                returnNodeKey,
                lineage.RegulatorCode,
                lineage.RegulatoryReference);
            AddEdge(
                "CAPTURES_FIELD",
                returnNodeKey,
                fieldNodeKey,
                lineage.RegulatorCode,
                lineage.RegulatoryReference,
                metadata: new
                {
                    lineage.TemplateName
                });
            AddEdge(
                "CAPTURED_BY",
                requirementNodeKey,
                fieldNodeKey,
                lineage.RegulatorCode,
                lineage.RegulatoryReference,
                metadata: new
                {
                    lineage.TemplateName,
                    lineage.FieldCode
                });
        }

        foreach (var obligation in request.Obligations)
        {
            AddEdge(
                "MUST_FILE",
                BuildNodeKey("Licence", obligation.LicenceType),
                BuildNodeKey("Return", obligation.ReturnCode),
                obligation.RegulatorCode,
                metadata: new
                {
                    obligation.ModuleCode,
                    obligation.Frequency,
                    obligation.NextDeadline
                });
        }

        foreach (var institution in request.InstitutionObligations)
        {
            var institutionNodeKey = BuildNodeKey("Institution", institution.InstitutionKey);
            var licenceNodeKey = BuildNodeKey("Licence", institution.LicenceType);
            var returnNodeKey = BuildNodeKey("Return", institution.ReturnCode);

            AddEdge(
                "HOLDS_LICENCE",
                institutionNodeKey,
                licenceNodeKey,
                institution.RegulatorCode,
                metadata: new
                {
                    institution.Status
                });
            AddEdge(
                "HAS_OBLIGATION",
                institutionNodeKey,
                returnNodeKey,
                institution.RegulatorCode,
                metadata: new
                {
                    institution.Status,
                    institution.NextDeadline
                });
        }

        return edges.Values.ToList();
    }

    private static string BuildNodeKey(string nodeType, string rawKey) =>
        $"{NormalizeKey(nodeType)}:{NormalizeKey(rawKey)}";

    private static string NormalizeKey(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var chars = trimmed
            .ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();

        return string.Join(
            string.Empty,
            new string(chars)
                .Split('_', StringSplitOptions.RemoveEmptyEntries));
    }
}

public sealed class KnowledgeGraphCatalogMaterializationRequest
{
    public List<KnowledgeGraphCatalogRegulatorInput> Regulators { get; init; } = [];
    public List<KnowledgeGraphCatalogRequirementInput> Requirements { get; init; } = [];
    public List<KnowledgeGraphCatalogLineageInput> Lineage { get; init; } = [];
    public List<KnowledgeGraphCatalogObligationInput> Obligations { get; init; } = [];
    public List<KnowledgeGraphCatalogInstitutionObligationInput> InstitutionObligations { get; init; } = [];
}

public sealed class KnowledgeGraphCatalogRegulatorInput
{
    public string RegulatorCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int ModuleCount { get; init; }
    public int RequirementCount { get; init; }
    public int InstitutionCount { get; init; }
    public List<string> ModuleCodes { get; init; } = [];
}

public sealed class KnowledgeGraphCatalogRequirementInput
{
    public string RegulatoryReference { get; init; } = string.Empty;
    public string RegulationFamily { get; init; } = string.Empty;
    public string RegulatorCode { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public List<string> FiledViaReturns { get; init; } = [];
    public int InstitutionCount { get; init; }
    public int FieldCount { get; init; }
    public string FrequencyProfile { get; init; } = string.Empty;
    public DateTime NextDeadline { get; init; }
}

public sealed class KnowledgeGraphCatalogLineageInput
{
    public string RegulatorCode { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public string ReturnCode { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public string FieldCode { get; init; } = string.Empty;
    public string RegulatoryReference { get; init; } = string.Empty;
}

public sealed class KnowledgeGraphCatalogObligationInput
{
    public string LicenceType { get; init; } = string.Empty;
    public string RegulatorCode { get; init; } = string.Empty;
    public string ModuleCode { get; init; } = string.Empty;
    public string ReturnCode { get; init; } = string.Empty;
    public string Frequency { get; init; } = string.Empty;
    public DateTime NextDeadline { get; init; }
}

public sealed class KnowledgeGraphCatalogInstitutionObligationInput
{
    public string InstitutionKey { get; init; } = string.Empty;
    public string InstitutionName { get; init; } = string.Empty;
    public string LicenceType { get; init; } = string.Empty;
    public string RegulatorCode { get; init; } = string.Empty;
    public string ReturnCode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime NextDeadline { get; init; }
}

public sealed class KnowledgeGraphCatalogMaterializationResult
{
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public DateTime MaterializedAt { get; init; }
    public List<KnowledgeGraphCatalogTypeCount> NodeTypes { get; init; } = [];
    public List<KnowledgeGraphCatalogTypeCount> EdgeTypes { get; init; } = [];
}

public sealed class KnowledgeGraphCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public List<KnowledgeGraphCatalogTypeCount> NodeTypes { get; init; } = [];
    public List<KnowledgeGraphCatalogTypeCount> EdgeTypes { get; init; } = [];
    public List<KnowledgeGraphCatalogNodeState> Nodes { get; init; } = [];
    public List<KnowledgeGraphCatalogEdgeState> Edges { get; init; } = [];
}

public sealed class KnowledgeGraphCatalogNodeState
{
    public string NodeKey { get; init; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? RegulatorCode { get; init; }
    public string? SourceReference { get; init; }
    public string? MetadataJson { get; init; }
    public DateTime MaterializedAt { get; init; }
}

public sealed class KnowledgeGraphCatalogEdgeState
{
    public string EdgeKey { get; init; } = string.Empty;
    public string EdgeType { get; init; } = string.Empty;
    public string SourceNodeKey { get; init; } = string.Empty;
    public string TargetNodeKey { get; init; } = string.Empty;
    public string? RegulatorCode { get; init; }
    public string? SourceReference { get; init; }
    public int Weight { get; init; }
    public string? MetadataJson { get; init; }
    public DateTime MaterializedAt { get; init; }
}

public sealed record KnowledgeGraphCatalogTypeCount(string Type, int Count);
