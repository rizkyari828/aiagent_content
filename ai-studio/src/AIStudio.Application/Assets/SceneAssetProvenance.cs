namespace AIStudio.Application.Assets;

/// <summary>
/// Provenance markers for assets AI Studio generates itself. A single prefix keeps
/// persistence and downstream wiring in agreement without a schema change; the
/// current suffix records which visual-direction version produced the asset so an
/// older static generation is regenerated once while a current one is reused.
/// </summary>
public static class SceneAssetProvenance
{
    /// <summary>Recorded as the asset source when the Visual Asset Engine produced it.</summary>
    public const string GeneratedVisualSource = "ai-studio:scene-visual-engine";

    /// <summary>Visual Direction + Multi-Engine Choreography v1.</summary>
    public const string VisualDirectionVersion = "motion-v1";

    /// <summary>Source value for assets produced by the current visual pipeline.</summary>
    public const string CurrentGeneratedVisualSource =
        GeneratedVisualSource + ":" + VisualDirectionVersion;

    /// <summary>
    /// Any engine-produced visual is text-heavy/interface content, so the transition
    /// policy must fade through background rather than crossfade. Manually
    /// registered assets return <c>false</c> and keep photographic rules.
    /// </summary>
    public static bool IsGeneratedGraphic(string? source) =>
        source is not null
        && source.StartsWith(GeneratedVisualSource, StringComparison.Ordinal);

    /// <summary>
    /// True only when the asset was produced by the current visual-direction
    /// version, so the handler can reuse it instead of regenerating.
    /// </summary>
    public static bool IsCurrentGeneratedVisual(string? source) =>
        string.Equals(source, CurrentGeneratedVisualSource, StringComparison.Ordinal);
}
