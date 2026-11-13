namespace AIStudio.Infrastructure.Subtitles;

public sealed class TranscriptionOptions
{
    public const string SectionName = "Transcription";

    /// <summary>
    /// Disabled by default so environments without faster-whisper/GPU run cleanly.
    /// </summary>
    public bool Enabled { get; set; }

    public FasterWhisperOptions FasterWhisper { get; set; } = new();
}

public sealed class FasterWhisperOptions
{
    public string PythonExecutable { get; set; } = "python3";

    public string ScriptPath { get; set; } = "Subtitles/FasterWhisper/transcribe.py";

    public string Model { get; set; } = "small";

    public string Device { get; set; } = "auto";

    public string ComputeType { get; set; } = "default";

    public string DefaultLanguage { get; set; } = "id";

    public int TimeoutSeconds { get; set; } = 300;
}
