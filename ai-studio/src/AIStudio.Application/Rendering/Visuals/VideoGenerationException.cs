namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Stable failure codes for the image-to-video boundary. The set is closed so a
/// future durable job can map failures to a fallback policy without parsing
/// provider messages.
/// </summary>
public sealed class VideoGenerationException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>The request is structurally unusable or exceeds the trusted bounds.</summary>
    public const string RequestInvalid = "video_request_invalid";

    /// <summary>The local engine rejected or failed the generation.</summary>
    public const string GenerationFailed = "video_generation_failed";

    /// <summary>The local engine did not finish inside the configured timeout.</summary>
    public const string Timeout = "video_timeout";

    /// <summary>The engine returned bytes that are not a usable video artifact.</summary>
    public const string ArtifactInvalid = "video_artifact_invalid";

    /// <summary>No trusted workflow or engine is configured, or the engine is disabled.</summary>
    public const string NotConfigured = "video_not_configured";

    public string ErrorCode { get; } = errorCode;
}
