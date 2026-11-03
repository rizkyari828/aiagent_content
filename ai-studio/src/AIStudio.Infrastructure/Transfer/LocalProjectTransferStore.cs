using System.Security.Cryptography;
using AIStudio.Application.Assets;
using AIStudio.Application.Transfer;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Transfer;

/// <summary>
/// Filesystem/ZIP adapter for project transfer. It is the only place that turns a
/// package into bytes: export reads through the existing approved-root resolver, import
/// writes exclusively (never overwriting), and every package is staged in a temporary
/// file and promoted atomically.
/// </summary>
public sealed class LocalProjectTransferStore : IProjectTransferStore
{
    private const string PackageExtension = ".aistudio.zip";
    private const int BufferSize = 81_920;

    private readonly LocalAssetFileStore assets;
    private readonly string transferRoot;

    public LocalProjectTransferStore(
        LocalAssetFileStore assets,
        IOptions<ProjectTransferOptions> options)
    {
        this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        Limits = new ProjectTransferLimits(
            value.MaxFileBytes,
            value.MaxPackageBytes,
            value.MaxManifestBytes,
            value.MaxFileCount);
        Limits.EnsureValid();

        transferRoot = ResolveTransferRoot(assets.RootPath, value.RootPath);
    }

    public ProjectTransferLimits Limits { get; }

    public async Task<ProjectAssetCopy> CopyAssetAsync(
        string relativePath,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var (_, absolutePath) = ResolveAsset(relativePath, import: false);
        if (!File.Exists(absolutePath))
        {
            throw new ProjectTransferException(
                "export_artifact_missing",
                $"Asset file '{relativePath}' does not exist under the approved root.");
        }

        try
        {
            await using var source = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long total = 0;

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > Limits.MaxFileBytes)
                {
                    throw new ProjectTransferException(
                        "export_file_too_large",
                        $"Asset file '{relativePath}' exceeds the configured file size limit.");
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total == 0)
            {
                throw new ProjectTransferException(
                    "export_integrity_failed",
                    $"Asset file '{relativePath}' is empty.");
            }

            return new ProjectAssetCopy(
                relativePath,
                total,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            throw new ProjectTransferException(
                "export_integrity_failed",
                $"Asset file '{relativePath}' could not be read.",
                exception);
        }
    }

    public async Task<ProjectWrittenAsset> WriteAssetAsync(
        string relativePath,
        Stream source,
        long expectedByteSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var (normalized, absolutePath) = ResolveAsset(relativePath, import: true);
        if (!string.Equals(normalized, relativePath, StringComparison.Ordinal))
        {
            throw new ProjectTransferException(
                "import_path_invalid",
                $"Imported path '{relativePath}' is not normalized.");
        }

        if (expectedByteSize <= 0 || expectedByteSize > Limits.MaxFileBytes)
        {
            throw new ProjectTransferException(
                "import_too_large",
                $"Imported file '{relativePath}' exceeds the configured file size limit.");
        }

        if (File.Exists(absolutePath))
        {
            throw new ProjectTransferException(
                "import_path_conflict",
                $"Imported file '{relativePath}' already exists; v1 never overwrites files.");
        }

        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.part";

        try
        {
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                long total = 0;

                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > expectedByteSize || total > Limits.MaxFileBytes)
                    {
                        throw new ProjectTransferException(
                            "import_integrity_failed",
                            $"Imported file '{relativePath}' is larger than declared.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                var actualHash = Convert
                    .ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant();
                if (total != expectedByteSize
                    || !string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ProjectTransferException(
                        "import_integrity_failed",
                        $"Imported file '{relativePath}' does not match its declared size or SHA-256.");
                }
            }

            try
            {
                File.Move(temporaryPath, absolutePath, overwrite: false);
            }
            catch (IOException exception)
            {
                throw new ProjectTransferException(
                    "import_path_conflict",
                    $"Imported file '{relativePath}' already exists; v1 never overwrites files.",
                    exception);
            }

            return new ProjectWrittenAsset(normalized, expectedByteSize, expectedSha256);
        }
        catch
        {
            DeleteIfExists(temporaryPath);
            throw;
        }
    }

    public void DeleteAsset(string relativePath)
    {
        try
        {
            var (_, absolutePath) = ResolveAsset(relativePath, import: true);
            DeleteIfExists(absolutePath);
        }
        catch (Exception exception)
            when (exception is ProjectTransferException
                or IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException)
        {
            // Best effort: cleanup must never hide the original import failure.
        }
    }

