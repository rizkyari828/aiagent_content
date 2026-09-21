using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Deterministic, AI-free post-StoryPlan grounding. It applies an explicit per-beat
/// assignment map to an existing <see cref="StoryPlan"/> and returns a NEW plan whose
/// beats reference registered character/world bibles. It only replaces
/// <c>CharacterRefs</c>/<c>WorldRefs</c>: identity, order, role, purpose, duration,
/// continuity, pattern, and source concept stay untouched. It creates no bibles,
/// resolves no asset, and executes no provider. The caller supplies the mapping; a
/// future Qwen-backed grounder will propose it, not bypass this deterministic step.
/// </summary>
public sealed class StoryPlanGrounder(
    ICharacterBibleRegistry characters,
    IWorldBibleRegistry worlds)
{
    private readonly ICharacterBibleRegistry _characters =
        characters ?? throw new ArgumentNullException(nameof(characters));

    private readonly IWorldBibleRegistry _worlds =
        worlds ?? throw new ArgumentNullException(nameof(worlds));

    public StoryPlan Ground(StoryPlanGroundingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = request.StoryPlan
            ?? throw new StoryPlanGroundingException(
                StoryPlanGroundingErrorCodes.RequestInvalid,
                "Story plan grounding requires a story plan.");

        var assignments = new Dictionary<StoryBeatId, StoryBeatGrounding>();
        foreach (var assignment in request.Beats ?? [])
        {
            ArgumentNullException.ThrowIfNull(assignment);

            if (!assignments.TryAdd(assignment.BeatId, assignment))
            {
                throw new StoryPlanGroundingException(
                    StoryPlanGroundingErrorCodes.BeatDuplicate,
                    $"Beat '{assignment.BeatId.Value}' is grounded more than once.");
            }

            ValidateReferences(assignment);
        }

        var beats = new List<StoryBeat>(plan.Beats.Count);
        var knownBeatIds = new HashSet<StoryBeatId>();

        foreach (var beat in plan.Beats)
        {
            knownBeatIds.Add(beat.Id);

            beats.Add(assignments.TryGetValue(beat.Id, out var assignment)
                ? beat with
                {
                    CharacterRefs = assignment.CharacterRefs ?? [],
                    WorldRefs = assignment.WorldRefs ?? []
                }
                : beat);
        }

        foreach (var beatId in assignments.Keys)
        {
            if (!knownBeatIds.Contains(beatId))
            {
                throw new StoryPlanGroundingException(
                    StoryPlanGroundingErrorCodes.BeatUnknown,
                    $"Story plan '{plan.Id}' has no beat '{beatId.Value}'.");
            }
        }

        var grounded = plan with { Beats = beats };

        // Reference validation reuses the existing continuity rules; it is never
        // re-implemented here.
        var issues = StoryContinuityValidator.Validate(grounded, _characters, _worlds);
        if (issues.Count > 0)
        {
            throw new StoryPlanGroundingException(
                StoryPlanGroundingErrorCodes.PlanInvalid,
                $"Grounded story plan is not coherent: {issues[0].Code}.");
        }

        return grounded;
    }

    private void ValidateReferences(StoryBeatGrounding assignment)
    {
        foreach (var reference in assignment.CharacterRefs ?? [])
        {
            if (!CharacterBibleId.TryParse(reference, out var characterId)
                || !_characters.TryGetLatest(characterId, out _))
            {
                throw new StoryPlanGroundingException(
                    StoryPlanGroundingErrorCodes.CharacterUnknown,
                    $"Character reference '{reference}' is not a registered character bible.");
            }
        }

        foreach (var reference in assignment.WorldRefs ?? [])
        {
            if (!WorldBibleId.TryParse(reference, out var worldId)
                || !_worlds.TryGetLatest(worldId, out _))
            {
                throw new StoryPlanGroundingException(
                    StoryPlanGroundingErrorCodes.WorldUnknown,
                    $"World reference '{reference}' is not a registered world bible.");
            }
        }
    }
}
