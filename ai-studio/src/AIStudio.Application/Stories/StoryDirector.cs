using AIStudio.Application.Concepts;

namespace AIStudio.Application.Stories;

/// <summary>
/// Deterministic v1 Story Director. It maps a creative direction onto a
/// data-driven <see cref="NarrativePattern"/>: each slot becomes one ordered
/// <see cref="StoryBeat"/> with the slot's role and guidance, and the target
/// duration is shared across beats by the slot weights. It is a structural
/// scaffold, not story generation — the actual narrative content will come from a
/// future Qwen-backed director that returns the same <see cref="StoryPlan"/> shape.
/// It calls no model, loads no pattern code, and executes nothing.
/// </summary>
public sealed class StoryDirector : IStoryDirector
{
    private readonly INarrativePatternRegistry _patterns;

    public StoryDirector(INarrativePatternRegistry patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    public StoryDirectorResult Direct(StoryDirectorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var concept = request.CreativeDirection.Concept;

        if (!ConceptIdentifier.IsValid(concept.Id.Value))
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.RequestInvalid,
                "Story directing requires a creative direction with a valid source concept id.");
        }

        if (!StoryIdentifier.IsValid(request.NarrativePattern.Value))
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.RequestInvalid,
                "Story directing requires an explicit narrative pattern id.");
        }

        var targetDuration = request.TargetDurationSeconds ?? concept.Duration;
        if (targetDuration is < StoryPlanValidator.MinimumDurationSeconds
            or > StoryPlanValidator.MaximumDurationSeconds)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.RequestInvalid,
                $"A story target duration must be between {StoryPlanValidator.MinimumDurationSeconds} " +
                $"and {StoryPlanValidator.MaximumDurationSeconds} seconds.");
        }

        var pattern = ResolvePattern(request);

        var plan = new StoryPlan
        {
            Id = request.PlanId ?? new StoryPlanId($"{concept.Id.Value}-story"),
            Version = request.Version ?? new StoryPlanVersion(1),
            SourceConceptId = concept.Id,
            NarrativePattern = pattern.Id,
            NarrativePatternVersion = pattern.Version,
            TargetDurationSeconds = targetDuration,
            Beats = BuildBeats(pattern, targetDuration)
        };

        var issues = StoryPlanValidator.Validate(plan, pattern);
        if (issues.Count > 0)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.PlanInvalid,
                $"The produced story plan is not structurally coherent: {issues[0].Code}.");
        }

        return new StoryDirectorResult { Plan = plan };
    }

    private NarrativePattern ResolvePattern(StoryDirectorRequest request)
    {
        if (request.NarrativePatternVersion is { } version
            && _patterns.TryGet(request.NarrativePattern, version, out var exact))
        {
            return exact;
        }

        if (request.NarrativePatternVersion is null
            && _patterns.TryGetLatest(request.NarrativePattern, out var latest))
        {
            return latest;
        }

        throw new StoryDirectorException(
            StoryDirectorErrorCodes.PatternNotFound,
            $"Narrative pattern '{request.NarrativePattern}' is not registered.");
    }

    private static IReadOnlyList<StoryBeat> BuildBeats(NarrativePattern pattern, int targetDuration)
    {
        var slots = pattern.BeatSlots;
        var totalWeight = slots.Sum(slot => slot.DurationWeight);

        var beats = new List<StoryBeat>(slots.Count);
        var assigned = 0;
        StoryBeatId? previous = null;

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            var isLast = index == slots.Count - 1;

            var duration = isLast
                ? Math.Max(StoryPlanValidator.MinimumDurationSeconds, targetDuration - assigned)
                : Math.Max(
                    StoryPlanValidator.MinimumDurationSeconds,
                    (int)Math.Round(targetDuration * (slot.DurationWeight / totalWeight), MidpointRounding.AwayFromZero));

            assigned += duration;

            var id = new StoryBeatId($"beat-{index + 1:00}");
            beats.Add(new StoryBeat
            {
                Id = id,
                Order = index + 1,
                Role = slot.Role,
                Purpose = slot.Purpose,
                Importance = StoryBeat.DefaultImportance,
                TargetDurationSeconds = duration,
                ContinuityFrom = previous is { } prior ? [prior] : []
            });

            previous = id;
        }

        return beats;
    }
}
