namespace AIStudio.Application.Content;

/// <summary>Transport-neutral, stable project lifecycle errors.</summary>
public sealed class ContentProjectException : Exception
{
    public ContentProjectException(
        string errorCode,
        string message,
        IReadOnlyList<string>? reasonCodes = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ReasonCodes = reasonCodes ?? [];
    }

    public string ErrorCode { get; }

    /// <summary>Readiness codes that blocked a transition; empty for other errors.</summary>
    public IReadOnlyList<string> ReasonCodes { get; }
}
