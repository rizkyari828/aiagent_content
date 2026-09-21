using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Tests.StoryContext;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateScriptJobHandlerTests
{
    [Fact]
    public async Task Handler_MapsSelectedIdeaAndReturnsCanonicalStructuredScript()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var handler = CreateHandler(projectId, generator);

        var resultJson = await handler.ExecuteAsync(
            GenerateScriptTestData.CreateJob(
                projectId,
                GenerateScriptTestData.ValidPayload(projectId)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(generator.Request);
        Assert.Null(generator.Request.Model);
        Assert.Equal(AiResponseFormat.JsonObject, generator.Request.ResponseFormat);
        Assert.Equal(0.3, generator.Request.Temperature);
        Assert.Equal(3_000, generator.Request.MaxTokens);
        Assert.Equal(false, generator.Request.Think);
        Assert.Contains("Idea title: Local AI Content", generator.Request.Prompt);
        Assert.Contains("Response language: Indonesian", generator.Request.Prompt);
        Assert.Contains("heading, narration", generator.Request.Prompt);

        var result = GenerateScriptResult.Deserialize(resultJson);
        Assert.Equal("Local AI Tutorial", result.Title);
        Assert.Equal(2, result.Sections.Count);
    }

    [Fact]
    public async Task Handler_RejectsMalformedStructuredResponse()
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(projectId, new RecordingTextGenerator("not-json"));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateScriptTestData.CreateJob(
                    projectId,
                    GenerateScriptTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("generate_script_invalid_result", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingProjectBeforeCallingAi()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateScriptTestData.ValidResult);
        var handler = new GenerateScriptJobHandler(
            new StubContentProjectReader(null),
            generator,
            StoryContextTestSupport.Builder());

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateScriptTestData.CreateJob(
                    projectId,
                    GenerateScriptTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_found", exception.ErrorCode);
        Assert.Null(generator.Request);
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
                GenerateScriptTestData.CreateJob(
                    projectId,
                    GenerateScriptTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedJobErrorCode, exception.ErrorCode);
    }

    private static GenerateScriptJobHandler CreateHandler(
        Guid projectId,
        IAiTextGenerator generator) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            generator,
            StoryContextTestSupport.Builder());
}
