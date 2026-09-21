using AIStudio.Application.Concepts;

namespace AIStudio.Application.Stories;

/// <summary>
/// Narrow, deterministic structural validation for a story plan. It checks the
/// plan's own coherence — identity, source concept reference, narrative pattern
/// reference, duration, non-empty ordered beats, unique ids/orders, positive beat
/// durations, duration sum, and continuity references — and can additionally
/// require a narrative pattern's required slots. It never scores story quality and
/// never consults a provider.
/// </summary>
public static class StoryPlanValidator
{
    public const int MinimumDurationSeconds = 1;
    public const int MaximumDurationSeconds = 86_400;

    /// <summary>Allowed relative gap between the sum of beat durations and the target.</summary>
    public const double DurationToleranceRatio = 0.10;

    /// <summary>Minimum absolute gap, so very short plans are not rejected by rounding.</summary>
    public const int DurationToleranceMinimumSeconds = 1;

    public static IReadOnlyList<StoryPlanIssue> Validate(StoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var issues = new List<StoryPlanIssue>();

        if (!StoryIdentifier.IsValid(plan.Id.Value))
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.PlanIdInvalid,
                "A story plan id is required and must be a lowercase identifier such as 'run-ai-locally-story'."));
        }

        if (!plan.Version.IsValid)
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.PlanVersionInvalid,
                $"A story plan version must be at least {StoryPlanVersion.Minimum}."));
        }

        if (!ConceptIdentifier.IsValid(plan.SourceConceptId.Value))
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.SourceConceptInvalid,
                "A story plan must reference a valid source concept id."));
        }

        if (!StoryIdentifier.IsValid(plan.NarrativePattern.Value))
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.PatternIdInvalid,
                "A story plan must reference a valid narrative pattern id."));
        }

        if (!plan.NarrativePatternVersion.IsValid)
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.PatternVersionInvalid,
                $"A narrative pattern version must be at least {NarrativePatternVersion.Minimum}."));
        }

        if (plan.TargetDurationSeconds is < MinimumDurationSeconds or > MaximumDurationSeconds)
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.TargetDurationInvalid,
                $"A story plan target duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds."));
        }

        ValidateBeats(plan, issues);

        return issues;
    }

    /// <summary>
    /// Structural validation plus the extra guarantee that a plan shaped by
    /// <paramref name="pattern"/> contains a beat for every required slot. Used by
    /// the director and by the future structured-response parser.
    /// </summary>
    public static IReadOnlyList<StoryPlanIssue> Validate(StoryPlan plan, NarrativePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var issues = new List<StoryPlanIssue>(Validate(plan));

        if (plan.NarrativePattern != pattern.Id || plan.NarrativePatternVersion != pattern.Version)
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.PatternMismatch,
                $"Story plan references '{plan.NarrativePattern}' v{plan.NarrativePatternVersion.Value} " +
                $"but was validated against '{pattern.Id}' v{pattern.Version.Value}."));
        }

        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var beat in plan.Beats ?? [])
        {
            if (beat is not null && StoryIdentifier.IsValid(beat.Role.Value))
            {
                roles.Add(beat.Role.Value);
            }
        }

        foreach (var slot in pattern.BeatSlots ?? [])
        {
            if (slot is null || !slot.IsRequired)
            {
                continue;
            }

            if (StoryIdentifier.IsValid(slot.Role.Value) && !roles.Contains(slot.Role.Value))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.RequiredSlotMissing,
                    $"Narrative pattern '{pattern.Id}' requires a '{slot.Role}' beat, but the plan has none."));
            }
        }

        return issues;
    }

    private static void ValidateBeats(StoryPlan plan, List<StoryPlanIssue> issues)
    {
        var beats = plan.Beats;
        if (beats is null || beats.Count == 0)
        {
            issues.Add(Issue(
                StoryPlanIssueCodes.BeatsEmpty,
                "A story plan must contain at least one beat."));

            return;
        }

        var seenIds = new HashSet<StoryBeatId>();
        var seenOrders = new HashSet<int>();
        var durationSum = 0L;

        foreach (var beat in beats)
        {
            if (beat is null)
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatIdInvalid,
                    "A story plan contains a missing beat."));

                continue;
            }

            if (!StoryIdentifier.IsValid(beat.Id.Value))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatIdInvalid,
                    "Every story beat requires a valid id."));
            }
            else if (!seenIds.Add(beat.Id))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatIdDuplicate,
                    $"Story beat id '{beat.Id}' is declared more than once."));
            }

            if (beat.Order < 1)
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatOrderInvalid,
                    "Every story beat order must be a positive integer."));
            }
            else if (!seenOrders.Add(beat.Order))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatOrderDuplicate,
                    $"Story beat order {beat.Order} is declared more than once."));
            }

            if (!StoryIdentifier.IsValid(beat.Role.Value))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatRoleInvalid,
                    "Every story beat must declare a valid role such as 'hook' or 'chorus'."));
            }

            if (!StoryIdentifier.IsValid(beat.Importance))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatImportanceInvalid,
                    "Every story beat importance must be a lowercase identifier such as 'major'."));
            }

            if (string.IsNullOrWhiteSpace(beat.Purpose))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatPurposeEmpty,
                    "Every story beat must declare a narrative purpose."));
            }

            if (beat.TargetDurationSeconds is < MinimumDurationSeconds or > MaximumDurationSeconds)
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatDurationInvalid,
                    $"Every story beat target duration must be between {MinimumDurationSeconds} and {MaximumDurationSeconds} seconds."));
            }
            else
            {
                durationSum += beat.TargetDurationSeconds;
            }

            ValidateReferences(beat, issues);
        }

        ValidateContinuity(beats, seenIds, issues);

        if (plan.TargetDurationSeconds is >= MinimumDurationSeconds and <= MaximumDurationSeconds
            && issues.All(issue => issue.Code != StoryPlanIssueCodes.BeatDurationInvalid))
        {
            var tolerance = Math.Max(
                plan.TargetDurationSeconds * DurationToleranceRatio,
                DurationToleranceMinimumSeconds);

            if (Math.Abs(durationSum - plan.TargetDurationSeconds) > tolerance)
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.DurationMismatch,
                    $"The sum of beat durations ({durationSum}s) does not match the story target " +
                    $"duration ({plan.TargetDurationSeconds}s) within tolerance."));
            }
        }
    }

    private static void ValidateReferences(StoryBeat beat, List<StoryPlanIssue> issues)
    {
        foreach (var reference in beat.CharacterRefs ?? [])
        {
            if (!StoryIdentifier.IsValid(reference))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatReferenceInvalid,
                    "Character references must be lowercase identifiers such as 'developer'."));
            }
        }

        foreach (var reference in beat.WorldRefs ?? [])
        {
            if (!StoryIdentifier.IsValid(reference))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatReferenceInvalid,
                    "World references must be lowercase identifiers such as 'bedroom'."));
            }
        }
    }

    private static void ValidateContinuity(
        IReadOnlyList<StoryBeat> beats,
        HashSet<StoryBeatId> knownIds,
        List<StoryPlanIssue> issues)
    {
        var edges = new Dictionary<StoryBeatId, List<StoryBeatId>>();

        foreach (var beat in beats)
        {
            if (beat is null || !StoryIdentifier.IsValid(beat.Id.Value))
            {
                continue;
            }

            var dependencies = new List<StoryBeatId>();

            foreach (var reference in beat.ContinuityFrom ?? [])
            {
                if (!StoryIdentifier.IsValid(reference.Value))
                {
                    issues.Add(Issue(
                        StoryPlanIssueCodes.BeatContinuityUnknown,
                        $"Story beat '{beat.Id}' has an invalid continuity reference."));

                    continue;
                }

                if (reference == beat.Id)
                {
                    issues.Add(Issue(
                        StoryPlanIssueCodes.BeatContinuitySelfReference,
                        $"Story beat '{beat.Id}' continues from itself."));

                    continue;
                }

                if (!knownIds.Contains(reference))
                {
                    issues.Add(Issue(
                        StoryPlanIssueCodes.BeatContinuityUnknown,
                        $"Story beat '{beat.Id}' continues from unknown beat '{reference}'."));

                    continue;
                }

                dependencies.Add(reference);
            }

            edges[beat.Id] = dependencies;
        }

        var state = new Dictionary<StoryBeatId, int>();

        foreach (var beatId in edges.Keys)
        {
            if (HasCycle(beatId, edges, state))
            {
                issues.Add(Issue(
                    StoryPlanIssueCodes.BeatContinuityCycle,
                    "Story beat continuity references contain a cycle."));

                break;
            }
        }
    }

    private static bool HasCycle(
        StoryBeatId node,
        Dictionary<StoryBeatId, List<StoryBeatId>> edges,
        Dictionary<StoryBeatId, int> state)
    {
        if (state.TryGetValue(node, out var current))
        {
            return current == 1;
        }

        state[node] = 1;

        if (edges.TryGetValue(node, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                if (HasCycle(dependency, edges, state))
                {
                    return true;
                }
            }
        }

        state[node] = 2;
        return false;
    }

    private static StoryPlanIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
