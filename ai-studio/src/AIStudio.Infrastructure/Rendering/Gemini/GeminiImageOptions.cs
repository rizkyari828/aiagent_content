namespace AIStudio.Infrastructure.Rendering.Gemini;

/// <summary>
/// Configuration for the Gemini Nano Banana image generation engine.
/// Cloud-hosted through Google's Generative Language API; does not consume
/// local GPU resources.
/// </summary>
public sealed class GeminiImageOptions
{
    public const string SectionName = "GeminiImage";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/";

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gemini-2.5-flash-image";

    public string AspectRatio { get; init; } = "16:9";

    public int TimeoutSeconds { get; init; } = 120;
}
