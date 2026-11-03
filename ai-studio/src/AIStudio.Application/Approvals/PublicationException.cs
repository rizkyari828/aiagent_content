namespace AIStudio.Application.Approvals;

/// <summary>Transport-neutral publication failure carrying a stable error code.</summary>
public sealed class PublicationException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
