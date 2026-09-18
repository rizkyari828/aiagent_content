using System.Security.Cryptography;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Subtitles;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Subtitles;

public sealed class SubtitleWorkflowTests : IDisposable
{
    private readonly string root;

    public SubtitleWorkflowTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-subtitle-tests",
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
    public async Task Register_AssociatesLocalSubtitleWithStoryboard()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var bytes = "1\n00:00:00,000 --> 00:00:02,000\nHello\n"u8.ToArray();
        WriteSubtitle("subtitle.srt", bytes);
        var repository = new StubSubtitleRepository();

        var subtitle = await CreateWorkflow(repository, projectId, job).RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("subtitle.srt", AssetOrigin.Local, null, null, null, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(subtitle);
        var persisted = Assert.IsType<SubtitleTrack>(repository.Subtitle);
        Assert.Equal(projectId, persisted.ContentProjectId);
        Assert.Equal(job.Id, persisted.SourceJobId);
        Assert.Equal("subtitle.srt", persisted.Path);
        Assert.Equal(bytes.Length, persisted.ByteSize);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            persisted.ContentHash);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Register_ReturnsNullWhenProjectMissing()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new StubSubtitleRepository();

        var subtitle = await CreateWorkflow(repository, projectId: null, job).RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("subtitle.srt", AssetOrigin.Local, null, null, null, null),
            TestContext.Current.CancellationToken);

        Assert.Null(subtitle);
        Assert.Null(repository.Subtitle);
    }

    [Fact]
    public async Task Register_RejectsMissingStoryboardJob()
    {
        var projectId = Guid.NewGuid();
        var repository = new StubSubtitleRepository();

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job: null).RegisterAsync(
                projectId,
                Guid.NewGuid(),
                new RegisterSubtitle("subtitle.srt", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_storyboard_not_found", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsIncompleteStoryboard()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult,
            JobStatus.Running);
        var repository = new StubSubtitleRepository();

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterSubtitle("subtitle.srt", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_storyboard_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsDuplicateSubtitle()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteSubtitle("subtitle.srt", [1]);
        var repository = new StubSubtitleRepository();
        repository.Add(SubtitleTrack.Create(
            projectId,
            job.Id,
            "existing.srt",
            1,
            new string('a', SubtitleTrack.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now));

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterSubtitle("subtitle.srt", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_conflict", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsUnsupportedSubtitleFormat()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new StubSubtitleRepository();

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterSubtitle("subtitle.vtt", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_format_unsupported", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RequiresProvenanceForExternalSubtitle()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteSubtitle("subtitle.srt", [1]);
        var repository = new StubSubtitleRepository();

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterSubtitle("subtitle.srt", AssetOrigin.External, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("subtitle_provenance_required", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsMissingSubtitleFile()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new StubSubtitleRepository();

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterSubtitle("missing.srt", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_file_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsPathTraversal()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new StubSubtitleRepository();

        var exception = await Assert.ThrowsAsync<SubtitleException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterSubtitle("../outside.srt", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Find_ReturnsRegisteredSubtitle()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteSubtitle("subtitle.srt", [9]);
        var repository = new StubSubtitleRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterSubtitle("subtitle.srt", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var found = await workflow.FindAsync(projectId, cancellationToken);
        var missing = await workflow.FindAsync(Guid.NewGuid(), cancellationToken);

        Assert.NotNull(found);
        Assert.Equal("subtitle.srt", found.Path);
        Assert.Null(missing);
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

    private void WriteSubtitle(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
