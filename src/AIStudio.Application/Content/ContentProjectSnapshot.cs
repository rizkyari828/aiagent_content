namespace AIStudio.Application.Content;

public sealed record ContentProjectSnapshot(
    Guid Id,
    string Title,
    string? Brief);
