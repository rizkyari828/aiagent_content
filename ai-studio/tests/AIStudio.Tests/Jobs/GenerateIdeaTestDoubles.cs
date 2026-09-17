using System.Diagnostics;
using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Jobs;

internal static class GenerateIdeaTestData
{
    public const string ValidResult = """
        {
          "title": "Local AI Content",
          "hook": "Create privately.",
          "summary": "A local workflow.",
          "angle": "Privacy",
          "targetAudience": "Creators",
          "suggestedFormat": "Tutorial"
        }
        """;

    public static string ValidPayload(Guid projectId, string? model = null) =>
        $$"""
        {
          "contentProjectId": "{{projectId}}",
          "topic": "Local AI",
          "targetAudience": "Creators",
          "language": "Indonesian"{{(model is null ? string.Empty : $",\n  \"model\": \"{model}\"")}}
        }
        """;

    public static ClaimedJob CreateJob(Guid projectId, string payload) =>
        new(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateIdea,
            "input-v1",
            payload,
            0,
            2,
            false);
}

internal sealed class StubContentProjectReader(ContentProjectSnapshot? project)
    : IContentProjectReader
{
    public Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(project?.Id == id ? project : null);
    }
}

internal sealed class RecordingTextGenerator(string responseText) : IAiTextGenerator
{
    public AiTextRequest? Request { get; private set; }

    public Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Request = request;
        return Task.FromResult(new AiTextResponse(
            responseText,
            request.Model ?? "default-model",
            null,
            null,
            null));
    }
}

internal sealed class BlockingTextGenerator : IAiTextGenerator
{
    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new UnreachableException();
    }
}

internal sealed class FailingTextGenerator(AiErrorCode errorCode) : IAiTextGenerator
{
    public Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken) =>
        throw new AiGenerationException(errorCode, "Expected AI failure.");
}
