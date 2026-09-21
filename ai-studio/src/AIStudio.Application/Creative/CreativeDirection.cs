using System.Text.Json.Serialization;
using AIStudio.Application.Concepts;

namespace AIStudio.Application.Creative;

/// <summary>
/// The Creative Director's output for one approved idea: a production-ready
/// <see cref="ConceptManifest"/> (WHAT it becomes) plus a small
/// <see cref="CreativeTreatment"/> (HOW it feels and flows). It is a proposal only
/// — nothing is registered, resolved into production, or executed here.
/// </summary>
public sealed record CreativeDirection
{
    /// <summary>Trace back to the approved idea this direction was derived from.</summary>
    [JsonPropertyName("ideaReference")]
    public string? IdeaReference { get; init; }

    [JsonPropertyName("concept")]
    public ConceptManifest Concept { get; init; } = new();

    [JsonPropertyName("treatment")]
    public CreativeTreatment Treatment { get; init; } = new();
}
