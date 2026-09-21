namespace AIStudio.Application.Concepts;

/// <summary>
/// One deterministic structural problem found in a concept manifest. A manifest
/// with no issues is valid data even when its referenced recipe cannot currently
/// be produced: missing production capability is a resolution concern, not a
/// malformed concept.
/// </summary>
public sealed record ConceptValidationIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable validation codes, safe to surface to future data loaders.</summary>
public static class ConceptValidationIssueCodes
{
    public const string ConceptIdInvalid = "concept_id_invalid";
    public const string ConceptVersionInvalid = "concept_version_invalid";
    public const string TitleEmpty = "concept_title_empty";
    public const string FormatInvalid = "concept_format_invalid";
    public const string StyleInvalid = "concept_style_invalid";
    public const string RecipeIdInvalid = "concept_recipe_id_invalid";
    public const string DurationInvalid = "concept_duration_invalid";
    public const string TagInvalid = "concept_tag_invalid";
    public const string TagDuplicate = "concept_tag_duplicate";
}
