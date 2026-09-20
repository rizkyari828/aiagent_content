namespace AIStudio.Application.Rendering;

/// <summary>
/// A render request. When <paramref name="SubtitleCueTexts"/> or
/// <paramref name="SubtitleSrtContent"/> is supplied, the renderer writes that
/// derived subtitle transiently and burns it in, leaving the canonical asset
/// untouched. <paramref name="TransitionDurationSeconds"/> lets a narrative-sync
/// render reuse the exact transition the narration timeline was assembled against.
/// </summary>
public sealed record VideoRenderRequest(
    IReadOnlyList<SceneMediaInput> Scenes,
    string NarrationAbsolutePath,
    string RelativeOutputPath,
    string? SubtitleAbsolutePath = null,
    IReadOnlyList<string>? SubtitleCueTexts = null,
    string? SubtitleSrtContent = null,
    double? TransitionDurationSeconds = null);
