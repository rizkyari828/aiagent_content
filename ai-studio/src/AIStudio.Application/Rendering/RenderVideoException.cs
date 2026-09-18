namespace AIStudio.Application.Rendering;

public sealed class RenderVideoException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
