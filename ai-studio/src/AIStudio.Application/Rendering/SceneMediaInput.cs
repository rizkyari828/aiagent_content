using AIStudio.Domain.Assets;

namespace AIStudio.Application.Rendering;

public sealed record SceneMediaInput(string AbsolutePath, AssetType Type);
