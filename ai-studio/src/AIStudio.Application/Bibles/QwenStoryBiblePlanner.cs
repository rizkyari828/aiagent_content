using AIStudio.Application.AI;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Qwen-backed Story Bible Planner. It builds a deterministic prompt from the
/// approved creative direction and the existing story plan, asks the existing
/// <see cref="IAiTextGenerator"/> boundary for one structured JSON response, and then
/// strictly parses and validates it into a <see cref="StoryBiblePlan"/> proposal. It
/// never registers bibles, applies grounding, mutates the story plan, persists
/// anything, or executes a provider: the caller applies the proposal explicitly.
/// </summary>
public sealed class QwenStoryBiblePlanner(IAiTextGenerator aiTextGenerator) : IStoryBiblePlanner
{
    private const double Temperature = 0.4;
    private const int MaxTokens = 2_500;

    private readonly IAiTextGenerator _aiTextGenerator =
        aiTextGenerator ?? throw new ArgumentNullException(nameof(aiTextGenerator));

    public async Task<StoryBiblePlan> BuildAsync(
        StoryBiblePlanningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var direction = request.CreativeDirection
            ?? throw Invalid("Story bible planning requires a creative direction.");
        var storyPlan = request.StoryPlan
            ?? throw Invalid("Story bible planning requires a story plan.");

        if (storyPlan.Beats is null || storyPlan.Beats.Count == 0)
        {
            throw Invalid("Story bible planning requires a story plan with at least one beat.");
        }

        var prompt = QwenStoryBiblePlannerPrompt.Build(direction, storyPlan);

        var response = await _aiTextGenerator.GenerateAsync(
            new AiTextRequest(
                prompt,
                SystemPrompt: QwenStoryBiblePlannerPrompt.SystemInstruction,
                Model: null,
                ResponseFormat: AiResponseFormat.JsonObject,
                Temperature: Temperature,
                MaxTokens: MaxTokens,
                Think: false),
            cancellationToken);

        return StoryBiblePlanParser.Parse(response.Text, storyPlan);
    }

    private static StoryBiblePlanningException Invalid(string message) =>
        new(StoryBiblePlanningErrorCodes.RequestInvalid, message);
}
