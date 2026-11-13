using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Learning;
using AIStudio.Application.Research;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateIdea;

public sealed class GenerateIdeaJobHandler(
    IContentProjectReader contentProjects,
    IAiTextGenerator textGenerator,
    IResearchRepository? researchRepository = null,
    ILearningRepository? learningRepository = null) : IJobHandler
{
    public bool CanHandle(JobType type) => type == JobType.GenerateIdea;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.GenerateIdea)
        {
            throw new JobExecutionException(
                "generate_idea_wrong_job_type",
                $"GenerateIdea handler cannot execute job type {job.Type}.");
        }

        var payload = GenerateIdeaJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw new JobExecutionException(
                "generate_idea_invalid_payload",
                "GenerateIdea payload contentProjectId does not match the claimed job.");
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

        IReadOnlyList<string>? recentTitles = null;
        if (researchRepository is not null)
        {
            recentTitles = await researchRepository.ListRecentProjectTitlesAsync(12, cancellationToken);
        }

        IReadOnlyList<string>? activeGuidelines = null;
        if (learningRepository is not null)
        {
            var guidelines = await learningRepository.ListAsync(
                AIStudio.Domain.Learning.LearningGuidelineStatuses.SupportedGuideline,
                5,
                cancellationToken);
            activeGuidelines = guidelines.Select(g => $"{g.Title}: {g.Summary}").ToArray();
        }

        AiTextResponse response;
        try
        {
            response = await textGenerator.GenerateAsync(
                new AiTextRequest(
                    GenerateIdeaPrompt.Build(project, payload, recentTitles, activeGuidelines),
                    GenerateIdeaPrompt.SystemInstruction,
                    payload.Model,
                    AiResponseFormat.JsonObject,
                    Temperature: 0.3,
                    MaxTokens: 1_200,
                    Provider: payload.Provider),
                cancellationToken);
        }
        catch (AiGenerationException exception)
        {
            throw new JobExecutionException(
                MapAiErrorCode(exception.ErrorCode),
                exception.Message,
                exception);
        }

        return GenerateIdeaResult.Deserialize(response.Text).Serialize();
    }

    private static string MapAiErrorCode(AiErrorCode errorCode) =>
        errorCode switch
        {
            AiErrorCode.ProviderUnavailable => "ai_provider_unavailable",
            AiErrorCode.Timeout => "ai_timeout",
            AiErrorCode.ModelNotFound => "ai_model_not_found",
            AiErrorCode.InvalidRequest => "ai_invalid_request",
            AiErrorCode.MalformedResponse => "ai_malformed_response",
            AiErrorCode.RateLimited => "ai_rate_limited",
            _ => "ai_provider_error"
        };
}
