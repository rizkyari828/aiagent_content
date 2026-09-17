using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateScript;

public sealed class GenerateScriptJobHandler(
    IContentProjectReader contentProjects,
    IAiTextGenerator textGenerator) : IJobHandler
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

        AiTextResponse response;
        try
        {
            response = await textGenerator.GenerateAsync(
                new AiTextRequest(
                    GenerateScriptPrompt.Build(project, payload),
                    GenerateScriptPrompt.SystemInstruction,
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
