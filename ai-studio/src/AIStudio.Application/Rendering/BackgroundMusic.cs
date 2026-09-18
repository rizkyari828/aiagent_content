namespace AIStudio.Application.Rendering;

/// <summary>
/// Optional background music bed. The file is looped and trimmed to the video
/// duration; <paramref name="Volume"/> keeps the bed below narration and
/// <paramref name="Duck"/> lowers it further whenever narration is active.
/// </summary>
public sealed record BackgroundMusic(
    string AbsolutePath,
    double Volume = 0.28,
    bool Duck = true);
