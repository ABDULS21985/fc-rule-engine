using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FC.Engine.Infrastructure.Export.Adapters;

public class CbnSubmissionAdapter : RegulatorSubmissionAdapterBase
{
    private static readonly ExportFormat[] Formats = [ExportFormat.Excel, ExportFormat.XML];

    public CbnSubmissionAdapter(
        IEnumerable<IExportGenerator> generators,
        ITenantBrandingService brandingService)
        : base(generators, brandingService)
    {
    }

    public override string RegulatorCode => "CBN";
    protected override IReadOnlyList<ExportFormat> SupportedFormats => Formats;

    public override async Task<byte[]> Package(Submission submission, ExportFormat preferredFormat, CancellationToken ct = default)
    {
        // eFASS packaging requires both Excel and XML artefacts in one package.
        var excelBytes = await base.Package(submission, ExportFormat.Excel, ct);
        var xmlBytes = await base.Package(submission, ExportFormat.XML, ct);

        var baseName = BuildBaseFileName(submission);
        var excelName = $"{baseName}.xlsx";
        var xmlName = $"{baseName}.xml";

        var manifest = new CbnEfassManifest
        {
            Regulator = "CBN",
            PackageType = "eFASS",
            SubmissionId = submission.Id,
            ReturnCode = submission.ReturnCode,
            InstitutionCode = submission.Institution?.InstitutionCode ?? string.Empty,
            GeneratedAtUtc = DateTime.UtcNow,
            Files =
            [
                new CbnEfassFile
                {
                    FileName = excelName,
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    Sha256 = ComputeSha256(excelBytes),
                    Size = excelBytes.LongLength
                },
                new CbnEfassFile
                {
                    FileName = xmlName,
                    ContentType = "application/xml",
                    Sha256 = ComputeSha256(xmlBytes),
                    Size = xmlBytes.LongLength
                }
            ]
        };

        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, excelName, excelBytes);
            AddEntry(archive, xmlName, xmlBytes);

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var manifestWriter = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false));
            await manifestWriter.WriteAsync(JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }

        return archiveStream.ToArray();
    }

    public override Task<SubmissionReceipt> Submit(byte[] package, Submission submission, CancellationToken ct = default)
    {
        return Task.FromResult(new SubmissionReceipt
        {
            Success = true,
            Reference = $"CBN-eFASS-{submission.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Message = "eFASS package (Excel + XML + manifest) generated for portal submission.",
            ReceivedAt = DateTime.UtcNow
        });
    }

    private static string BuildBaseFileName(Submission submission)
    {
        var institutionCode = submission.Institution?.InstitutionCode ?? "INST";
        var period = submission.ReturnPeriod is null
            ? DateTime.UtcNow.ToString("yyyyMM", CultureInfo.InvariantCulture)
            : submission.ReturnPeriod.Month is >= 1 and <= 12
                ? $"{submission.ReturnPeriod.Year}{submission.ReturnPeriod.Month:00}"
                : submission.ReturnPeriod.Year.ToString(CultureInfo.InvariantCulture);

        return $"{Sanitize(submission.ReturnCode)}_{Sanitize(institutionCode)}_{period}_{submission.Id}";
    }

    private static string Sanitize(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "NA";
        }

        var clean = new string(source
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray());
        return clean.Length == 0 ? "NA" : clean;
    }

    private static string ComputeSha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private static void AddEntry(ZipArchive archive, string fileName, byte[] content)
    {
        var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(content, 0, content.Length);
    }

    private sealed class CbnEfassManifest
    {
        public string Regulator { get; set; } = "CBN";
        public string PackageType { get; set; } = "eFASS";
        public int SubmissionId { get; set; }
        public string ReturnCode { get; set; } = string.Empty;
        public string InstitutionCode { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; }
        public List<CbnEfassFile> Files { get; set; } = [];
    }

    private sealed class CbnEfassFile
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
