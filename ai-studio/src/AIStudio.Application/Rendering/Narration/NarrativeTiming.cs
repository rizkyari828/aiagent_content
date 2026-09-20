namespace AIStudio.Application.Rendering.Narration;

/// <summary>
/// Shared, deterministic narrative timing constants. Per-scene scene length is
/// <c>introLead + measured narration + outroHold</c>; consecutive scenes overlap by
/// the render transition so the assembled narration and the rendered video share
/// exactly the same timeline. Values are deliberately small so speech drives timing.
/// </summary>
public static class NarrativeTiming
{
    /// <summary>Bump when the narration/timing behavior changes.</summary>
    public const string Version = "narrative-sync/v1";

    public const double IntroLeadSeconds = 0.25;

    public const double OutroHoldSeconds = 0.45;

    public const double TransitionSeconds = 0.35;

    /// <summary>Minimum on-screen scene length for readability (never stretches speech).</summary>
    public const double MinSceneSeconds = 1.0;

    public const double MaxSceneSeconds = 120.0;

    public const int MaxSubtitleLineCharacters = 42;

    public const int MaxSubtitleLines = 2;
}
