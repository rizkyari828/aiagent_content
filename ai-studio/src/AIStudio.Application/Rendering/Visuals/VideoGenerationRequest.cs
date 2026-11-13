using AIStudio.Application.IdentityAssets;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// One scene animation request. The optional first-frame reference is already
/// materialized and pinned: execution never resolves a floating latest version.
/// Output paths, workflow files, model names, and node graphs belong to provider
/// configuration, never to the caller. Structural validation here is deliberately
/// minimal; execution bounds are enforced by the provider against its own options.
/// </summary>
public sealed record VideoGenerationRequest(
    string Prompt,
    int DurationSeconds,
    int FramesPerSecond,
    int Width,
    int Height,
    long Seed,
    PinnedIdentityAsset? FirstFrame = null,
    string? Provider = null);
