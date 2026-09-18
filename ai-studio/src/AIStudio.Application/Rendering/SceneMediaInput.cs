using AIStudio.Domain.Assets;

namespace AIStudio.Application.Rendering;

public sealed record SceneMediaInput(
    string AbsolutePath,
    AssetType Type,
    SceneMotion? Motion = null,
    SceneSoundEffect? SoundEffect = null,
    SceneVisualKind? VisualKind = null,
    double Weight = 1);
