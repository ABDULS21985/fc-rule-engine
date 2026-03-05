using FC.Engine.Domain.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Storage;

public class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageOptions _options;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IWebHostEnvironment environment,
        IOptions<FileStorageOptions> options,
        ILogger<LocalFileStorageService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizeRelativePath(path);
        var fullPath = ResolvePhysicalPath(normalizedPath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);

        _logger.LogDebug("Stored tenant asset at {Path}", fullPath);
        return GetPublicUrl(normalizedPath);
    }

    public Task<Stream> DownloadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizeRelativePath(path);
        var fullPath = ResolvePhysicalPath(normalizedPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Asset not found", fullPath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizeRelativePath(path);
        var fullPath = ResolvePhysicalPath(normalizedPath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public string GetPublicUrl(string path)
    {
        var normalizedPath = NormalizeRelativePath(path);
        var baseUrl = string.IsNullOrWhiteSpace(_options.Local.BaseUrl)
            ? "/uploads"
            : _options.Local.BaseUrl.TrimEnd('/');

        return $"{baseUrl}/{normalizedPath}";
    }

    private string ResolvePhysicalPath(string normalizedPath)
    {
        var basePath = string.IsNullOrWhiteSpace(_options.Local.BasePath)
            ? "wwwroot/uploads"
            : _options.Local.BasePath;

        var root = _environment.ContentRootPath;
        var combinedBase = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(root, basePath);

        return Path.Combine(combinedBase, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path
            .Replace('\\', '/')
            .Trim('/');

        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage path");
        }

        return normalized;
    }
}
