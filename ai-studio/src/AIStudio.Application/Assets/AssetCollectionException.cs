namespace AIStudio.Application.Assets;

public sealed class AssetCollectionException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
