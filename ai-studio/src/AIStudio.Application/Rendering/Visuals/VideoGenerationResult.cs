namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Neutral metadata for one generated clip. <see cref="RelativeOutputPath"/> is
/// relative to the approved asset root; no absolute path, host, workflow, or model
/// detail crosses this boundary. Duration and dimensions describe the requested
/// window and are re-verified against the artifact by the durable pipeline (P3).
/// </summary>
public sealed record VideoGenerationResult(
    string RelativeOutputPath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int Width,
    int Height,
    int FramesPerSecond);
