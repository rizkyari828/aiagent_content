namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

/// <summary>
/// Configuration for the optional music generation engine. The ACE-Step runtime is
/// an external local installation, not a bundled dependency: when
/// <see cref="Enabled"/> is false callers must treat music as unavailable.
/// Machine-specific python/project paths come from configuration or environment
/// overrides, never source code.
/// </summary>
public sealed class MusicGenerationOptions
{
    public const string SectionName = "MusicGeneration";

    public bool Enabled { get; set; }

    public MusicGenerationAceStepOptions AceStep { get; set; } = new();
}

public sealed class MusicGenerationAceStepOptions
{
    /// <summary>Python interpreter that has the ACE-Step package installed.</summary>
    public string PythonExecutable { get; set; } = "python3";

    /// <summary>Approved local ACE-Step project root (contains the checkpoints directory).</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Checkpoint/config name resolved by the ACE-Step handler.</summary>
    public string Model { get; set; } = "acestep-v15-turbo";

    /// <summary>Repository-owned launcher; relative paths resolve against the app/output directory.</summary>
    public string ScriptPath { get; set; } = "Rendering/ACE-Step/generate.py";

    public int TimeoutSeconds { get; set; } = 900;

    public int InferenceSteps { get; set; } = 8;

    public int ExpectedSampleRate { get; set; } = 48000;

    public int ExpectedChannels { get; set; } = 2;
}
