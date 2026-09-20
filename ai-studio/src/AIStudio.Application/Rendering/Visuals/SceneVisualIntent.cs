namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// What a scene is communicating. The director maps intent to a preferred engine;
/// the router then resolves provider availability. Intent is derived
/// deterministically from the canonical heading and visual direction text.
/// </summary>
public enum SceneVisualIntent
{
    Generic = 0,
    Opening = 1,
    TechnicalFlow = 2,
    Requirements = 3,
    DownloadInstall = 4,
    ModelPull = 5,
    Conversation = 6,
    Tips = 7,
    Closing = 8
}

/// <summary>
/// The composition pattern for a scene, independent of the engine that renders it.
/// </summary>
public enum SceneVisualComposition
{
    Hero = 0,
    Cards = 1,
    Terminal = 2,
    Chat = 3,
    DataFlow = 4,
    Device3D = 5,
    Illustration = 6
}
