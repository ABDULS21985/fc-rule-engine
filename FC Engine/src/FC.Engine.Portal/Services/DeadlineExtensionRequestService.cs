using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Portal.Services;

public class DeadlineExtensionRequestService
{
    private readonly IInstitutionRepository _institutionRepository;
    private readonly IFileStorageService _fileStorage;

    public DeadlineExtensionRequestService(
        IInstitutionRepository institutionRepository,
        IFileStorageService fileStorage)
    {
        _institutionRepository = institutionRepository;
        _fileStorage = fileStorage;
    }

    public async Task SubmitRequest(
        int institutionId,
        DeadlineExtensionRequestInput input,
        CancellationToken ct = default)
    {
        var institution = await _institutionRepository.GetById(institutionId, ct)
            ?? throw new InvalidOperationException("Institution was not found.");

        if (string.IsNullOrWhiteSpace(input.ReturnCode) || string.IsNullOrWhiteSpace(input.TemplateName))
        {
            throw new InvalidOperationException("Return information is incomplete.");
        }

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            throw new InvalidOperationException("Please provide a reason for the extension request.");
        }

        var settings = InstitutionSettingsState.Deserialize(institution.SettingsJson);
        var record = new DeadlineExtensionRequestRecord
        {
            InstitutionId = institutionId,
            RequestedByUserId = input.RequestedByUserId,
            RequestedByDisplayName = string.IsNullOrWhiteSpace(input.RequestedByDisplayName)
                ? "Institution user"
                : input.RequestedByDisplayName.Trim(),
            ReturnCode = input.ReturnCode.Trim(),
            TemplateName = input.TemplateName.Trim(),
            ModuleCode = input.ModuleCode?.Trim(),
            PeriodLabel = input.PeriodLabel?.Trim(),
            DueDate = input.DueDate,
            RequestedUntil = input.RequestedUntil,
            Reason = input.Reason.Trim(),
            Status = "Pending",
            RequestedAt = DateTime.UtcNow
        };

        foreach (var attachment in input.Attachments)
        {
            if (attachment.Content.Length == 0)
            {
                continue;
            }

            var safeName = SanitizeFileName(attachment.FileName);
            var storagePath = $"institutions/{institution.TenantId}/extensions/{DateTime.UtcNow:yyyyMM}/{record.Id}-{safeName}";
            await using var stream = new MemoryStream(attachment.Content, writable: false);
            var publicUrl = await _fileStorage.UploadAsync(storagePath, stream, attachment.ContentType, ct);

            record.AttachmentNames.Add(attachment.FileName);
            record.AttachmentUrls.Add(publicUrl);
        }

        settings.ExtensionRequests.Add(record);
        institution.SettingsJson = InstitutionSettingsState.Serialize(settings);
        await _institutionRepository.Update(institution, ct);
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "attachment" : fileName.Trim();
        return Regex.Replace(name, @"[^A-Za-z0-9._-]", "-");
    }
}

public class DeadlineExtensionRequestInput
{
    public int? RequestedByUserId { get; set; }
    public string RequestedByDisplayName { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string? ModuleCode { get; set; }
    public string? PeriodLabel { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? RequestedUntil { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<DeadlineExtensionAttachmentUpload> Attachments { get; set; } = new();
}

public class DeadlineExtensionAttachmentUpload
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
