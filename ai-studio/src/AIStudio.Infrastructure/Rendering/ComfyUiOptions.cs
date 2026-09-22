namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Configuration for the optional local ComfyUI image engine. ComfyUI is an
/// external local service, not a bundled dependency: when <see cref="Enabled"/> is
/// false the planner keeps every still scene on the deterministic SVG engine.
/// </summary>
public sealed class ComfyUiOptions
{
    public const string SectionName = "ComfyUi";

    public bool Enabled { get; set; }

    /// <summary>Loopback ComfyUI base URL.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";

    /// <summary>
    /// Repository-owned approved workflow in ComfyUI API format. It is the only
    /// graph ever submitted; the provider injects prompt/dimensions/seed only.
    /// </summary>
    public string WorkflowPath { get; set; } = "Rendering/ComfyUI/flux2_klein_4b_distilled.json";

    /// <summary>
    /// Repository-owned image-edit graph used only for one approved pinned
    /// identity reference.
    /// </summary>
    public string ImageEditWorkflowPath { get; set; } =
        "Rendering/ComfyUI/flux2_klein_4b_distilled_edit.json";

    public int TimeoutSeconds { get; set; } = 600;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;
}
