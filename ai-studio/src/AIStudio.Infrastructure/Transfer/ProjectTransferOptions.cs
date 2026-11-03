namespace AIStudio.Infrastructure.Transfer;

/// <summary>
/// Project package configuration. <see cref="RootPath"/> is always resolved relative to
/// the approved asset root; an absolute path, a drive, or a parent segment is rejected so
/// an export can never be written outside the approved root.
/// </summary>
public sealed class ProjectTransferOptions
{
    public const string SectionName = "ProjectTransfer";

    public string RootPath { get; set; } = "exports";

    public long MaxFileBytes { get; set; } = 512L * 1024 * 1024;

    public long MaxPackageBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    public long MaxManifestBytes { get; set; } = 16L * 1024 * 1024;

    public int MaxFileCount { get; set; } = 10_000;
}
