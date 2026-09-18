using System.Security.Cryptography;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Narration;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Narration;

public sealed class NarrationWorkflowTests : IDisposable
{
    private readonly string root;

    public NarrationWorkflowTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-narration-tests",
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
    public async Task Register_AssociatesLocalNarrationWithStoryboard()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var bytes = new byte[] { 5, 6, 7, 8 };
        WriteAudio("narration.wav", bytes);
        var repository = new RecordingNarrationRepository();

        var narration = await CreateWorkflow(repository, projectId, job).RegisterAsync(
            projectId,
            job.Id,
            new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
            TestContext.Current.CancellationToken);

        Assert.NotNull(narration);
        var persisted = Assert.IsType<NarrationTrack>(repository.Track);
        Assert.Equal(projectId, persisted.ContentProjectId);
        Assert.Equal(job.Id, persisted.SourceJobId);
        Assert.Equal("narration.wav", persisted.Path);
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
        var repository = new RecordingNarrationRepository();

        var narration = await CreateWorkflow(repository, projectId: null, job).RegisterAsync(
            projectId,
            job.Id,
            new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
            TestContext.Current.CancellationToken);

        Assert.Null(narration);
        Assert.Null(repository.Track);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsMissingStoryboardJob()
    {
        var projectId = Guid.NewGuid();
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job: null).RegisterAsync(
                projectId,
                Guid.NewGuid(),
                new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("narration_storyboard_not_found", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsStoryboardFromAnotherProject()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(Guid.NewGuid(), GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("narration_storyboard_not_found", exception.ErrorCode);
    }

    [Theory]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Failed)]
    public async Task Register_RejectsIncompleteStoryboard(JobStatus status)
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult,
            status);
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("narration_storyboard_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsMalformedStoryboardResult()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, "{}");
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("narration_storyboard_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsDuplicateNarration()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAudio("narration.wav", [1]);
        var repository = new RecordingNarrationRepository();
        repository.Add(NarrationTrack.Create(
            projectId,
            job.Id,
            "existing.wav",
            1,
            new string('a', NarrationTrack.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now));

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("narration_conflict", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RequiresProvenanceForExternalNarration()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAudio("narration.wav", [1]);
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("narration.wav", AssetOrigin.External, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("narration_provenance_required", exception.ErrorCode);
        Assert.Null(repository.Track);
    }

    [Fact]
    public async Task Register_PersistsExternalProvenance()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAudio("narration.mp3", [1, 2]);
        var repository = new RecordingNarrationRepository();
        var retrievedAt = AssetTestData.Now.AddDays(-2);

        var narration = await CreateWorkflow(repository, projectId, job).RegisterAsync(
            projectId,
            job.Id,
            new RegisterNarration(
                "narration.mp3",
                AssetOrigin.External,
                "https://example.test/voice",
                "Voiceover artist",
                "CC-BY-4.0",
                retrievedAt),
            TestContext.Current.CancellationToken);

        Assert.NotNull(narration);
        var persisted = Assert.IsType<NarrationTrack>(repository.Track);
        Assert.Equal(AssetOrigin.External, persisted.Origin);
        Assert.Equal("https://example.test/voice", persisted.Source);
        Assert.Equal("Voiceover artist", persisted.Creator);
        Assert.Equal("CC-BY-4.0", persisted.License);
        Assert.Equal(retrievedAt, persisted.RetrievedAt);
    }

    [Fact]
    public async Task Register_RejectsMissingAudioFile()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("missing.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_file_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsPathTraversalAndRootEscape()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingNarrationRepository();

        var exception = await Assert.ThrowsAsync<NarrationException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                new RegisterNarration("../outside.wav", AssetOrigin.Local, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Find_ReturnsRegisteredNarration()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAudio("narration.wav", [9]);
        var repository = new RecordingNarrationRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            new RegisterNarration("narration.wav", AssetOrigin.Local, null, null, null, null),
            cancellationToken);

        var found = await workflow.FindAsync(projectId, cancellationToken);
        var missing = await workflow.FindAsync(Guid.NewGuid(), cancellationToken);

        Assert.NotNull(found);
        Assert.Equal("narration.wav", found.Path);
        Assert.Null(missing);
    }

    private NarrationWorkflow CreateWorkflow(
        RecordingNarrationRepository repository,
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

    private void WriteAudio(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
