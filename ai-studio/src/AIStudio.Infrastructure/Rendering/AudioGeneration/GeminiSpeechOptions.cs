namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

/// <summary>
/// Configuration for the Gemini cloud text-to-speech engine.
/// Speech synthesis runs in Google Cloud; output audio is resampled
/// locally to 48 kHz mono PCM WAV via FFmpeg.
/// </summary>
public sealed class GeminiSpeechOptions
{
    public const string SectionName = "GeminiSpeech";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/";

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gemini-2.5-flash-preview-tts";

    public string FfmpegPath { get; init; } = "ffmpeg";

    public int TimeoutSeconds { get; init; } = 120;

    public int ExpectedSampleRate { get; init; } = 48000;
}
