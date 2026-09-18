using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Domain.Assets;

public sealed class SceneAsset
{
    public const int MaxPathLength = 1024;
    public const int MaxProvenanceLength = 500;
    public const int ContentHashLength = 64;

    private SceneAsset()
    {
    }

    private SceneAsset(
        Guid id,
        Guid contentProjectId,
        Guid sourceJobId,
        int sceneIndex,
        AssetType type,
        string path,
        long byteSize,
        string contentHash,
        AssetOrigin origin,
        string? source,
        string? creator,
        string? license,
        DateTimeOffset? retrievedAt,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        SourceJobId = sourceJobId;
        SceneIndex = sceneIndex;
        Type = type;
        Path = path;
        ByteSize = byteSize;
        ContentHash = contentHash;
        Origin = origin;
        Source = source;
        Creator = creator;
        License = license;
        RetrievedAt = retrievedAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public ContentProject ContentProject { get; private set; } = null!;

    public Guid SourceJobId { get; private set; }

    public Job SourceJob { get; private set; } = null!;

    public int SceneIndex { get; private set; }

    public AssetType Type { get; private set; }

    public string Path { get; private set; } = string.Empty;

    public long ByteSize { get; private set; }

    public string ContentHash { get; private set; } = string.Empty;

    public AssetOrigin Origin { get; private set; }

    public string? Source { get; private set; }

    public string? Creator { get; private set; }

    public string? License { get; private set; }

    public DateTimeOffset? RetrievedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SceneAsset Create(
        Guid contentProjectId,
        Guid sourceJobId,
        int sceneIndex,
        AssetType type,
        string path,
        long byteSize,
        string contentHash,
        AssetOrigin origin,
        string? source,
        string? creator,
        string? license,
        DateTimeOffset? retrievedAt,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException("Content project ID cannot be empty.", nameof(contentProjectId));
        }

        if (sourceJobId == Guid.Empty)
        {
            throw new ArgumentException("Source job ID cannot be empty.", nameof(sourceJobId));
        }

        if (sceneIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneIndex), "Scene index cannot be negative.");
        }

        if (byteSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteSize), "Byte size cannot be negative.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Asset type is not supported.");
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "Asset origin is not supported.");
        }

        var assetId = id ?? Guid.NewGuid();
        if (assetId == Guid.Empty)
        {
            throw new ArgumentException("Asset ID cannot be empty.", nameof(id));
        }

        var normalizedSource = OptionalText(source, MaxProvenanceLength, nameof(source));
        var normalizedCreator = OptionalText(creator, MaxProvenanceLength, nameof(creator));
        var normalizedLicense = OptionalText(license, MaxProvenanceLength, nameof(license));

        if (origin == AssetOrigin.External)
        {
            if (normalizedSource is null)
            {
                throw new ArgumentException(
                    "Externally sourced assets require a source.",
                    nameof(source));
            }

            if (normalizedLicense is null)
            {
                throw new ArgumentException(
                    "Externally sourced assets require license information.",
                    nameof(license));
            }
        }

        var createdAt = utcNow.ToUniversalTime();
        var normalizedRetrievedAt = origin == AssetOrigin.External
            ? (retrievedAt ?? createdAt).ToUniversalTime()
            : retrievedAt?.ToUniversalTime();

        return new SceneAsset(
            assetId,
            contentProjectId,
            sourceJobId,
            sceneIndex,
            type,
            RequireText(path, MaxPathLength, nameof(path)),
            byteSize,
            RequireHash(contentHash),
            origin,
            normalizedSource,
            normalizedCreator,
            normalizedLicense,
            normalizedRetrievedAt,
            createdAt);
    }

    private static string RequireHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != ContentHashLength
            || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Content hash must be a 64-character hexadecimal SHA-256 value.",
                nameof(value));
        }

        return value.ToLowerInvariant();
    }

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length > maxLength
            ? throw new ArgumentException(
                $"Value must contain 1 to {maxLength} characters.",
                parameterName)
            : normalized;
    }

    private static string? OptionalText(string? value, int maxLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RequireText(value, maxLength, parameterName);
}
