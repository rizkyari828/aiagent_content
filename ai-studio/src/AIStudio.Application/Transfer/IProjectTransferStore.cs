namespace AIStudio.Application.Transfer;

/// <summary>
/// Package and approved-root file boundary. Implementations are responsible for
/// containment, symlink refusal, atomic writes, no-overwrite imports, hashing, and the
/// configured size bounds; workflows never see a host absolute path.
/// </summary>
public interface IProjectTransferStore
{
    ProjectTransferLimits Limits { get; }

    /// <summary>
    /// Copies one approved-root file into <paramref name="destination"/> and returns the
    /// bytes/size that were actually read. The caller compares them with the persisted
    /// metadata hash before writing the manifest.
    /// </summary>
    Task<ProjectAssetCopy> CopyAssetAsync(
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes one imported file under the approved root. An existing target is never
    /// overwritten; the bytes are hashed while writing and must match the manifest.
    /// </summary>
    Task<ProjectWrittenAsset> WriteAssetAsync(
        string relativePath,
        Stream source,
        long expectedByteSize,
        string expectedSha256,
        CancellationToken cancellationToken);

    /// <summary>Best-effort removal of a file this import wrote.</summary>
    void DeleteAsset(string relativePath);

    /// <summary>
    /// Creates one package atomically: the body writes a ZIP to a temporary stream, then
    /// the store hashes it, enforces the package bound, and promotes it to a final name.
    /// </summary>
    Task<ProjectPackageInfo> WritePackageAsync(
        Guid contentProjectId,
        DateTimeOffset exportedAtUtc,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken);

    /// <summary>Lists packages for one project without opening their contents.</summary>
    IReadOnlyList<ProjectPackageInfo> ListPackages(Guid contentProjectId);

    /// <summary>
    /// Stages an uploaded package in a temporary location, enforcing the package bound,
    /// and exposes a read-only stream for the duration of <paramref name="readAsync"/>.
    /// The temporary file is always removed afterwards.
    /// </summary>
    Task<T> WithStagedPackageAsync<T>(
        Stream source,
        Func<Stream, CancellationToken, Task<T>> readAsync,
        CancellationToken cancellationToken);
}

public sealed record ProjectAssetCopy(string RelativePath, long ByteSize, string Sha256);

public sealed record ProjectWrittenAsset(string RelativePath, long ByteSize, string Sha256);

/// <summary>
/// Filesystem metadata for one package. <see cref="Sha256"/> is only known when the
/// store just wrote the package; the list surface deliberately does not read contents.
/// </summary>
public sealed record ProjectPackageInfo(
    string FileName,
    long ByteSize,
    DateTimeOffset CreatedAtUtc,
    string? Sha256);
