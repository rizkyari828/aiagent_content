using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Strict parser and deterministic validator for an untrusted story bible proposal.
/// It accepts valid JSON only (unknown members rejected), reuses the existing bible
/// and grounding validators, resolves grounding references against the bibles in the
/// SAME proposal, and never repairs, registers, or executes anything. Registration
/// and application happen later, explicitly.
/// </summary>
public static class StoryBiblePlanParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static StoryBiblePlan Parse(string json, StoryPlan storyPlan)
    {
        ArgumentNullException.ThrowIfNull(storyPlan);

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new StoryBiblePlanningException(
                StoryBiblePlanningErrorCodes.InvalidJson,
                "Story bible proposal was empty.");
        }

        StoryBiblePlanDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<StoryBiblePlanDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new StoryBiblePlanningException(
                StoryBiblePlanningErrorCodes.InvalidJson,
                "Story bible proposal is not valid JSON for a bible plan.",
                exception);
        }

        if (document is null)
        {
            throw new StoryBiblePlanningException(
                StoryBiblePlanningErrorCodes.InvalidJson,
                "Story bible proposal did not contain a bible plan.");
        }

        var characterBibles = document.CharacterBibles ?? [];
        var worldBibles = document.WorldBibles ?? [];
        var beatGroundings = document.BeatGroundings ?? [];

        var characterIds = ValidateCharacterBibles(characterBibles);
        var worldIds = ValidateWorldBibles(worldBibles);
        ValidateRelationships(characterBibles, characterIds);
        ValidateGroundings(beatGroundings, storyPlan, characterIds, worldIds);

        return new StoryBiblePlan
        {
            CharacterBibles = characterBibles,
            WorldBibles = worldBibles,
            BeatGroundings = beatGroundings
        };
    }

    private static HashSet<string> ValidateCharacterBibles(IReadOnlyList<CharacterBible> bibles)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var bible in bibles)
        {
            if (bible is null)
            {
                throw Invalid("A proposed character bible is missing.");
            }

            var issues = CharacterBibleValidator.Validate(bible);
            if (issues.Count > 0)
            {
                throw Invalid($"Proposed character bible is invalid: {issues[0].Code}.");
            }

            if (!ids.Add(bible.Id.Value))
            {
                throw new StoryBiblePlanningException(
                    StoryBiblePlanningErrorCodes.DuplicateCharacter,
                    $"Character bible '{bible.Id}' is proposed more than once.");
            }
        }

        return ids;
    }

    private static HashSet<string> ValidateWorldBibles(IReadOnlyList<WorldBible> bibles)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var bible in bibles)
        {
            if (bible is null)
            {
                throw Invalid("A proposed world bible is missing.");
            }

            var issues = WorldBibleValidator.Validate(bible);
            if (issues.Count > 0)
            {
                throw Invalid($"Proposed world bible is invalid: {issues[0].Code}.");
            }

            if (!ids.Add(bible.Id.Value))
            {
                throw new StoryBiblePlanningException(
                    StoryBiblePlanningErrorCodes.DuplicateWorld,
                    $"World bible '{bible.Id}' is proposed more than once.");
            }
        }

        return ids;
    }

    private static void ValidateRelationships(
        IReadOnlyList<CharacterBible> bibles,
        HashSet<string> characterIds)
    {
        foreach (var bible in bibles)
        {
            foreach (var relationship in bible.Relationships ?? [])
            {
                if (relationship is null
                    || !characterIds.Contains(relationship.Target.Value))
                {
                    throw new StoryBiblePlanningException(
                        StoryBiblePlanningErrorCodes.RelationshipUnknown,
                        $"Character '{bible.Id}' has a relationship to a character that is not part of this proposal.");
                }
            }
        }
    }

    private static void ValidateGroundings(
        IReadOnlyList<StoryBeatGrounding> groundings,
        StoryPlan storyPlan,
        HashSet<string> characterIds,
        HashSet<string> worldIds)
    {
        var planBeatIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var beat in storyPlan.Beats ?? [])
        {
            if (beat is not null)
            {
                planBeatIds.Add(beat.Id.Value);
            }
        }

        var seenBeatIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var grounding in groundings)
        {
            if (grounding is null)
            {
                throw Invalid("A proposed beat grounding is missing.");
            }

            var beatId = grounding.BeatId.Value;
            if (!StoryIdentifier.IsValid(beatId) || !planBeatIds.Contains(beatId))
            {
                throw new StoryBiblePlanningException(
                    StoryBiblePlanningErrorCodes.BeatUnknown,
                    $"Beat grounding references unknown beat '{beatId}'.");
            }

            if (!seenBeatIds.Add(beatId))
            {
                throw new StoryBiblePlanningException(
                    StoryBiblePlanningErrorCodes.BeatDuplicate,
                    $"Beat '{beatId}' is grounded more than once.");
            }

            foreach (var reference in grounding.CharacterRefs ?? [])
            {
                if (!CharacterBibleId.TryParse(reference, out var characterId)
                    || !characterIds.Contains(characterId.Value))
                {
                    throw new StoryBiblePlanningException(
                        StoryBiblePlanningErrorCodes.CharacterRefUnknown,
                        $"Beat '{beatId}' references character '{reference}' that is not part of this proposal.");
                }
            }

            foreach (var reference in grounding.WorldRefs ?? [])
            {
                if (!WorldBibleId.TryParse(reference, out var worldId)
                    || !worldIds.Contains(worldId.Value))
                {
                    throw new StoryBiblePlanningException(
                        StoryBiblePlanningErrorCodes.WorldRefUnknown,
                        $"Beat '{beatId}' references world '{reference}' that is not part of this proposal.");
                }
            }
        }
    }

    private static StoryBiblePlanningException Invalid(string message) =>
        new(StoryBiblePlanningErrorCodes.PlanInvalid, message);

    internal sealed record StoryBiblePlanDocument
    {
        [JsonPropertyName("characterBibles")]
        public IReadOnlyList<CharacterBible>? CharacterBibles { get; init; }

        [JsonPropertyName("worldBibles")]
        public IReadOnlyList<WorldBible>? WorldBibles { get; init; }

        [JsonPropertyName("beatGroundings")]
        public IReadOnlyList<StoryBeatGrounding>? BeatGroundings { get; init; }
    }
}
