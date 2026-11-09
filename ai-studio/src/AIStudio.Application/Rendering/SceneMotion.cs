namespace AIStudio.Application.Rendering;

/// <summary>
/// Deterministic motion treatment applied to a static scene image.
/// </summary>
public enum SceneMotion
{
    None,
    SlowZoomIn,
    SlowZoomOut,
    PanLeft,
    PanRight
}

/// <summary>
/// Deterministic motion cycles. The AiImage narrative cycle gives a generate-time
/// still real camera movement without any new model: zoom in, pan left, zoom out,
/// pan right, repeating by scene index.
/// </summary>
public static class SceneMotionCycles
{
    public static readonly SceneMotion[] AiImageNarrative =
    [
        SceneMotion.SlowZoomIn,
        SceneMotion.PanLeft,
        SceneMotion.SlowZoomOut,
        SceneMotion.PanRight
    ];

    public static SceneMotion ForAiImageNarrative(int sceneIndex) =>
        AiImageNarrative[Math.Abs(sceneIndex) % AiImageNarrative.Length];
}
