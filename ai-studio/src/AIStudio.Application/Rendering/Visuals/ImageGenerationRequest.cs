namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// One scene image request. Kept deliberately small: the prompt plus a stable seed
/// so a retry of the same scene reproduces the same composition. Output dimensions
/// belong to the provider configuration, not the caller.
/// </summary>
public sealed record ImageGenerationRequest(string Prompt, long Seed);
