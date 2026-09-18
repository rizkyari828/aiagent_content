namespace AIStudio.Application.Rendering;

public sealed record VideoRenderOutput(
    string RelativePath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int Width,
    int Height);
