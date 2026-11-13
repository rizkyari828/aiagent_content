namespace AIStudio.Application.Subtitles;

/// <summary>
/// Local audio-to-SRT transcription boundary (PRD §35). Implementations
/// transcribe an approved audio file into validated SRT content without
/// accepting caller-supplied scripts, executables, or model paths.
/// </summary>
public interface ITranscriptionProvider
{
    /// <summary>
    /// False when no transcription runtime is enabled.
    /// </summary>
    bool IsEnabled { get; }

    Task<TranscriptionResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken);
}

public sealed record TranscriptionRequest(
    string AudioAbsolutePath,
    string? Language = null);

public sealed record TranscriptionResult(
    string SrtContent,
    string? DetectedLanguage,
    int SegmentCount);

public sealed class TranscriptionException(
    string errorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ErrorCode { get; } = errorCode;
}
