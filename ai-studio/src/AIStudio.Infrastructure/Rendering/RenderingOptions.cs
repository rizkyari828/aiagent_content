using AIStudio.Application.Rendering;

namespace AIStudio.Infrastructure.Rendering;

public sealed class RenderingOptions
{
    public const string SectionName = "Rendering";

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    public int TimeoutSeconds { get; set; } = 600;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public int FrameRate { get; set; } = 30;

    /// <summary>Motion rotation applied to static scenes when no explicit motion is set.</summary>
    public bool EnableMotion { get; set; } = true;

    public SceneTransition Transition { get; set; } = SceneTransition.Crossfade;

    public double TransitionDurationSeconds { get; set; } = 0.35;

    public SubtitleStyle Subtitle { get; set; } = new();

    /// <summary>Optional local background music bed. Absolute or relative to the asset root.</summary>
    public string? BackgroundMusicPath { get; set; }

    public double BackgroundMusicVolume { get; set; } = 0.28;

    public bool EnableDucking { get; set; } = true;
}
