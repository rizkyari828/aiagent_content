namespace AIStudio.Infrastructure.Rendering;

/// <summary>
/// Configuration for the optional local ComfyUI image-to-video engine (Image-to-Video,
/// I2V). ComfyUI is an external local service, not a bundled dependency. The engine is
/// disabled by default and no production path selects it in v1.
/// </summary>
public sealed class ComfyUiVideoOptions
{
    public const string SectionName = "ComfyUiVideo";

    public bool Enabled { get; set; }

    /// <summary>Loopback ComfyUI base URL.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8188";

    /// <summary>
    /// Repository-owned trusted workflow in ComfyUI API format. It is the only graph
    /// ever submitted. The file does not exist yet: the real graph must be authored and
    /// verified on the GPU machine (P1.6/P3.1), so a missing file reports
    /// <c>video_not_configured</c> and never falls back to an invented graph.
    /// </summary>
    public string WorkflowPath { get; set; } = "Rendering/ComfyUI/i2v_workflow.json";

    /// <summary>
    /// Repository-owned trusted workflow for a request that carries one pinned
    /// first-frame reference. Kept separate from <see cref="WorkflowPath"/> exactly
    /// like the image engine's text-to-image and edit graphs, so a zero-reference
    /// request never submits a graph with an unresolved image placeholder.
    /// </summary>
    public string FirstFrameWorkflowPath { get; set; } =
        "Rendering/ComfyUI/i2v_workflow_first_frame.json";

    /// <summary>
    /// Approved-root-relative directory for generated clips. The filename is derived
    /// from a deterministic request fingerprint; callers never supply a path.
    /// </summary>
    public string OutputDirectory { get; set; } = "renders/i2v";

    /// <summary>Trusted upper bound for request width.</summary>
    public int Width { get; set; } = 1280;

    /// <summary>Trusted upper bound for request height.</summary>
    public int Height { get; set; } = 720;

    /// <summary>Trusted upper bound for request frame rate.</summary>
    public int Fps { get; set; } = 24;

    /// <summary>Trusted upper bound for request clip length.</summary>
    public int MaxSeconds { get; set; } = 5;

    public int TimeoutSeconds { get; set; } = 1800;
}
