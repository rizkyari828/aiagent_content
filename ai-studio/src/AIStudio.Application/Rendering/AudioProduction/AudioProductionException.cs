namespace AIStudio.Application.Rendering.AudioProduction;

/// <summary>
/// Structured failure raised while resolving or producing the durable audio
/// production workspace (enqueue-facing codes; the job handler maps them onto
/// the job execution boundary).
/// </summary>
public sealed class AudioProductionException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
