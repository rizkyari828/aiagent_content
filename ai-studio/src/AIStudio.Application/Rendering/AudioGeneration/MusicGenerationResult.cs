namespace AIStudio.Application.Rendering.AudioGeneration;

public sealed record MusicGenerationResult(
    string RelativePath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int SampleRate,
    int Channels,
    long? Seed);
