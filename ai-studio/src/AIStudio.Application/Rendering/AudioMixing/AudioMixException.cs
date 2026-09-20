namespace AIStudio.Application.Rendering.AudioMixing;

public sealed class AudioMixException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
