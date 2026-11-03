namespace AIStudio.Application.Transfer;

/// <summary>
/// Transport-neutral project transfer failure carrying a stable error code. The code is
/// part of the HTTP contract and is never derived from a message or an inner exception.
/// </summary>
public sealed class ProjectTransferException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
