namespace AIStudio.Application.Tracking;

/// <summary>
/// Transport-neutral tracking error. The code is stable and specific to the rejected
/// field or rule, so a caller can react without parsing the message.
/// </summary>
public sealed class TrackingException : Exception
{
    public TrackingException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException) =>
        ErrorCode = errorCode;

    public string ErrorCode { get; }
}
