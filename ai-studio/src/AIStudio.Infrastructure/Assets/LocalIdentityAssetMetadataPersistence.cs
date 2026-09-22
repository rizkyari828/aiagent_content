using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Assets;

/// <summary>
/// Local, registry-owned durable metadata for identity assets. Records are one
/// JSON file per <c>(assetId, version)</c> under a deterministic sub-tree of the
/// approved asset root; the physical mapping stays private to this class and is
/// never exposed through the Application contract. Writes are atomic
/// (temp file + move), creation fails on an existing record, and approval
/// replaces the record in place. Single-process, local-first v1: cross-process
/// coordination is not attempted.
/// </summary>
public sealed class LocalIdentityAssetMetadataPersistence : IIdentityAssetMetadataPersistence
{
    private const string MetadataDirectoryName = "identity-asset-metadata";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string rootPath;
    private readonly string metadataRootPath;

    public LocalIdentityAssetMetadataPersistence(IOptions<AssetStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Value.RootPath));
        metadataRootPath = Path.Combine(rootPath, MetadataDirectoryName);
    }

    public IReadOnlyList<IdentityAsset> LoadAll()
    {
        if (!Directory.Exists(metadataRootPath))
        {
            return [];
        }

        var assets = new List<IdentityAsset>();
        var files = Directory
            .EnumerateFiles(metadataRootPath, "*.json", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal);

        foreach (var file in files)
        {
            IdentityAsset? asset;
            try
            {
                asset = JsonSerializer.Deserialize<IdentityAsset>(File.ReadAllText(file), JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Identity asset metadata '{Path.GetFileName(file)}' is corrupt.",
                    exception);
            }

            if (asset is null)
            {
                throw new InvalidOperationException(
                    $"Identity asset metadata '{Path.GetFileName(file)}' is empty.");
            }

            assets.Add(asset);
        }

        return assets;
    }

    public void Create(IdentityAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var path = ResolvePath(asset.Id, asset.Version);
        try
        {
            WriteAtomic(path, Serialize(asset), overwrite: false);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Identity asset metadata '{asset.Id}' v{asset.Version.Value} already exists durably.",
                exception);
        }
    }

    public void Update(IdentityAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        WriteAtomic(ResolvePath(asset.Id, asset.Version), Serialize(asset), overwrite: true);
    }

    private static string Serialize(IdentityAsset asset) =>
        JsonSerializer.Serialize(asset, JsonOptions);

    private static void WriteAtomic(string path, string content, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private string ResolvePath(AssetReferenceId id, IdentityAssetVersion version)
    {
        var relativePath = $"{MetadataDirectoryName}/{id.Value}/v{version.Value}.json";
        var absolutePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!absolutePath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Identity asset metadata path escapes the approved asset root.");
        }

        return absolutePath;
    }

    private static void TryDelete(string path)
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
