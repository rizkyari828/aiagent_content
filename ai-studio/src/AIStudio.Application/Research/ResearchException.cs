namespace AIStudio.Application.Research;

public sealed class ResearchException : Exception
{
    public ResearchException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
