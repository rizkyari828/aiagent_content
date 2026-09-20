namespace AIStudio.Application.Rendering.AudioGeneration;

/// <summary>
/// A request to generate one music bed. The prompt is a natural-language brief and
/// is always transferred as JSON data; providers never accept CLI flags, model
/// settings, or filter strings from callers.
/// </summary>
public sealed record MusicGenerationRequest(
    string Prompt,
    double DurationSeconds,
    int Bpm,
    bool Instrumental,
    string RelativeOutputPath,
    long? Seed = null);
