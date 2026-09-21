using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Concepts;

namespace AIStudio.Application.Creative;

/// <summary>
/// Parses untrusted model output into a validated <see cref="CreativeDirection"/>.
/// It accepts valid JSON only, rejects malformed JSON and missing concept/treatment
/// sections, maps the concept portion to the existing <see cref="ConceptManifest"/>
/// (reusing <see cref="ConceptValidator"/>), and validates the treatment
/// structurally. It performs no speculative JSON repair and executes nothing.
/// </summary>
public static class CreativeDirectionParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static CreativeDirection Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionInvalidJson,
                "Creative director response was empty.");
        }

        CreativeDirectionDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<CreativeDirectionDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionInvalidJson,
                "Creative director response is not valid JSON for a creative direction.",
                exception);
        }

        if (document is null)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionInvalidJson,
                "Creative director response did not contain a creative direction.");
        }

        if (document.Concept is null)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionMissingConcept,
                "Creative director response is missing the concept section.");
        }

        if (document.Treatment is null)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionMissingTreatment,
                "Creative director response is missing the treatment section.");
        }

        var conceptIssues = ConceptValidator.Validate(document.Concept);
        if (conceptIssues.Count > 0)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionInvalidConcept,
                $"Creative director concept is invalid: {conceptIssues[0].Code}.");
        }

        var treatmentIssues = CreativeTreatmentValidator.Validate(document.Treatment);
        if (treatmentIssues.Count > 0)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.DirectionInvalidTreatment,
                $"Creative director treatment is invalid: {treatmentIssues[0].Code}.");
        }

        return new CreativeDirection
        {
            IdeaReference = string.IsNullOrWhiteSpace(document.IdeaReference)
                ? null
                : document.IdeaReference.Trim(),
            Concept = document.Concept,
            Treatment = document.Treatment
        };
    }

    internal sealed record CreativeDirectionDocument
    {
        [JsonPropertyName("ideaReference")]
        public string? IdeaReference { get; init; }

        [JsonPropertyName("concept")]
        public ConceptManifest? Concept { get; init; }

        [JsonPropertyName("treatment")]
        public CreativeTreatment? Treatment { get; init; }
    }
}
