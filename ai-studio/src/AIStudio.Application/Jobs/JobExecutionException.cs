namespace AIStudio.Application.Jobs;

public sealed class JobExecutionException : Exception
{
    public JobExecutionException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
