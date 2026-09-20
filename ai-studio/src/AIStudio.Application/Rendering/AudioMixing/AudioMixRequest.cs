namespace AIStudio.Application.Rendering.AudioMixing;

/// <summary>
/// A request to master narration with an optional background music bed.
/// Narration is the primary track and is required. Narration and music are
/// asset references relative to the approved asset root; the output path is
/// relative to the same root. No absolute or caller-supplied paths are accepted.
/// </summary>
public sealed record AudioMixRequest(
    string NarrationRelativePath,
    string? BackgroundMusicRelativePath,
    string RelativeOutputPath);
