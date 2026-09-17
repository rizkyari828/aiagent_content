using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateIdeaJobHandlerTests
{
    [Fact]
    public async Task Handler_MapsPromptAndReturnsCanonicalStructuredResult()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateIdeaTestData.ValidResult);
        var handler = CreateHandler(projectId, generator);

        var resultJson = await handler.ExecuteAsync(
            GenerateIdeaTestData.CreateJob(
                projectId,
                GenerateIdeaTestData.ValidPayload(projectId, "configured-model")),
            TestContext.Current.CancellationToken);

        Assert.NotNull(generator.Request);
        Assert.Equal("configured-model", generator.Request.Model);
        Assert.Equal(AiResponseFormat.JsonObject, generator.Request.ResponseFormat);
        Assert.Equal(0.3, generator.Request.Temperature);
        Assert.Equal(512, generator.Request.MaxTokens);
        Assert.Contains("Project title: Test project", generator.Request.Prompt);
        Assert.Contains("Response language: Indonesian", generator.Request.Prompt);
        Assert.Contains("targetAudience, suggestedFormat", generator.Request.Prompt);
        Assert.DoesNotContain(
            "Gemma",
            generator.Request.Prompt,
            StringComparison.OrdinalIgnoreCase);

        var result = GenerateIdeaResult.Deserialize(resultJson);
        Assert.Equal("Local AI Content", result.Title);
        Assert.Equal("Creators", result.TargetAudience);
        Assert.Equal("Tutorial", result.SuggestedFormat);
    }

    [Fact]
    public async Task Handler_RejectsMalformedStructuredResponse()
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(projectId, new RecordingTextGenerator("not-json"));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateIdeaTestData.CreateJob(
                    projectId,
                    GenerateIdeaTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("generate_idea_invalid_result", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingProjectBeforeCallingAi()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateIdeaTestData.ValidResult);
        var handler = new GenerateIdeaJobHandler(
            new StubContentProjectReader(null),
            generator);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateIdeaTestData.CreateJob(
                    projectId,
                    GenerateIdeaTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_found", exception.ErrorCode);
        Assert.Null(generator.Request);
    }

    [Fact]
    public async Task Handler_PropagatesCancellationToAiGateway()
    {
        var projectId = Guid.NewGuid();
        var generator = new BlockingTextGenerator();
        var handler = CreateHandler(projectId, generator);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var execution = handler.ExecuteAsync(
            GenerateIdeaTestData.CreateJob(
                projectId,
                GenerateIdeaTestData.ValidPayload(projectId)),
            cancellation.Token);
        await generator.Started.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    [Theory]
    [InlineData(AiErrorCode.ProviderUnavailable, "ai_provider_unavailable")]
    [InlineData(AiErrorCode.Timeout, "ai_timeout")]
    public async Task Handler_MapsAiFailuresForWorkerLifecycle(
        AiErrorCode errorCode,
        string expectedJobErrorCode)
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(projectId, new FailingTextGenerator(errorCode));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateIdeaTestData.CreateJob(
                    projectId,
                    GenerateIdeaTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedJobErrorCode, exception.ErrorCode);
    }

    private static GenerateIdeaJobHandler CreateHandler(
        Guid projectId,
        IAiTextGenerator generator) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(
                    projectId,
                    "Test project",
                    "Test project brief")),
            generator);
}
