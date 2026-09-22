using AIStudio.Application.Bibles;
using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;
using AIStudio.Application.Stories;

namespace AIStudio.Application.StoryContext;

/// <summary>
/// Deterministic, AI-free context projector. It selects only the characters/worlds a
/// story (or one beat) actually references, deduplicates and orders them, reuses the
/// existing bible-continuity rules for reference validation, and projects compact
/// identity. It never invents or summarizes content, ranks, generates beats, chooses
/// engines, mutates its inputs, or executes a provider. Token efficiency comes from
/// relevance filtering, never unsafe truncation.
/// </summary>
public sealed class StoryContextBuilder : IStoryContextBuilder
{
    private readonly ICharacterBibleRegistry _characters;
    private readonly IWorldBibleRegistry _worlds;

    public StoryContextBuilder(ICharacterBibleRegistry characters, IWorldBibleRegistry worlds)
    {
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _worlds = worlds ?? throw new ArgumentNullException(nameof(worlds));
    }

    public StoryContextBuildResult Build(StoryContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var direction = request.CreativeDirection ?? new CreativeDirection();
        var plan = request.StoryPlan ?? new StoryPlan();
        var concept = direction.Concept ?? new ConceptManifest();
        var treatment = direction.Treatment ?? new CreativeTreatment();

        var issues = new List<StoryContextIssue>();

        var allBeats = (plan.Beats ?? []).Where(beat => beat is not null).ToList();
        if (allBeats.Count == 0)
        {
            issues.Add(Issue(
                StoryContextIssueCodes.RequestInvalid,
                "The story plan contains no beats to project."));
        }

        IReadOnlyList<StoryBeat> selectedBeats;

        if (request.BeatId is { } beatId)
        {
            var target = allBeats.FirstOrDefault(beat => beat.Id == beatId);

            if (target is null)
            {
                issues.Add(Issue(
                    StoryContextIssueCodes.BeatNotFound,
                    $"The story plan does not contain beat '{beatId}'."));

                selectedBeats = [];
            }
            else
            {
                selectedBeats = SelectContinuityClosure(allBeats, target);
            }
        }
        else
        {
            selectedBeats = OrderBeats(allBeats);
        }

        // Reuse the existing continuity rules, scoped to the projected beats, by
        // validating a non-mutating copy of the plan; unresolved references are
        // reported, never silently dropped.
        var scopedPlan = plan with { Beats = selectedBeats };
        foreach (var continuityIssue in StoryContinuityValidator.Validate(scopedPlan, _characters, _worlds))
        {
            issues.Add(Issue(continuityIssue.Code, continuityIssue.Message));
        }

        var characterIds = CollectRefs(selectedBeats, static beat => beat.CharacterRefs);
        var worldIds = CollectRefs(selectedBeats, static beat => beat.WorldRefs);

        var characterBibles = new List<CharacterBible>();
        foreach (var id in characterIds)
        {
            if (_characters.TryGetLatest(new CharacterBibleId(id), out var bible))
            {
                characterBibles.Add(bible);
            }
        }

        var worldBibles = new List<WorldBible>();
        foreach (var id in worldIds)
        {
            if (_worlds.TryGetLatest(new WorldBibleId(id), out var bible))
            {
                worldBibles.Add(bible);
            }
        }

        var includedCharacterIds = characterBibles
            .Select(bible => bible.Id.Value)
            .ToHashSet(StringComparer.Ordinal);

        var context = new StoryContext
        {
            Concept = ProjectConcept(concept),
            Treatment = treatment,
            Story = ProjectNarrative(plan),
            Characters = characterBibles
                .OrderBy(bible => bible.Id.Value, StringComparer.Ordinal)
                .Select(bible => ProjectCharacter(bible, includedCharacterIds, request.IncludeAssetReferences))
                .ToList(),
            Worlds = worldBibles
                .OrderBy(bible => bible.Id.Value, StringComparer.Ordinal)
                .Select(bible => ProjectWorld(bible, request.IncludeAssetReferences))
                .ToList(),
            Beats = selectedBeats.Select(ProjectBeat).ToList(),
            States = ProjectStates(request),
            BeatScope = request.BeatId
        };

        return new StoryContextBuildResult { Context = context, Issues = issues };
    }

    private static StoryConceptContext ProjectConcept(ConceptManifest concept) =>
        new()
        {
            Id = concept.Id,
            Title = concept.Title,
            Audience = concept.Audience,
            Format = concept.Format,
            Style = concept.Style,
            Duration = concept.Duration
        };

    private static StoryNarrativeContext ProjectNarrative(StoryPlan plan) =>
        new()
        {
            Id = plan.Id,
            Version = plan.Version.Value,
            Pattern = plan.NarrativePattern,
            PatternVersion = plan.NarrativePatternVersion,
            TargetDurationSeconds = plan.TargetDurationSeconds
        };

    private static StoryBeatContext ProjectBeat(StoryBeat beat) =>
        new()
        {
            Id = beat.Id,
            Order = beat.Order,
            Role = beat.Role,
            Purpose = beat.Purpose,
            Importance = beat.Importance,
            DurationSeconds = beat.TargetDurationSeconds,
            CharacterRefs = (beat.CharacterRefs ?? []).ToList(),
            WorldRefs = (beat.WorldRefs ?? []).ToList(),
            ContinuityFrom = (beat.ContinuityFrom ?? []).ToList()
        };

