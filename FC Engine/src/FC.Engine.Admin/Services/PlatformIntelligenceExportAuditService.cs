using System.Text.Json;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public sealed class PlatformIntelligenceExportAuditService
{
    private readonly MetadataDbContext _db;

    public PlatformIntelligenceExportAuditService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlatformIntelligenceExportAuditRow>> GetRecentExportsAsync(
        string? area,
        string? format,
        string? action,
        int take = 25,
        CancellationToken ct = default)
    {
        var normalizedArea = PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(area);
        var normalizedFormat = PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(format);
        var normalizedAction = PlatformIntelligenceApiRequestMapper.NormalizeOptionalFilter(action);
        var size = Math.Clamp(take, 1, 100);

        var auditRows = await _db.AuditLog
            .AsNoTracking()
            .Where(x => x.EntityType == "PlatformIntelligence" && x.Action.EndsWith("Exported"))
            .OrderByDescending(x => x.PerformedAt)
            .Take(Math.Max(size * 4, 200))
            .ToListAsync(ct);

        return auditRows
            .Select(MapRow)
            .Where(x => x is not null)
            .Select(x => x!)
            .Where(x => normalizedArea is null || x.Area.Equals(normalizedArea, StringComparison.OrdinalIgnoreCase))
            .Where(x => normalizedFormat is null || x.Format.Equals(normalizedFormat, StringComparison.OrdinalIgnoreCase))
            .Where(x => normalizedAction is null || x.Action.Equals(normalizedAction, StringComparison.OrdinalIgnoreCase))
            .Take(size)
            .ToList();
    }

    private static PlatformIntelligenceExportAuditRow? MapRow(AuditLogEntry entry)
    {
        var payload = ParsePayload(entry.NewValues);
        var area = payload.Area ?? ResolveArea(entry.Action);
        var format = payload.Format ?? ResolveFormat(entry.Action, payload.FileName);

        if (string.IsNullOrWhiteSpace(area) || string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        return new PlatformIntelligenceExportAuditRow
        {
            Action = entry.Action,
            Area = area,
            Format = format,
            FileName = payload.FileName ?? BuildFallbackFileName(entry.Action, format),
            Lens = payload.Lens,
            InstitutionId = payload.InstitutionId,
            SizeBytes = payload.SizeBytes,
            PerformedBy = entry.PerformedBy,
            PerformedAt = entry.PerformedAt
        };
    }

    private static PlatformIntelligenceExportAuditPayload ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PlatformIntelligenceExportAuditPayload();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new PlatformIntelligenceExportAuditPayload
            {
                Area = TryReadString(root, "Area"),
                Format = TryReadString(root, "Format"),
                FileName = TryReadString(root, "FileName"),
                Lens = TryReadString(root, "Lens"),
                InstitutionId = TryReadInt32(root, "InstitutionId"),
                SizeBytes = TryReadInt64(root, "SizeBytes")
            };
        }
        catch (JsonException)
        {
            return new PlatformIntelligenceExportAuditPayload();
        }
    }

    private static string? TryReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString()?.Trim(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int? TryReadInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static long? TryReadInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
        {
            return number;
        }

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static string? ResolveArea(string action) => action switch
    {
        "OverviewExported" => "Overview",
        "DashboardBriefingPackExported" => "Dashboards",
        "KnowledgeDossierExported" => "Knowledge",
        "CapitalPackExported" => "Capital",
        "SanctionsPackExported" => "Sanctions",
        "ResiliencePackExported" => "Resilience",
        "ModelRiskPackExported" => "Model Risk",
        "BundleExported" => "Bundle",
        _ => null
    };

    private static string? ResolveFormat(string action, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension.TrimStart('.').ToLowerInvariant();
            }
        }

        return action switch
        {
            "BundleExported" => "zip",
            _ => null
        };
    }

    private static string BuildFallbackFileName(string action, string format) => action switch
    {
        "BundleExported" => $"platform-intelligence-bundle.{format}",
        _ => $"platform-intelligence-{action.Replace("Exported", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}.{format}"
    };

    private sealed class PlatformIntelligenceExportAuditPayload
    {
        public string? Area { get; init; }
        public string? Format { get; init; }
        public string? FileName { get; init; }
        public string? Lens { get; init; }
        public int? InstitutionId { get; init; }
        public long? SizeBytes { get; init; }
    }
}

public sealed class PlatformIntelligenceExportAuditRow
{
    public string Action { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? Lens { get; set; }
    public int? InstitutionId { get; set; }
    public long? SizeBytes { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
}
