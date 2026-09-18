using AIStudio.Domain.Assets;

namespace AIStudio.Application.Assets;

public sealed record SceneAssetSnapshot(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    int SceneIndex,
    AssetType Type,
    string Path,
    long ByteSize,
    string ContentHash,
    AssetOrigin Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset CreatedAt);
