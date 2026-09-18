using AIStudio.Domain.Assets;

namespace AIStudio.Application.Narration;

public sealed record NarrationSnapshot(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    string Path,
    long ByteSize,
    string ContentHash,
    AssetOrigin Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset CreatedAt);
