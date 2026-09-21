using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Narrow, deterministic structural validation for a concept manifest. It checks
/// only the manifest's own declarative data — identity, title, format/style tokens,
/// recipe reference, duration bounds and tags — and never consults the recipe or
/// capability registries. It performs no subjective quality, trend, or
/// profitability scoring.
/// </summary>
public static class ConceptValidator
{
    public const int MinimumDurationSeconds = 1;
    public const int MaximumDurationSeconds = 86_400;

    public static IReadOnlyList<ConceptValidationIssue> Validate(ConceptManifest concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        var issues = new List<ConceptValidationIssue>();

        if (!ConceptIdentifier.IsValid(concept.Id.Value))
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.ConceptIdInvalid,
                "A concept id is required and must be a lowercase identifier such as 'local-ai-tech-explainer'."));
        }

        if (!concept.Version.IsValid)
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.ConceptVersionInvalid,
                $"A concept version must be at least {ConceptVersion.Minimum}."));
        }

        if (string.IsNullOrWhiteSpace(concept.Title))
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.TitleEmpty,
                "A concept title is required."));
        }

        if (!ConceptIdentifier.IsValid(concept.Format))
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.FormatInvalid,
                "A concept format is required and must be a lowercase identifier such as 'youtube-short'."));
        }

        if (!ConceptIdentifier.IsValid(concept.Style))
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.StyleInvalid,
                "A concept style is required and must be a lowercase identifier such as 'clean-tech'."));
        }

        if (!ProductionRecipeId.IsValid(concept.RecipeId.Value))
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.RecipeIdInvalid,
                "A concept must reference a valid production recipe id."));
        }

        if (concept.Duration is < MinimumDurationSeconds or > MaximumDurationSeconds)
        {
            issues.Add(Issue(
                ConceptValidationIssueCodes.DurationInvalid,
                $"A concept duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds."));
        }

        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in concept.Tags ?? [])
        {
            if (!ConceptIdentifier.IsValid(tag))
            {
                issues.Add(Issue(
                    ConceptValidationIssueCodes.TagInvalid,
                    $"Tag '{tag}' must be a lowercase identifier."));

                continue;
            }

            if (!seenTags.Add(tag))
            {
                issues.Add(Issue(
                    ConceptValidationIssueCodes.TagDuplicate,
                    $"Tag '{tag}' is declared more than once."));
            }
        }

        return issues;
    }

    private static ConceptValidationIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
