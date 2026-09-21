using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.StoryContext;
using AIStudio.Domain.Jobs;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Application.Jobs.GenerateScript;

public sealed class GenerateScriptJobHandler(
    IContentProjectReader contentProjects,
    IAiTextGenerator textGenerator,
    IStoryContextBuilder storyContextBuilder) : IJobHandler
{
    public bool CanHandle(JobType type) => type == JobType.GenerateScript;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.GenerateScript)
        {
            throw new JobExecutionException(
                "generate_script_wrong_job_type",
                $"GenerateScript handler cannot execute job type {job.Type}.");
        }

        var payload = GenerateScriptJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw new JobExecutionException(
                "generate_script_invalid_payload",
                "GenerateScript payload contentProjectId does not match the claimed job.");
        }

        var project = await contentProjects.FindByIdAsync(
            job.ContentProjectId,
            cancellationToken);
        if (project is null)
        {
            throw new JobExecutionException(
                "content_project_not_found",
                $"Content project '{job.ContentProjectId}' was not found.");
        }

        // When the payload carries the validated narrative planning inputs, project
        // them through StoryContextBuilder; unreferenced characters/worlds stay out
        // and unresolved references are omitted rather than fabricated.
        StoryContextModel? storyContext = null;
        if (payload.CreativeDirection is not null && payload.StoryPlan is not null)
        {
            storyContext = storyContextBuilder.Build(new StoryContextRequest
            {
                CreativeDirection = payload.CreativeDirection,
                StoryPlan = payload.StoryPlan,
                CharacterStates = payload.CharacterStates ?? [],
                WorldStates = payload.WorldStates ?? []
            }).Context;
        }

        AiTextResponse response;
        try
        {
            response = await textGenerator.GenerateAsync(
                new AiTextRequest(
                    GenerateScriptPrompt.Build(project, payload, storyContext),
                    GenerateScriptPrompt.SystemInstruction,
                    ResponseFormat: AiResponseFormat.JsonObject,
                    Temperature: 0.3,
                    MaxTokens: 3_000,
                    Think: false),
                cancellationToken);
        }
        catch (AiGenerationException exception)
        {
            throw new JobExecutionException(
                MapAiErrorCode(exception.ErrorCode),
                exception.Message,
                exception);
        }

        return GenerateScriptResult.Deserialize(response.Text).Serialize();
    }

    private static string MapAiErrorCode(AiErrorCode errorCode) =>
        errorCode switch
        {
            AiErrorCode.ProviderUnavailable => "ai_provider_unavailable",
            AiErrorCode.Timeout => "ai_timeout",
            AiErrorCode.ModelNotFound => "ai_model_not_found",
            AiErrorCode.InvalidRequest => "ai_invalid_request",
            AiErrorCode.MalformedResponse => "ai_malformed_response",
            _ => "ai_provider_error"
        };
}
