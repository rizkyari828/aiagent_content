namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Configuration for the optional Blender 3D engine. Blender is an external local
/// tool, not a bundled dependency: when <see cref="Enabled"/> is false the planner
/// keeps every scene on the deterministic still engines. CUDA is the verified
/// production backend; OptiX is intentionally not required.
/// </summary>
public sealed class BlenderOptions
{
    public const string SectionName = "Blender";

    public bool Enabled { get; set; }

    /// <summary>Blender executable; resolved from PATH by default.</summary>
    public string ExecutablePath { get; set; } = "blender";

    /// <summary>Repository-owned template directory; relative paths resolve against the app/output directory.</summary>
    public string TemplateDirectory { get; set; } = "Rendering/Blender/templates";

    /// <summary>Bound for one Blender process; 3D renders are much longer than image calls.</summary>
    public int TimeoutSeconds { get; set; } = 1800;

    public string RenderEngine { get; set; } = "CYCLES";

    public string ComputeBackend { get; set; } = "CUDA";

    public int Samples { get; set; } = 16;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public int FramesPerSecond { get; set; } = 30;
}
