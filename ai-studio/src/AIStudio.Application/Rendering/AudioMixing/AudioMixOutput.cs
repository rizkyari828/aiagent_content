namespace AIStudio.Application.Rendering.AudioMixing;

public sealed record AudioMixOutput(
    string RelativePath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int SampleRate,
    int Channels);
