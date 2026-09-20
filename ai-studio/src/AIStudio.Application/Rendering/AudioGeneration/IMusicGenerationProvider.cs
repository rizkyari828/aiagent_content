namespace AIStudio.Application.Rendering.AudioGeneration;

/// <summary>
/// Local music-generation boundary. Implementations turn one validated brief into
/// a WAV and persist it through the approved asset store. They never accept
/// caller-supplied CLI flags, executable paths, model paths, or shell text.
/// </summary>
public interface IMusicGenerationProvider
{
    /// <summary>
    /// False when no music runtime is configured. Callers then treat music as
    /// unavailable instead of failing.
    /// </summary>
    bool IsEnabled { get; }

    Task<MusicGenerationResult> GenerateAsync(
        MusicGenerationRequest request,
        CancellationToken cancellationToken);
}
