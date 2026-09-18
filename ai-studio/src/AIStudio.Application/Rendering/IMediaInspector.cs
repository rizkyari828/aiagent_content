namespace AIStudio.Application.Rendering;

public interface IMediaInspector
{
    Task<MediaInspection> InspectAsync(
        string absolutePath,
        CancellationToken cancellationToken);
}

public sealed record MediaInspection(
    double DurationSeconds,
    bool HasVideo,
    bool HasAudio,
    bool HasSubtitle,
    int Width,
    int Height);
