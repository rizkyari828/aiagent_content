namespace AIStudio.Application.Rendering;

public sealed record VideoRenderRequest(
    IReadOnlyList<SceneMediaInput> Scenes,
    string NarrationAbsolutePath,
    string RelativeOutputPath);
