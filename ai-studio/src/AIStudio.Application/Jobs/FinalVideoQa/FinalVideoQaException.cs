namespace AIStudio.Application.Jobs.FinalVideoQa;

public sealed class FinalVideoQaException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
