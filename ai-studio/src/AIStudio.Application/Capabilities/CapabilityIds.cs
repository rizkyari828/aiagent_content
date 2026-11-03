namespace AIStudio.Application.Capabilities;

/// <summary>
/// Stable capability identifiers known to the studio v1. These are values, not an
/// enum: adding a future identifier does not require a code change here, and a
/// content concept may request any of them as data. Only providers that already
/// exist are registered; unknown identifiers resolve to an explicit gap.
/// </summary>
public static class CapabilityIds
{
    public static readonly CapabilityId SpeechNarration = new("speech.narration");

    public static readonly CapabilityId MusicInstrumental = new("music.instrumental");

    public static readonly CapabilityId VisualStill = new("visual.still");

    public static readonly CapabilityId VisualDiagram = new("visual.diagram");

    public static readonly CapabilityId VisualUiMotion = new("visual.ui_motion");

    public static readonly CapabilityId VisualAiImage = new("visual.ai_image");

    public static readonly CapabilityId VisualThreeD = new("visual.three_d");

    /// <summary>
    /// Image-to-video character animation. This id is the documented "valid data, no
    /// provider yet" example (D-036) and keeps that name; the scaffold provider is
    /// disabled by default and not selected by any production path in v1.
    /// </summary>
    public static readonly CapabilityId VisualCharacterAnimation = new("visual.character_animation");

    public static readonly CapabilityId MediaCompose = new("media.compose");

    public static readonly CapabilityId SubtitleBurned = new("subtitle.burned");
}
