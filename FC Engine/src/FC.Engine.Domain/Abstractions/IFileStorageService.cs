namespace FC.Engine.Domain.Abstractions;

public interface IFileStorageService
{
    Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string path, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    string GetPublicUrl(string path);
}
