using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Subtitles;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Subtitles;

public sealed class SubtitleEditTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly string root;

    public SubtitleEditTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-subtitle-edit-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SubtitleTrack_UpdateContentMetadata_UpdatesByteSizeAndHash()
    {
        var track = SubtitleTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subs.srt",
            100,
            new string('a', SubtitleTrack.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        var newHash = new string('b', SubtitleTrack.ContentHashLength);
        track.UpdateContentMetadata(250, newHash, DateTimeOffset.UtcNow);

        Assert.Equal(250, track.ByteSize);
        Assert.Equal(newHash, track.ContentHash);
    }

    [Fact]
    public void SubtitleTrack_UpdateContentMetadata_RejectsInvalidInputs()
    {
        var track = SubtitleTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subs.srt",
            100,
            new string('a', SubtitleTrack.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            track.UpdateContentMetadata(0, new string('a', 64)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            track.UpdateContentMetadata(-10, new string('a', 64)));

        Assert.Throws<ArgumentException>(() =>
            track.UpdateContentMetadata(100, "invalid-hash"));
    }

    [Fact]
    public async Task GetContent_ReadsSrtSafelyViaFileStore()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var srtContent = "1\n00:00:00,000 --> 00:00:02,000\nHello World\n";
        WriteSubtitle("test.srt", srtContent);

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("test.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var readContent = await workflow.GetContentAsync(projectId, cancellationToken);

        Assert.NotNull(readContent);
        Assert.Equal(srtContent, readContent);
    }

    [Fact]
    public async Task GetContent_ReturnsNullWhenProjectMissing()
    {
        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId: null, job: null);

        var content = await workflow.GetContentAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Null(content);
    }

    [Fact]
    public async Task GetContent_ThrowsNotFoundWhenSubtitleMissing()
    {
        var projectId = Guid.NewGuid();
        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job: null);

        var exception = await Assert.ThrowsAsync<SubtitleException>(() =>
            workflow.GetContentAsync(projectId, TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateContent_AtomicallyWritesAndRecomputesHashAndSize()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var initialSrt = "1\n00:00:00,000 --> 00:00:02,000\nInitial\n";
        WriteSubtitle("captions.srt", initialSrt);

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("captions.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var updatedSrt = "1\n00:00:00,000 --> 00:00:03,000\nUpdated Text Line\n";
        var updatedSnapshot = await workflow.UpdateContentAsync(
            projectId,
            updatedSrt,
            cancellationToken);

        Assert.NotNull(updatedSnapshot);
        var updatedBytes = System.Text.Encoding.UTF8.GetBytes(updatedSrt);
        var expectedHash = Convert.ToHexString(SHA256.HashData(updatedBytes)).ToLowerInvariant();

        Assert.Equal(updatedBytes.Length, updatedSnapshot.ByteSize);
        Assert.Equal(expectedHash, updatedSnapshot.ContentHash);

        var reRead = await workflow.GetContentAsync(projectId, cancellationToken);
        Assert.Equal(updatedSrt, reRead);
        Assert.True(repository.SaveCount >= 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\n")]
    public async Task UpdateContent_RejectsEmptyOrWhitespace(string invalidSrt)
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteSubtitle("test.srt", "1\n00:00:00,000 --> 00:00:02,000\nInitial\n");

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("test.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var exception = await Assert.ThrowsAsync<SubtitleException>(() =>
            workflow.UpdateContentAsync(projectId, invalidSrt, cancellationToken));

        Assert.Equal("subtitle_content_invalid", exception.ErrorCode);
    }

    [Theory]
    [InlineData("Not a valid srt file at all")]
    [InlineData("1\n00:00:00,000 -> 00:00:02,000\nMissing arrow dash\n")]
    [InlineData("abc\n00:00:00,000 --> 00:00:02,000\nInvalid index\n")]
    [InlineData("1\n00:00:00,000 --> 00:00:02,000\n")]
    public async Task UpdateContent_RejectsMalformedSrt(string malformedSrt)
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteSubtitle("test.srt", "1\n00:00:00,000 --> 00:00:02,000\nInitial\n");

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("test.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var exception = await Assert.ThrowsAsync<SubtitleException>(() =>
            workflow.UpdateContentAsync(projectId, malformedSrt, cancellationToken));

        Assert.Equal("subtitle_content_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateContent_ThrowsNotFoundWhenSubtitleMissing()
    {
        var projectId = Guid.NewGuid();
        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job: null);

        var exception = await Assert.ThrowsAsync<SubtitleException>(() =>
            workflow.UpdateContentAsync(
                projectId,
                "1\n00:00:00,000 --> 00:00:02,000\nHello\n",
                TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task SubtitleEndpoints_GetContent_ReturnsOkWithResponse()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var srt = "1\n00:00:00,000 --> 00:00:02,000\nApi Subtitle\n";
        WriteSubtitle("api.srt", srt);

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("api.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var result = await SubtitleEndpoints.GetSubtitleContentAsync(
            projectId.ToString(),
            workflow,
            cancellationToken);

        var ok = Assert.IsType<Ok<SubtitleContentResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(projectId, ok.Value.ContentProjectId);
        Assert.Equal("api.srt", ok.Value.Path);
        Assert.Equal(srt, ok.Value.Content);
        Assert.True(ok.Value.ByteSize > 0);
        Assert.Equal(64, ok.Value.ContentHash.Length);
    }

    [Fact]
    public async Task SubtitleEndpoints_GetContent_ReturnsNotFoundForMissingSubtitle()
    {
        var projectId = Guid.NewGuid();
        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job: null);

        var result = await SubtitleEndpoints.GetSubtitleContentAsync(
            projectId.ToString(),
            workflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("subtitle_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task SubtitleEndpoints_PutContent_UpdatesAndReturnsOk()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var initialSrt = "1\n00:00:00,000 --> 00:00:02,000\nOld\n";
        WriteSubtitle("subs.srt", initialSrt);

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("subs.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var updatedSrt = "1\n00:00:00,000 --> 00:00:03,000\nNew Edited Line\n";
        var result = await SubtitleEndpoints.UpdateSubtitleContentAsync(
            projectId.ToString(),
            new UpdateSubtitleContentRequest(updatedSrt),
            workflow,
            cancellationToken);

        var ok = Assert.IsType<Ok<SubtitleContentResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(updatedSrt, ok.Value.Content);

        var json = JsonSerializer.SerializeToElement(ok.Value, WebJson);
        Assert.Equal(updatedSrt, json.GetProperty("content").GetString());
        Assert.Equal("subs.srt", json.GetProperty("path").GetString());
    }

    [Fact]
    public async Task SubtitleEndpoints_PutContent_RejectsMalformedContent()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteSubtitle("subs.srt", "1\n00:00:00,000 --> 00:00:02,000\nValid\n");

        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("subs.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var result = await SubtitleEndpoints.UpdateSubtitleContentAsync(
            projectId.ToString(),
            new UpdateSubtitleContentRequest("malformed non-srt text"),
            workflow,
            cancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("subtitle_content_invalid", problem.ProblemDetails.Extensions["code"]);
    }

    private SubtitleWorkflow CreateWorkflow(
        StubSubtitleRepository repository,
        Guid? projectId,
        JobSnapshot? job) =>
        new(
            repository,
            new StubContentProjectReader(
                projectId is null
                    ? null
                    : new ContentProjectSnapshot(projectId.Value, "Project", "Brief")),
            new StubJobReader(job),
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })),
            new AssetStubTimeProvider(AssetTestData.Now));

    private void WriteSubtitle(string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
