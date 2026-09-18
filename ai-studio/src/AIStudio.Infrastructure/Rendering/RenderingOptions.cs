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
}
