namespace AIStudio.Application.AI;

public sealed class AiGenerationException : Exception
{
    public AiGenerationException(
        AiErrorCode errorCode,
        string message,
        int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ProviderStatusCode = providerStatusCode;
    }

    public AiErrorCode ErrorCode { get; }

    public int? ProviderStatusCode { get; }
}
