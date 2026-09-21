using AIStudio.Application.AI;

namespace AIStudio.Application.Creative;

/// <summary>
/// LLM-backed creative planner. It consumes an already-approved idea, builds a
/// deterministic prompt from a safe production summary, asks the existing AI text
/// boundary for structured JSON, and parses/validates the untrusted response into
/// a <see cref="CreativeDirection"/>. It never generates the idea, never registers
/// a concept, never starts production, and never executes model output.
/// </summary>
public sealed class CreativeDirector : ICreativeDirector
{
    private const double Temperature = 0.4;
    private const int MaxTokens = 1_500;

    private readonly IAiTextGenerator _aiTextGenerator;
    private readonly ICreativePlanningContextProvider _planningContext;

    public CreativeDirector(
        IAiTextGenerator aiTextGenerator,
        ICreativePlanningContextProvider planningContext)
    {
        _aiTextGenerator = aiTextGenerator ?? throw new ArgumentNullException(nameof(aiTextGenerator));
        _planningContext = planningContext ?? throw new ArgumentNullException(nameof(planningContext));
    }

    public async Task<CreativeDirectionResult> DirectAsync(
        ApprovedIdea idea,
        CreativeDirectionOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idea);

        var ideaIssues = ApprovedIdeaValidator.Validate(idea);
        if (ideaIssues.Count > 0)
        {
            throw new CreativeDirectionException(
                CreativeIssueCodes.IdeaInvalid,
                $"Approved idea is not usable creative input: {ideaIssues[0].Code}.");
        }

        var effectiveOptions = options ?? new CreativeDirectionOptions();
        var context = _planningContext.Build();
        var prompt = CreativeDirectorPrompt.Build(idea, context, effectiveOptions);

        var response = await _aiTextGenerator.GenerateAsync(
            new AiTextRequest(
                prompt,
                SystemPrompt: CreativeDirectorPrompt.SystemInstruction,
                Model: effectiveOptions.Model,
                ResponseFormat: AiResponseFormat.JsonObject,
                Temperature: Temperature,
                MaxTokens: MaxTokens,
                Think: false),
            cancellationToken);

        var direction = CreativeDirectionParser.Parse(response.Text);

        return new CreativeDirectionResult
        {
            Direction = ApplyIdeaGuardrails(idea, direction),
            Model = response.Model
        };
    }

    /// <summary>
    /// Deterministic guardrail: the approved idea stays authoritative. The intended
    /// audience and idea reference are preserved over anything the model returned,
    /// so the director cannot silently change who the idea is for or which approved
    /// idea it came from. No semantic/AI scoring is used.
    /// </summary>
    private static CreativeDirection ApplyIdeaGuardrails(
        ApprovedIdea idea,
        CreativeDirection direction)
    {
        var concept = direction.Concept with { Audience = idea.Audience.Trim() };
        var ideaReference = string.IsNullOrWhiteSpace(idea.IdeaReference)
            ? direction.IdeaReference
            : idea.IdeaReference.Trim();

        return direction with
        {
            IdeaReference = ideaReference,
            Concept = concept
        };
    }
}
