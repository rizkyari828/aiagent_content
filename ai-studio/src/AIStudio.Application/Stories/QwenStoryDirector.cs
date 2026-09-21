using AIStudio.Application.AI;
using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;

namespace AIStudio.Application.Stories;

/// <summary>
/// Qwen-backed Story Director. It builds a deterministic prompt from the approved
/// creative direction and the caller-supplied narrative pattern, asks the existing
/// <see cref="IAiTextGenerator"/> boundary for structured JSON, and then parses and
/// validates the untrusted response through the existing
/// <see cref="StoryPlanParser"/> and <see cref="StoryPlanValidator"/>. It never
/// selects a pattern, writes dialogue, or starts production, and it has no silent
/// fallback to the deterministic director: a provider or contract failure surfaces.
/// </summary>
public sealed class QwenStoryDirector : IStoryDirector
{
    private const double Temperature = 0.4;
    private const int MaxTokens = 2_500;

    private readonly IAiTextGenerator _aiTextGenerator;
    private readonly INarrativePatternRegistry _patterns;

    public QwenStoryDirector(
        IAiTextGenerator aiTextGenerator,
        INarrativePatternRegistry patterns)
    {
        _aiTextGenerator = aiTextGenerator ?? throw new ArgumentNullException(nameof(aiTextGenerator));
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
        var planId = request.PlanId ?? new StoryPlanId($"{concept.Id.Value}-story");
        var planVersion = request.Version ?? new StoryPlanVersion(1);

        var prompt = QwenStoryDirectorPrompt.Build(
            request.CreativeDirection,
            pattern,
            planId,
            planVersion,
            targetDuration);

        // IStoryDirector is synchronous (the deterministic director is pure), so the
        // existing async AI boundary is awaited here. There is no SynchronizationContext
        // in the product host, so this cannot deadlock; it does block one thread per call.
        // ponytail: sync-over-async ceiling; make IStoryDirector async if directing ever
        // runs concurrently at volume.
        var response = _aiTextGenerator
            .GenerateAsync(
                new AiTextRequest(
                    prompt,
                    SystemPrompt: QwenStoryDirectorPrompt.SystemInstruction,
                    Model: null,
                    ResponseFormat: AiResponseFormat.JsonObject,
                    Temperature: Temperature,
                    MaxTokens: MaxTokens,
                    Think: false),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var plan = StoryPlanParser.Parse(response.Text, _patterns);

        EnsureAuthoritativeHeader(plan, planId, planVersion, concept.Id, pattern, targetDuration);

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

    /// <summary>
    /// The StoryPlanParser validates structure and pattern slot coverage against the
    /// plan's own pattern reference, so the director additionally pins the header to
    /// the authoritative request: a model that swaps the pattern, source concept,
    /// identity, or target duration fails clearly instead of being silently accepted.
    /// </summary>
    private static void EnsureAuthoritativeHeader(
        StoryPlan plan,
        StoryPlanId planId,
        StoryPlanVersion planVersion,
        ConceptId sourceConceptId,
        NarrativePattern pattern,
        int targetDuration)
    {
        if (plan.Id != planId)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.PlanInvalid,
                $"The generated story plan id '{plan.Id}' does not match the requested id '{planId}'.");
        }

        if (plan.Version != planVersion)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.PlanInvalid,
                $"The generated story plan version '{plan.Version}' does not match the requested version '{planVersion}'.");
        }

        if (plan.SourceConceptId != sourceConceptId)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.PlanInvalid,
                $"The generated story plan source concept '{plan.SourceConceptId}' does not match the supplied concept '{sourceConceptId}'.");
        }

        if (plan.NarrativePattern != pattern.Id || plan.NarrativePatternVersion != pattern.Version)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.PlanInvalid,
                $"The generated story plan references narrative pattern '{plan.NarrativePattern}' " +
                $"v{plan.NarrativePatternVersion.Value} instead of the supplied '{pattern.Id}' v{pattern.Version.Value}.");
        }

        if (plan.TargetDurationSeconds != targetDuration)
        {
            throw new StoryDirectorException(
                StoryDirectorErrorCodes.PlanInvalid,
                $"The generated story plan target duration {plan.TargetDurationSeconds}s does not match the requested {targetDuration}s.");
        }
    }
}
