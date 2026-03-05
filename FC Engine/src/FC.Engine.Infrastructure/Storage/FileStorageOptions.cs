namespace FC.Engine.Infrastructure.Storage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string Provider { get; set; } = "Local";
    public LocalStorageOptions Local { get; set; } = new();

    public class LocalStorageOptions
    {
        public string BasePath { get; set; } = "wwwroot/uploads";
        public string BaseUrl { get; set; } = "/uploads";
    }
}
