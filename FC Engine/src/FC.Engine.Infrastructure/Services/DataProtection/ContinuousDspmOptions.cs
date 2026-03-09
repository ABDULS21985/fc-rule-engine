namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class ContinuousDspmOptions
{
    public const string SectionName = "ContinuousDspm";

    public int AtRestIntervalHours { get; set; } = 24;
    public string ShadowScanCron { get; set; } = "0 2 * * 0";
    public int NearDuplicateEditDistance { get; set; } = 3;
    public int HdfsMaxFilesPerSource { get; set; } = 2000;
}
