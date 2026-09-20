namespace AIStudio.Infrastructure.Rendering.AudioMixing;

/// <summary>
/// Centralizes every value used by the audio mastering filter graph so no magic
/// numbers are scattered through the mixer. Defaults target spoken-word content:
/// narration around -16 LUFS, background music comfortably below it, automatic
/// sidechain ducking under speech and a -1 dBFS limiter ceiling.
/// </summary>
public sealed class AudioMixingOptions
{
    public const string SectionName = "AudioMixing";

    public int SampleRate { get; set; } = 48000;

    public int Channels { get; set; } = 2;

    public double NarrationLoudnessTarget { get; set; } = -16;

    public double NarrationTruePeak { get; set; } = -1.5;

    public double NarrationLoudnessRange { get; set; } = 11;

    public double MusicLoudnessTarget { get; set; } = -24;

    public double MusicTruePeak { get; set; } = -3;

    public double MusicLoudnessRange { get; set; } = 7;

    public bool EnableDucking { get; set; } = true;

    public double DuckThreshold { get; set; } = 0.05;

    public double DuckRatio { get; set; } = 4;

    public int DuckAttackMs { get; set; } = 20;

    public int DuckReleaseMs { get; set; } = 400;

    public double FadeInSeconds { get; set; } = 1;

    public double FadeOutSeconds { get; set; } = 1.5;

    public double LimiterCeilingDb { get; set; } = -1;
}