    private static StoryCharacterContext ProjectCharacter(
        CharacterBible bible,
        HashSet<string> includedCharacterIds,
        bool includeAssets)
    {
        var relationships = (bible.Relationships ?? [])
            .Where(relationship => relationship is not null
                && includedCharacterIds.Contains(relationship.Target.Value))
            .OrderBy(relationship => relationship.Target.Value, StringComparer.Ordinal)
            .ThenBy(relationship => relationship.Type, StringComparer.Ordinal)
            .Select(relationship => new StoryRelationshipContext
            {
                Target = relationship.Target,
                Type = relationship.Type,
                Description = relationship.Description
            })
            .ToList();

        return new StoryCharacterContext
        {
            Id = bible.Id,
            Version = bible.Version.Value,
            DisplayName = bible.DisplayName,
            Identity = bible.Identity ?? new CharacterIdentity(),
            PersonalityTraits = (bible.PersonalityTraits ?? []).ToList(),
            BaselineVariant = bible.BaselineVariant,
            VariantIds = (bible.Variants ?? [])
                .Where(variant => variant is not null)
                .Select(variant => variant.Id)
                .ToList(),
            Relationships = relationships,
            AssetReferences = includeAssets ? ProjectAssets(bible.AssetReferences) : []
        };
    }

    private static StoryWorldContext ProjectWorld(WorldBible bible, bool includeAssets) =>
        new()
        {
            Id = bible.Id,
            Version = bible.Version.Value,
            DisplayName = bible.DisplayName,
            Identity = bible.Identity ?? new WorldIdentity(),
            RecurringProps = (bible.RecurringProps ?? []).ToList(),
            ContinuityRules = (bible.ContinuityRules ?? []).ToList(),
            Locations = (bible.Locations ?? []).Where(location => location is not null).ToList(),
            AssetReferences = includeAssets ? ProjectAssets(bible.AssetReferences) : []
        };

    private static IReadOnlyList<StoryAssetContext> ProjectAssets(IReadOnlyList<AssetReference>? references) =>
        (references ?? [])
            .Where(reference => reference is not null)
            .OrderBy(reference => reference.AssetId.Value, StringComparer.Ordinal)
            .ThenBy(reference => reference.Purpose, StringComparer.Ordinal)
            .Select(reference => new StoryAssetContext
            {
                AssetId = reference.AssetId,
                Version = reference.Version,
                Purpose = reference.Purpose,
                Variant = reference.Variant
            })
            .ToList();

    private static StoryStateContext? ProjectStates(StoryContextRequest request)
    {
        var characterStates = (request.CharacterStates ?? []).Where(state => state is not null).ToList();
        var worldStates = (request.WorldStates ?? []).Where(state => state is not null).ToList();

        if (characterStates.Count == 0 && worldStates.Count == 0)
        {
            return null;
        }

        return new StoryStateContext
        {
            Characters = characterStates
                .OrderBy(state => state.CharacterRef.Value, StringComparer.Ordinal)
                .ToList(),
            Worlds = worldStates
                .OrderBy(state => state.WorldRef.Value, StringComparer.Ordinal)
                .ToList()
        };
    }

    private static IReadOnlyList<StoryBeat> OrderBeats(IEnumerable<StoryBeat> beats) =>
        beats
            .OrderBy(beat => beat.Order)
            .ThenBy(beat => beat.Id.Value, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The selected beat plus its transitive <c>continuityFrom</c> dependencies. The
    /// plan is acyclic, so this is a finite, deterministic set; unrelated beats are
    /// never pulled in.
    /// </summary>
    private static IReadOnlyList<StoryBeat> SelectContinuityClosure(
        IReadOnlyList<StoryBeat> beats,
        StoryBeat target)
    {
        var byId = new Dictionary<StoryBeatId, StoryBeat>();
        foreach (var beat in beats)
        {
            byId.TryAdd(beat.Id, beat);
        }

        var selected = new List<StoryBeat>();
        var visited = new HashSet<StoryBeatId>();
        var pending = new Stack<StoryBeat>();
        pending.Push(target);

        while (pending.Count > 0)
        {
            var beat = pending.Pop();
            if (!visited.Add(beat.Id))
            {
                continue;
            }

            selected.Add(beat);

            foreach (var dependency in beat.ContinuityFrom ?? [])
            {
                if (byId.TryGetValue(dependency, out var dependencyBeat))
                {
                    pending.Push(dependencyBeat);
                }
            }
        }

        return OrderBeats(selected);
    }

    private static List<string> CollectRefs(
        IReadOnlyList<StoryBeat> beats,
        Func<StoryBeat, IReadOnlyList<string>> selector)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);

        foreach (var beat in beats)
        {
            foreach (var reference in selector(beat) ?? [])
            {
                if (StoryIdentifier.IsValid(reference))
                {
                    references.Add(reference);
                }
            }
        }

        return references.OrderBy(reference => reference, StringComparer.Ordinal).ToList();
    }

    private static StoryContextIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
