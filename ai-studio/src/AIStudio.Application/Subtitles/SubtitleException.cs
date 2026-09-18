namespace AIStudio.Application.Subtitles;

public sealed class SubtitleException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
