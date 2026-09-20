namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Predefined, repository-owned Blender scene templates. A planner or model may
/// only choose one of these names; the Python belongs to the repository. An
/// unsupported value is rejected at the Blender provider boundary.
/// </summary>
public enum SceneThreeDTemplate
{
    None = 0,

    /// <summary>Deterministic local-AI laptop-on-a-desk scene with a short camera orbit.</summary>
    LocalAiLaptop = 1
}
