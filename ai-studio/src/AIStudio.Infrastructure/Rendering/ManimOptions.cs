namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Configuration for the optional Manim animation engine. Manim is an external
/// local tool, not a bundled dependency: when <see cref="Enabled"/> is false the
/// planner keeps every scene on the deterministic SVG still engine.
/// </summary>
public sealed class ManimOptions
{
    public const string SectionName = "Manim";

    public bool Enabled { get; set; }

    /// <summary>Python interpreter that has Manim installed.</summary>
    public string PythonPath { get; set; } = "python3";

    /// <summary>Repository-owned launcher; relative paths resolve against the app/output directory.</summary>
    public string ScriptPath { get; set; } = "Rendering/Manim/render_scene.py";

    public int TimeoutSeconds { get; set; } = 600;
}
