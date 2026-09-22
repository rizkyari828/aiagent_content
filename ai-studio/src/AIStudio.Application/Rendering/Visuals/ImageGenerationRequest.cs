using System.Collections.ObjectModel;
using AIStudio.Application.IdentityAssets;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// One scene image request. Identity references are already materialized and pinned:
/// execution never resolves a floating latest version. Output dimensions belong to
/// provider configuration, not the caller.
/// </summary>
public sealed record ImageGenerationRequest
{
    public ImageGenerationRequest(
        string prompt,
        long seed,
        IEnumerable<PinnedIdentityAsset>? identityReferences = null)
    {
        var references = (identityReferences ?? []).ToArray();
        if (references.Length > 1)
        {
            throw new NotSupportedException(
                "Image generation supports at most one identity reference in v1.");
        }

        if (references.Any(reference => reference is null))
        {
            throw new ArgumentException(
                "Identity references cannot contain null values.",
                nameof(identityReferences));
        }

        Prompt = prompt;
        Seed = seed;
        IdentityReferences = new ReadOnlyCollection<PinnedIdentityAsset>(references);
    }

    public string Prompt { get; }

    public long Seed { get; }

    public IReadOnlyList<PinnedIdentityAsset> IdentityReferences { get; }
}
