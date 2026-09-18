namespace AIStudio.Application.Rendering;

/// <summary>
/// A render request. When <paramref name="SubtitleCueTexts"/> is supplied alongside
/// <paramref name="SubtitleAbsolutePath"/>, the renderer derives cue boundaries from
/// the same scene timing used for the video and burns that derived subtitle in,
/// leaving the canonical asset untouched.
/// </summary>
public sealed record VideoRenderRequest(
    IReadOnlyList<SceneMediaInput> Scenes,
    string NarrationAbsolutePath,
    string RelativeOutputPath,
    string? SubtitleAbsolutePath = null,
    IReadOnlyList<string>? SubtitleCueTexts = null);