    public async Task<ProjectPackageInfo> WritePackageAsync(
        Guid contentProjectId,
        DateTimeOffset exportedAtUtc,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeAsync);
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty content project identifier is required.",
                nameof(contentProjectId));
        }

        EnsureTransferRootIsSafe();
        Directory.CreateDirectory(transferRoot);
        var temporaryPath = Path.Combine(
            transferRoot,
            $".{Guid.NewGuid():N}.package.part");

        ProjectPackageInfo info;
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await writeAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            var byteSize = new FileInfo(temporaryPath).Length;
            if (byteSize > Limits.MaxPackageBytes)
            {
                throw new ProjectTransferException(
                    "export_package_too_large",
                    "The package exceeds the configured package size limit.");
            }

            var sha256 = ComputeFileHash(temporaryPath);
            var fileName = BuildPackageFileName(contentProjectId, exportedAtUtc);
            var finalPath = Path.Combine(transferRoot, fileName);
            try
            {
                File.Move(temporaryPath, finalPath, overwrite: false);
            }
            catch (IOException)
            {
                // A same-millisecond collision is possible with a random suffix; prefer
                // failing over replacing an existing package.
                throw new ProjectTransferException(
                    "export_integrity_failed",
                    "A package with the generated name already exists.");
            }

            info = new ProjectPackageInfo(
                fileName,
                byteSize,
                exportedAtUtc.ToUniversalTime(),
                sha256);
        }
        catch
        {
            DeleteIfExists(temporaryPath);
            throw;
        }

        return info;
    }

    public IReadOnlyList<ProjectPackageInfo> ListPackages(Guid contentProjectId)
    {
        if (contentProjectId == Guid.Empty || !Directory.Exists(transferRoot))
        {
            return [];
        }

        EnsureTransferRootIsSafe();
        var prefix = $"{contentProjectId:N}-";
        var packages = new List<ProjectPackageInfo>();

        foreach (var path in Directory.EnumerateFiles(
            transferRoot,
            $"*{PackageExtension}",
            SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
                || fileName.Length <= prefix.Length + PackageExtension.Length
                || !Guid.TryParseExact(
                    fileName[..32],
                    "N",
                    out var parsedProjectId)
                || parsedProjectId != contentProjectId)
            {
                continue;
            }

            var fileInfo = new FileInfo(path);
            if (fileInfo.LinkTarget is not null)
            {
                continue;
            }

            packages.Add(new ProjectPackageInfo(
                fileName,
                fileInfo.Length,
                new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                null));
        }

        return packages
            .OrderByDescending(package => package.CreatedAtUtc)
            .ThenBy(package => package.FileName, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<T> WithStagedPackageAsync<T>(
        Stream source,
        Func<Stream, CancellationToken, Task<T>> readAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(readAsync);

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "aistudio-project-imports");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.zip");

        try
        {
            long total = 0;
            var buffer = new byte[BufferSize];
            await using (var staged = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > Limits.MaxPackageBytes)
                    {
                        throw new ProjectTransferException(
                            "import_too_large",
                            "The uploaded package exceeds the configured package size limit.");
                    }

                    await staged.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await staged.FlushAsync(cancellationToken);
            }

            if (total == 0)
            {
                throw new ProjectTransferException(
                    "import_package_invalid",
                    "The uploaded package is empty.");
            }

            await using var package = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await readAsync(package, cancellationToken);
        }
        catch (ProjectTransferException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ProjectTransferException(
                "import_package_invalid",
                "The uploaded package could not be staged.",
                exception);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private (string RelativePath, string AbsolutePath) ResolveAsset(
        string relativePath,
        bool import)
    {
        try
        {
            return assets.Resolve(relativePath);
        }
        catch (AssetCollectionException exception)
        {
            if (import)
            {
                throw new ProjectTransferException(
                    "import_path_invalid",
                    "The imported path is not a safe path under the approved root.",
                    exception);
            }

            var code = exception.ErrorCode switch
            {
                "asset_file_not_found" => "export_artifact_missing",
                "asset_path_invalid" => "export_path_invalid",
                _ => "export_integrity_failed"
            };
            throw new ProjectTransferException(code, exception.Message, exception);
        }
    }

    private void EnsureTransferRootIsSafe()
    {
        var relative = Path.GetRelativePath(assets.RootPath, transferRoot);
        var current = assets.RootPath;
        foreach (var segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                throw new ProjectTransferException(
                    "export_path_invalid",
                    "The configured transfer root traverses a symbolic link.");
            }
        }
    }

    private static string ResolveTransferRoot(string assetRoot, string requestedRoot)
    {
        if (!ProjectPackagePath.TryNormalize(requestedRoot, out var relative))
        {
            throw new InvalidOperationException(
                "ProjectTransfer:RootPath must be a relative path without traversal.");
        }

        var resolved = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(assetRoot, relative)));
        if (!resolved.StartsWith(
                assetRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ProjectTransfer:RootPath must stay inside the approved asset root.");
        }

        return resolved;
    }

    private static string BuildPackageFileName(
        Guid contentProjectId,
        DateTimeOffset exportedAtUtc) =>
        $"{contentProjectId:N}-"
        + $"{exportedAtUtc.ToUniversalTime():yyyyMMdd'T'HHmmssfff'Z'}-"
        + $"{Guid.NewGuid():N}"[..8]
        + PackageExtension;

    private static string ComputeFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert
            .ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
