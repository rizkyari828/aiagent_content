namespace AIStudio.Application.Rendering;

/// <summary>
/// Zero or one optional sound effect layered for a single scene.
/// </summary>
public sealed record SceneSoundEffect(
    string AbsolutePath,
    double StartOffsetSeconds = 0,
    double Volume = 0.5);
