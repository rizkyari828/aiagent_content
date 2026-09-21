using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Narrow, deterministic structural validation for a character bible and its
/// scene-level state. It checks identity/trait/variant/relationship structure and
/// that a state only uses an approved variant — it never scores likeness, compares
/// images, calls a model, or resolves assets. Identity and mutable state are
/// validated separately so scene state can change freely without touching identity.
/// </summary>
public static class CharacterBibleValidator
{
    public const int MaximumTextLength = 2_000;

    public static IReadOnlyList<BibleIssue> Validate(CharacterBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);

        var issues = new List<BibleIssue>();

        if (!StoryIdentifier.IsValid(bible.Id.Value))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterBibleIdInvalid,
                "A character bible id is required and must be a lowercase identifier such as 'rio'."));
        }

        if (!bible.Version.IsValid)
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterBibleVersionInvalid,
                $"A character bible version must be at least {CharacterBibleVersion.Minimum}."));
        }

        if (string.IsNullOrWhiteSpace(bible.DisplayName))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterDisplayNameEmpty,
                "A character display name is required."));
        }

        ValidateIdentity(bible.Identity ?? new CharacterIdentity(), issues);
        ValidateTextList(
            bible.PersonalityTraits,
            BibleIssueCodes.CharacterTraitInvalid,
            BibleIssueCodes.CharacterTraitDuplicate,
            "personality trait",
            issues);

        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in bible.Variants ?? [])
        {
            if (variant is null || !StoryIdentifier.IsValid(variant.Id))
            {
                issues.Add(Issue(
                    BibleIssueCodes.CharacterVariantIdInvalid,
                    "Every character variant must declare a valid lowercase id such as 'winter-jacket'."));

                continue;
            }

            if (!declared.Add(variant.Id))
            {
                issues.Add(Issue(
                    BibleIssueCodes.CharacterVariantDuplicate,
                    $"Character variant '{variant.Id}' is declared more than once."));
            }
        }

        if (!StoryIdentifier.IsValid(bible.BaselineVariant))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterBaselineVariantInvalid,
                "A character baseline variant must be a lowercase identifier such as 'default'."));
        }
        else if (bible.BaselineVariant != CharacterBible.DefaultVariant
            && !declared.Contains(bible.BaselineVariant))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterBaselineVariantInvalid,
                $"Character baseline variant '{bible.BaselineVariant}' is not a declared variant."));
        }

        foreach (var relationship in bible.Relationships ?? [])
        {
            if (relationship is null)
            {
                continue;
            }

            if (!StoryIdentifier.IsValid(relationship.Target.Value))
            {
                issues.Add(Issue(
                    BibleIssueCodes.CharacterRelationshipTargetInvalid,
                    "A character relationship must reference a valid target character id."));
            }
            else if (relationship.Target == bible.Id)
            {
                issues.Add(Issue(
                    BibleIssueCodes.CharacterRelationshipSelfReference,
                    "A character cannot have a relationship to itself."));
            }

            if (!StoryIdentifier.IsValid(relationship.Type))
            {
                issues.Add(Issue(
                    BibleIssueCodes.CharacterRelationshipTypeInvalid,
                    "A character relationship must declare a data-driven type such as 'sibling'."));
            }
        }

        ValidateAssetReferences(bible.AssetReferences, issues);

        return issues;
    }

    public static IReadOnlyList<BibleIssue> Validate(CharacterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var issues = new List<BibleIssue>();

        if (!StoryIdentifier.IsValid(state.CharacterRef.Value))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterStateCharacterRefInvalid,
                "A character state must reference a valid character id."));
        }

        if (!StoryIdentifier.IsValid(state.Variant))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterStateVariantInvalid,
                "A character state variant must be a lowercase identifier such as 'default'."));
        }

        ValidateOptionalToken(state.Emotion, BibleIssueCodes.CharacterStateTokenInvalid, issues);
        ValidateOptionalToken(state.Pose, BibleIssueCodes.CharacterStateTokenInvalid, issues);
        ValidateOptionalToken(state.Action, BibleIssueCodes.CharacterStateTokenInvalid, issues);

        if (state.WorldRef is { } world && !StoryIdentifier.IsValid(world.Value))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterStateWorldRefInvalid,
                "A character state world reference must be a valid world id."));
        }

        ValidateTokens(
            state.HeldProps,
            BibleIssueCodes.CharacterStateTokenInvalid,
            duplicateCode: null,
            "held prop",
            issues);

        return issues;
    }

    /// <summary>
    /// Structural state validation plus the identity rule that a state may only use
    /// an approved variant (the implicit <c>default</c> or a declared variant).
    /// </summary>
    public static IReadOnlyList<BibleIssue> Validate(CharacterState state, CharacterBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);

        var issues = new List<BibleIssue>(Validate(state));

        if (StoryIdentifier.IsValid(state.Variant) && !bible.IsVariantAllowed(state.Variant))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterStateVariantUnknown,
                $"Character '{bible.Id}' has no approved variant '{state.Variant}'."));
        }

        return issues;
    }

    private static void ValidateIdentity(CharacterIdentity identity, List<BibleIssue> issues)
    {
        if (!StoryIdentifier.IsValid(identity.Role))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterRoleInvalid,
                "A character identity must declare a data-driven role such as 'protagonist'."));
        }

        ValidateOptionalToken(identity.Species, BibleIssueCodes.CharacterIdentityTokenInvalid, issues);
        ValidateOptionalToken(identity.AgePresentation, BibleIssueCodes.CharacterIdentityTokenInvalid, issues);
        ValidateOptionalToken(identity.BodyStyle, BibleIssueCodes.CharacterIdentityTokenInvalid, issues);
        ValidateOptionalToken(identity.Hair, BibleIssueCodes.CharacterIdentityTokenInvalid, issues);
        ValidateOptionalToken(identity.Eyes, BibleIssueCodes.CharacterIdentityTokenInvalid, issues);

        if (identity.VisualDescription is not null
            && (identity.VisualDescription.Trim().Length == 0
                || identity.VisualDescription.Trim().Length > MaximumTextLength))
        {
            issues.Add(Issue(
                BibleIssueCodes.CharacterVisualDescriptionInvalid,
                $"A character visual description must contain 1 to {MaximumTextLength} characters when provided."));
        }

        ValidateTextList(
            identity.DistinguishingTraits,
            BibleIssueCodes.CharacterTraitInvalid,
            BibleIssueCodes.CharacterTraitDuplicate,
            "distinguishing trait",
            issues);
    }

    internal static void ValidateAssetReferences(
        IReadOnlyList<AssetReference>? references,
        List<BibleIssue> issues)
    {
        var seen = new HashSet<(string AssetId, string Purpose)>();

        foreach (var reference in references ?? [])
        {
            if (reference is null)
            {
                continue;
            }

            if (!StoryIdentifier.IsValid(reference.AssetId.Value))
            {
                issues.Add(Issue(
                    BibleIssueCodes.AssetReferenceIdInvalid,
                    "An asset reference must declare a valid asset id such as 'character-rio-front-v1'."));
            }

            if (!StoryIdentifier.IsValid(reference.Purpose))
            {
                issues.Add(Issue(
                    BibleIssueCodes.AssetReferencePurposeInvalid,
                    "An asset reference must declare a data-driven purpose such as 'visual-reference'."));
            }

            if (reference.Variant is not null && !StoryIdentifier.IsValid(reference.Variant))
            {
                issues.Add(Issue(
                    BibleIssueCodes.AssetReferenceVariantInvalid,
                    "An asset reference variant must be a lowercase identifier such as 'winter-jacket'."));
            }

            if (!seen.Add((reference.AssetId.Value, reference.Purpose)))
            {
                issues.Add(Issue(
                    BibleIssueCodes.AssetReferenceDuplicate,
                    $"Asset reference '{reference.AssetId}' for purpose '{reference.Purpose}' is declared more than once."));
            }
        }
    }

    internal static void ValidateTokens(
        IReadOnlyList<string>? values,
        string invalidCode,
        string? duplicateCode,
        string description,
        List<BibleIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values ?? [])
        {
            if (!StoryIdentifier.IsValid(value))
            {
                issues.Add(Issue(invalidCode, $"Every {description} must be a lowercase identifier such as 'curious'."));

                continue;
            }

            if (duplicateCode is not null && !seen.Add(value))
            {
                issues.Add(Issue(duplicateCode, $"Value '{value}' is declared more than once."));
            }
        }
    }

    /// <summary>
    /// Validates free-text descriptive lists (traits, spatial traits). These are
    /// human-readable data, not machine identifiers, so they only need to be
    /// non-empty and bounded; duplicates are still rejected.
    /// </summary>
    internal static void ValidateTextList(
        IReadOnlyList<string>? values,
        string invalidCode,
        string? duplicateCode,
        string description,
        List<BibleIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaximumTextLength)
            {
                issues.Add(Issue(
                    invalidCode,
                    $"Every {description} must contain 1 to {MaximumTextLength} characters."));

                continue;
            }

            if (duplicateCode is not null && !seen.Add(value.Trim()))
            {
                issues.Add(Issue(duplicateCode, $"Value '{value}' is declared more than once."));
            }
        }
    }

    internal static void ValidateOptionalToken(string? value, string code, List<BibleIssue> issues)
    {
        if (value is not null && !StoryIdentifier.IsValid(value))
        {
            issues.Add(Issue(code, $"'{value}' must be a lowercase identifier or omitted."));
        }
    }

    private static BibleIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
