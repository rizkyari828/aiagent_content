namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

public sealed class SceneVisualGenerationException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
