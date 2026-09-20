namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Structured, pre-validated inputs for a Blender template. Data only: the request
/// never carries Python source, shell fragments, command arguments, filesystem
/// paths, or node graphs. Resolution, frame rate, samples, render engine, and
/// compute backend come from configuration, never from the caller.
/// </summary>
public sealed record ThreeDRenderRequest(
    SceneThreeDTemplate Template,
    SceneVisualPalette Palette,
    double DurationSeconds,
    long Seed);
