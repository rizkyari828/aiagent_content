using AIStudio.Domain.Content;

namespace AIStudio.Application.Content;

/// <summary>Compact project row for deterministic lists: no brief and no job internals.</summary>
public sealed record ContentProjectSummary(
    Guid Id,
    string Title,
    ContentStatus Status,
    DateTimeOffset UpdatedAt);
