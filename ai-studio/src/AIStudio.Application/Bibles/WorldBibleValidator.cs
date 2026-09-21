using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Narrow, deterministic structural validation for a world bible and its
/// scene-level state. It checks environment identity, recurring props, continuity
/// rules, locations, and asset references, and validates mutable state tokens
/// separately — it never renders, compares images, calls a model, or resolves
/// assets. Identity stays stable while state may legitimately change.
/// </summary>
public static class WorldBibleValidator
{
    public static IReadOnlyList<BibleIssue> Validate(WorldBible world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var issues = new List<BibleIssue>();

        if (!StoryIdentifier.IsValid(world.Id.Value))
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldBibleIdInvalid,
                "A world bible id is required and must be a lowercase identifier such as 'rio-bedroom'."));
        }

        if (!world.Version.IsValid)
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldBibleVersionInvalid,
                $"A world bible version must be at least {WorldBibleVersion.Minimum}."));
        }

        if (string.IsNullOrWhiteSpace(world.DisplayName))
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldDisplayNameEmpty,
                "A world display name is required."));
        }

        var identity = world.Identity ?? new WorldIdentity();

        if (!StoryIdentifier.IsValid(identity.EnvironmentType))
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldEnvironmentTypeInvalid,
                "A world identity must declare a data-driven environment type such as 'bedroom'."));
        }

        if (string.IsNullOrWhiteSpace(identity.VisualDescription)
            || identity.VisualDescription.Trim().Length > CharacterBibleValidator.MaximumTextLength)
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldVisualDescriptionInvalid,
                $"A world visual description must contain 1 to {CharacterBibleValidator.MaximumTextLength} characters."));
        }

        CharacterBibleValidator.ValidateTextList(
            identity.SpatialTraits,
            BibleIssueCodes.WorldSpatialTraitInvalid,
            duplicateCode: null,
            "spatial trait",
            issues);

        CharacterBibleValidator.ValidateTokens(
            world.RecurringProps,
            BibleIssueCodes.WorldRecurringPropInvalid,
            BibleIssueCodes.WorldRecurringPropDuplicate,
            "recurring prop",
            issues);

        foreach (var rule in world.ContinuityRules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule)
                || rule.Trim().Length > CharacterBibleValidator.MaximumTextLength)
            {
                issues.Add(Issue(
                    BibleIssueCodes.WorldContinuityRuleInvalid,
                    $"Every continuity rule must contain 1 to {CharacterBibleValidator.MaximumTextLength} characters."));
            }
        }

        var seenLocations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in world.Locations ?? [])
        {
            if (location is null || !StoryIdentifier.IsValid(location.Id))
            {
                issues.Add(Issue(
                    BibleIssueCodes.WorldLocationIdInvalid,
                    "Every world location must declare a valid lowercase id such as 'classroom'."));

                continue;
            }

            if (!seenLocations.Add(location.Id))
            {
                issues.Add(Issue(
                    BibleIssueCodes.WorldLocationDuplicate,
                    $"World location '{location.Id}' is declared more than once."));
            }
        }

        CharacterBibleValidator.ValidateAssetReferences(world.AssetReferences, issues);

        return issues;
    }

    public static IReadOnlyList<BibleIssue> Validate(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var issues = new List<BibleIssue>();

        if (!StoryIdentifier.IsValid(state.WorldRef.Value))
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldStateWorldRefInvalid,
                "A world state must reference a valid world id."));
        }

        CharacterBibleValidator.ValidateOptionalToken(state.TimeOfDay, BibleIssueCodes.WorldStateTokenInvalid, issues);
        CharacterBibleValidator.ValidateOptionalToken(state.Weather, BibleIssueCodes.WorldStateTokenInvalid, issues);
        CharacterBibleValidator.ValidateOptionalToken(state.Lighting, BibleIssueCodes.WorldStateTokenInvalid, issues);

        CharacterBibleValidator.ValidateTokens(
            state.TemporaryProps,
            BibleIssueCodes.WorldStateTemporaryPropInvalid,
            duplicateCode: null,
            "temporary prop",
            issues);

        if (state.Notes is not null
            && (state.Notes.Trim().Length == 0
                || state.Notes.Trim().Length > CharacterBibleValidator.MaximumTextLength))
        {
            issues.Add(Issue(
                BibleIssueCodes.WorldStateNotesInvalid,
                $"World state notes must contain 1 to {CharacterBibleValidator.MaximumTextLength} characters when provided."));
        }

        return issues;
    }

    private static BibleIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
