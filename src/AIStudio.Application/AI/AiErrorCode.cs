namespace AIStudio.Application.AI;

public enum AiErrorCode
{
    ProviderUnavailable = 0,
    Timeout = 1,
    ModelNotFound = 2,
    InvalidRequest = 3,
    MalformedResponse = 4,
    ProviderError = 5
}
