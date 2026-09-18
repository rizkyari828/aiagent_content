using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateStoryboardJobHandlerTests
{
    [Fact]
    public async Task Handler_MapsApprovedScriptAndReturnsCanonicalStructuredStoryboard()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, ScriptReviewStatus.Approved);

        var resultJson = await handler.ExecuteAsync(
            GenerateStoryboardTestData.CreateJob(
                projectId,
                GenerateStoryboardTestData.ValidPayload(projectId)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(generator.Request);
        Assert.Null(generator.Request.Model);
        Assert.Equal(AiResponseFormat.JsonObject, generator.Request.ResponseFormat);
        Assert.Equal(0.3, generator.Request.Temperature);
        Assert.Equal(3_000, generator.Request.MaxTokens);
        Assert.Contains("Script title: Local AI Tutorial", generator.Request.Prompt);
        Assert.Contains("Section 1 heading: Why local AI", generator.Request.Prompt);
        Assert.Contains("heading, visual", generator.Request.Prompt);

        var result = GenerateStoryboardResult.Deserialize(resultJson);
        Assert.Equal("Local AI Storyboard", result.Title);
        Assert.Equal(2, result.Scenes.Count);
        Assert.Equal(
            GenerateStoryboardResult.Deserialize(GenerateStoryboardTestData.ValidResult).Serialize(),
            resultJson);
    }

    [Fact]
    public async Task Handler_RejectsDraftScriptBeforeCallingAi()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = CreateHandler(projectId, generator, ScriptReviewStatus.Draft);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateStoryboardTestData.CreateJob(
                    projectId,
                    GenerateStoryboardTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("storyboard_script_not_approved", exception.ErrorCode);
        Assert.Null(generator.Request);
    }

    [Fact]
    public async Task Handler_RejectsMissingScriptBeforeCallingAi()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = new GenerateStoryboardJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            new StubScriptReviewRepository(null),
            generator);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateStoryboardTestData.CreateJob(
                    projectId,
                    GenerateStoryboardTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("storyboard_script_not_found", exception.ErrorCode);
        Assert.Null(generator.Request);
    }

    [Fact]
    public async Task Handler_RejectsMissingProjectBeforeCallingAi()
    {
        var projectId = Guid.NewGuid();
        var generator = new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult);
        var handler = new GenerateStoryboardJobHandler(
            new StubContentProjectReader(null),
            new StubScriptReviewRepository(
                GenerateStoryboardTestData.Script(projectId, ScriptReviewStatus.Approved)),
            generator);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateStoryboardTestData.CreateJob(
                    projectId,
                    GenerateStoryboardTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_found", exception.ErrorCode);
        Assert.Null(generator.Request);
    }

    [Fact]
    public async Task Handler_RejectsMalformedStructuredResponse()
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(
            projectId,
            new RecordingTextGenerator("not-json"),
            ScriptReviewStatus.Approved);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateStoryboardTestData.CreateJob(
                    projectId,
                    GenerateStoryboardTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal("generate_storyboard_invalid_result", exception.ErrorCode);
    }

    [Theory]
    [InlineData(AiErrorCode.ProviderUnavailable, "ai_provider_unavailable")]
    [InlineData(AiErrorCode.Timeout, "ai_timeout")]
    public async Task Handler_MapsAiFailuresForWorkerLifecycle(
        AiErrorCode errorCode,
        string expectedJobErrorCode)
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(
            projectId,
            new FailingTextGenerator(errorCode),
            ScriptReviewStatus.Approved);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                GenerateStoryboardTestData.CreateJob(
                    projectId,
                    GenerateStoryboardTestData.ValidPayload(projectId)),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedJobErrorCode, exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_CanonicalResultCanBeStoredAsSucceededJobResult()
    {
        var projectId = Guid.NewGuid();
        var now = GenerateStoryboardTestData.Created;
        var payload = GenerateStoryboardTestData.ValidPayload(projectId);
        var handler = CreateHandler(
            projectId,
            new RecordingTextGenerator(GenerateStoryboardTestData.ValidResult),
            ScriptReviewStatus.Approved);
        var job = Job.Create(
            projectId,
            JobType.GenerateStoryboard,
            "input-v1",
            payload,
            now);
        job.Start("worker-1", now.AddMinutes(2), now);

        var resultJson = await handler.ExecuteAsync(
            new ClaimedJob(
                job.Id,
                projectId,
                JobType.GenerateStoryboard,
                "input-v1",
                payload,
                0,
                2,
                false),
            TestContext.Current.CancellationToken);
        job.Succeed(resultJson, now.AddMinutes(1));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(
            GenerateStoryboardResult.Deserialize(resultJson).Serialize(),
            job.Result);
    }

    private static GenerateStoryboardJobHandler CreateHandler(
        Guid projectId,
        IAiTextGenerator generator,
        ScriptReviewStatus status) =>
        new(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Test project", "Test brief")),
            new StubScriptReviewRepository(
                GenerateStoryboardTestData.Script(projectId, status)),
            generator);
}
