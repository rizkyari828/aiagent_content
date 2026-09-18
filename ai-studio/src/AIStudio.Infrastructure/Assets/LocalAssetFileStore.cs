using System.Security;
using System.Security.Cryptography;
using AIStudio.Application.Assets;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Assets;

public sealed class LocalAssetFileStore : IAssetFileStore
{
    private readonly string rootPath;

    public LocalAssetFileStore(IOptions<AssetStorageOptions> options)
    {
        rootPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(options.Value.RootPath));
    }

    public AssetFileInfo Register(string requestedPath)
    {
        var (relativePath, absolutePath) = Resolve(requestedPath);

        if (!File.Exists(absolutePath))
        {
            throw Invalid(
                "asset_file_not_found",
                "The asset file does not exist under the approved root.");
        }

        try
        {
            using var stream = File.OpenRead(absolutePath);
            var byteSize = stream.Length;
            if (byteSize == 0)
            {
                throw Invalid("asset_file_invalid", "The asset file is empty.");
            }

            var contentHash = Convert
                .ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
            return new AssetFileInfo(relativePath, absolutePath, byteSize, contentHash);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
        {
            throw Invalid(
                "asset_file_unreadable",
                "The asset file could not be read.",
                exception);
        }
    }

    public async Task<AssetFileInfo> WriteAsync(
        string relativePath,
        byte[] content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
        {
            throw Invalid("asset_file_invalid", "Generated asset content is empty.");
        }

        var (normalized, absolutePath) = Resolve(relativePath);
        var contentHash = Convert
            .ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();

        if (File.Exists(absolutePath)
            && await MatchesAsync(absolutePath, contentHash, cancellationToken))
        {
            // Idempotent: identical content already exists, leave it untouched.
            return new AssetFileInfo(normalized, absolutePath, content.Length, contentHash);
        }

        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.part";

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, absolutePath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }

        return new AssetFileInfo(normalized, absolutePath, content.Length, contentHash);
    }

    private static async Task<bool> MatchesAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert
            .ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        return string.Equals(hash, expectedHash, StringComparison.Ordinal);
    }

    private (string RelativePath, string AbsolutePath) Resolve(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            throw Invalid("asset_path_invalid", "An asset path is required.");
        }

        var normalized = requestedPath.Trim().Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            throw Invalid(
                "asset_path_invalid",
                "Asset path must be relative to the approved asset root.");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw Invalid(
                "asset_path_invalid",
                "Asset path must not contain relative segments.");
        }

        var relativePath = string.Join('/', segments);
        string absolutePath;
        try
        {
            absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        }
        catch (Exception exception)
            when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            throw Invalid("asset_path_invalid", "Asset path is not valid.", exception);
        }

        if (!IsWithinRoot(absolutePath))
        {
            throw Invalid(
                "asset_path_invalid",
                "Asset path escapes the approved asset root.");
        }

        if (ContainsSymbolicLink(absolutePath))
        {
            throw Invalid(
                "asset_path_invalid",
                "Asset path must not traverse symbolic links.");
        }

        return (relativePath, absolutePath);
    }

    private bool IsWithinRoot(string path) =>
        path.StartsWith(
            rootPath + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);

    private bool ContainsSymbolicLink(string absolutePath)
    {
        var relative = Path.GetRelativePath(rootPath, absolutePath);
        var current = rootPath;

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
                return true;
            }
        }

        return false;
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

    private static AssetCollectionException Invalid(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
