using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Scripts;
using AIStudio.Application.StoryContext;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Application.Jobs.GenerateStoryboard;

public sealed class GenerateStoryboardJobHandler(
    IContentProjectReader contentProjects,
    IScriptReviewRepository scripts,
    IAiTextGenerator textGenerator,
    IStoryContextBuilder storyContextBuilder) : IJobHandler
{
    public bool CanHandle(JobType type) => type == JobType.GenerateStoryboard;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.GenerateStoryboard)
        {
            throw new JobExecutionException(
                "generate_storyboard_wrong_job_type",
                $"GenerateStoryboard handler cannot execute job type {job.Type}.");
        }

        var payload = GenerateStoryboardJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw new JobExecutionException(
                "generate_storyboard_invalid_payload",
                "GenerateStoryboard payload contentProjectId does not match the claimed job.");
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

        var script = await scripts.FindByProjectIdAsync(
            job.ContentProjectId,
            cancellationToken);
        if (script is null)
        {
            throw new JobExecutionException(
                "storyboard_script_not_found",
                $"Content project '{job.ContentProjectId}' does not have a reviewed script.");
        }

        if (script.Status != ScriptReviewStatus.Approved)
        {
            throw new JobExecutionException(
                "storyboard_script_not_approved",
                "The canonical storyboard input must be an approved reviewed script.");
        }

        GenerateScriptResult scriptContent;
        try
        {
            scriptContent = GenerateScriptResult.Deserialize(script.Content);
        }
        catch (JobExecutionException exception)
        {
            throw new JobExecutionException(
                "storyboard_script_invalid",
                "The reviewed script content is not a valid structured script.",
                exception);
        }

        // When the payload carries the validated narrative planning inputs, project
        // them through the existing StoryContextBuilder; unreferenced characters and
        // worlds stay out, and unresolved references are omitted rather than faked.
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
                    GenerateStoryboardPrompt.Build(project, scriptContent, storyContext),
                    GenerateStoryboardPrompt.SystemInstruction,
                    ResponseFormat: AiResponseFormat.JsonObject,
                    Temperature: 0.3,
                    MaxTokens: 3_000),
                cancellationToken);
        }
        catch (AiGenerationException exception)
        {
            throw new JobExecutionException(
                MapAiErrorCode(exception.ErrorCode),
                exception.Message,
                exception);
        }

        return GenerateStoryboardResult.Deserialize(response.Text).Serialize();
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
