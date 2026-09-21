using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Additive continuity validation that checks a <see cref="StoryPlan"/>'s
/// character/world references against the trusted bible registries. It never
/// changes the Story Director or the plan: it only reports whether the referenced
/// identities are known, so a plan without bibles stays usable and this stays an
/// opt-in check. No model, image comparison, or provider is involved.
/// </summary>
public static class StoryContinuityValidator
{
    public static IReadOnlyList<BibleIssue> Validate(
        StoryPlan plan,
        ICharacterBibleRegistry characters,
        IWorldBibleRegistry worlds)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(worlds);

        var issues = new List<BibleIssue>();
        var unknownCharacters = new HashSet<string>(StringComparer.Ordinal);
        var unknownWorlds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var beat in plan.Beats ?? [])
        {
            if (beat is null)
            {
                continue;
            }

            foreach (var reference in beat.CharacterRefs ?? [])
            {
                if (!StoryIdentifier.IsValid(reference) || !unknownCharacters.Add(reference))
                {
                    continue;
                }

                if (!characters.TryGetLatest(new CharacterBibleId(reference), out _))
                {
                    issues.Add(new BibleIssue
                    {
                        Code = BibleIssueCodes.StoryCharacterReferenceUnknown,
                        Message = $"Story beat '{beat.Id}' references unknown character '{reference}'."
                    });
                }
            }

            foreach (var reference in beat.WorldRefs ?? [])
            {
                if (!StoryIdentifier.IsValid(reference) || !unknownWorlds.Add(reference))
                {
                    continue;
                }

                if (!worlds.TryGetLatest(new WorldBibleId(reference), out _))
                {
                    issues.Add(new BibleIssue
                    {
                        Code = BibleIssueCodes.StoryWorldReferenceUnknown,
                        Message = $"Story beat '{beat.Id}' references unknown world '{reference}'."
                    });
                }
            }
        }

        return issues;
    }
}
