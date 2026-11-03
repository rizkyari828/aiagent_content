namespace AIStudio.Application.Approvals;

/// <summary>Transport-neutral A3 approval failure carrying a stable error code.</summary>
public sealed class FinalApprovalException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
