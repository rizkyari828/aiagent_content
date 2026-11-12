namespace AIStudio.Infrastructure.Rendering.Gemini;

/// <summary>
/// Configuration for the Google Veo video generation provider.
/// </summary>
public sealed class GeminiVideoOptions
{
    public const string SectionName = "GeminiVideo";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/";

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "veo-3.1-generate-preview";

    public string OutputDirectory { get; init; } = "generated-video/gemini-veo";

    public string AspectRatio { get; init; } = "16:9";

    public string Resolution { get; init; } = "720p";

    public int TimeoutSeconds { get; init; } = 600;

    public int PollIntervalMilliseconds { get; init; } = 2000;

    public int MaxSeconds { get; init; } = 8;

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public int Fps { get; init; } = 30;
}
