namespace AIStudio.Application.Rendering;

public sealed class ProcessExecutionException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public const string StartFailed = "process_start_failed";

    public const string TimedOut = "process_timeout";

    public const string MediaProbeFailed = "media_probe_failed";

    public string ErrorCode { get; } = errorCode;
}
