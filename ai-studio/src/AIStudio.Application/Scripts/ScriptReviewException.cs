namespace AIStudio.Application.Scripts;

public sealed class ScriptReviewException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
