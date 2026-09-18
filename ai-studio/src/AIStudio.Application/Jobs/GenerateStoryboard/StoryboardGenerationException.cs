namespace AIStudio.Application.Jobs.GenerateStoryboard;

public sealed class StoryboardGenerationException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
