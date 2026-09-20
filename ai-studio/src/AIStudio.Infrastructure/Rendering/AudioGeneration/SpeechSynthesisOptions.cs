namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

/// <summary>
/// Configuration for the optional speech synthesis engine. The VoxCPM2 runtime is
/// an external local installation, not a bundled dependency: when
/// <see cref="Enabled"/> is false callers must treat speech as unavailable.
/// Machine-specific python/model paths come from configuration or environment
/// overrides, never source code.
/// </summary>
public sealed class SpeechSynthesisOptions
{
    public const string SectionName = "SpeechSynthesis";

    public bool Enabled { get; set; }

    public SpeechSynthesisVoxCpmOptions VoxCpm2 { get; set; } = new();
}

public sealed class SpeechSynthesisVoxCpmOptions
{
    /// <summary>Python interpreter that has the VoxCPM2 package installed.</summary>
    public string PythonExecutable { get; set; } = "python3";

    /// <summary>Approved local VoxCPM2 model directory.</summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>Repository-owned launcher; relative paths resolve against the app/output directory.</summary>
    public string ScriptPath { get; set; } = "Rendering/VoxCPM2/synthesize.py";

    public int TimeoutSeconds { get; set; } = 600;

    public int InferenceTimesteps { get; set; } = 20;

    public double CfgValue { get; set; } = 2.0;

    public bool Normalize { get; set; } = true;

    /// <summary>Expected output sample rate used to validate the generated WAV.</summary>
    public int ExpectedSampleRate { get; set; } = 48000;
}
