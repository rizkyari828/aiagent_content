namespace AIStudio.Application.Narration;

public sealed class NarrationException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
