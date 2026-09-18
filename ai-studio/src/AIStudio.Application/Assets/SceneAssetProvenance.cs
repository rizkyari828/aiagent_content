namespace AIStudio.Application.Assets;

/// <summary>
/// Provenance markers for assets AI Studio generates itself. A single constant
/// keeps persistence and downstream wiring in agreement without a schema change.
/// </summary>
public static class SceneAssetProvenance
{
    /// <summary>Recorded as the asset source when the Visual Asset Engine produced it.</summary>
    public const string GeneratedVisualSource = "ai-studio:scene-visual-engine";

    /// <summary>
    /// A programmatically generated visual is text-heavy/interface content, so the
    /// transition policy must fade through background rather than crossfade.
    /// Manually registered assets return <c>false</c> and keep photographic rules.
    /// </summary>
    public static bool IsGeneratedGraphic(string? source) =>
        string.Equals(source, GeneratedVisualSource, StringComparison.Ordinal);
}
