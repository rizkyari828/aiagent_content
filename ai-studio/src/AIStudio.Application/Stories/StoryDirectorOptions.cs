namespace AIStudio.Application.Stories;

/// <summary>
/// Explicit Story Director implementation selection. The default keeps current
/// production behavior (the deterministic director); Qwen-backed directing is an
/// explicit opt-in. Choosing a mode never silently falls back to another mode, and
/// an unknown mode is a configuration error rather than a default.
/// </summary>
public sealed class StoryDirectorOptions
{
    public const string SectionName = "StoryDirector";

    public const string DeterministicMode = "deterministic";

    public const string QwenMode = "qwen";

    /// <summary>Selected implementation; defaults to <see cref="DeterministicMode"/>.</summary>
    public string Mode { get; set; } = DeterministicMode;

    public static bool IsKnownMode(string? mode) =>
        string.Equals(mode, DeterministicMode, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, QwenMode, StringComparison.OrdinalIgnoreCase);
}
