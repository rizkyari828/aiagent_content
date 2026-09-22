namespace AIStudio.Application.IdentityAssets;

/// <summary>
/// Stable failure channel for identity-asset authoring (import/approval). Carries a
/// transport-neutral error code so callers map it without parsing messages.
/// </summary>
public sealed class IdentityAssetAuthoringException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
