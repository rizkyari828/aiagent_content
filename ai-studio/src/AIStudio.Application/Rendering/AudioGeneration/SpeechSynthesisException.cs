namespace AIStudio.Application.Rendering.AudioGeneration;

public sealed class SpeechSynthesisException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
