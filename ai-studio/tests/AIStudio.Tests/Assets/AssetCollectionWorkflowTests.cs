using System.Security.Cryptography;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Assets;

public sealed class AssetCollectionWorkflowTests : IDisposable
{
    private readonly string root;

    public AssetCollectionWorkflowTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-asset-workflow-tests",
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
    public async Task Register_AssociatesLocalFileWithStoryboardScene()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var bytes = new byte[] { 10, 20, 30, 40 };
        WriteAsset("scene-0.png", bytes);
        var repository = new RecordingAssetRepository();

        var asset = await CreateWorkflow(repository, projectId, job).RegisterAsync(
            projectId,
            job.Id,
            AssetTestData.Request(sceneIndex: 0),
            TestContext.Current.CancellationToken);

        Assert.NotNull(asset);
        var persisted = Assert.Single(repository.Assets);
        Assert.Equal(projectId, persisted.ContentProjectId);
        Assert.Equal(job.Id, persisted.SourceJobId);
        Assert.Equal(0, persisted.SceneIndex);
        Assert.Equal(AssetType.Image, persisted.Type);
        Assert.Equal("scene-0.png", persisted.Path);
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
        var repository = new RecordingAssetRepository();

        var asset = await CreateWorkflow(repository, projectId: null, job).RegisterAsync(
            projectId,
            job.Id,
            AssetTestData.Request(),
            TestContext.Current.CancellationToken);

        Assert.Null(asset);
        Assert.Empty(repository.Assets);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsMissingStoryboardJob()
    {
        var projectId = Guid.NewGuid();
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job: null).RegisterAsync(
                projectId,
                Guid.NewGuid(),
                AssetTestData.Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_storyboard_not_found", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsStoryboardFromAnotherProject()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(Guid.NewGuid(), GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_storyboard_not_found", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
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
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_storyboard_invalid", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsWrongJobType()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult,
            JobStatus.Succeeded,
            JobType.GenerateScript);
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_storyboard_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsMalformedStoryboardResult()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, "{}");
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_storyboard_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsSceneOutsideStoryboard()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(sceneIndex: 5),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_scene_not_found", exception.ErrorCode);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Register_RejectsDuplicateScene()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAsset("scene-0.png", [1]);
        var repository = new RecordingAssetRepository();
        repository.Add(SceneAsset.Create(
            projectId,
            job.Id,
            0,
            AssetType.Image,
            "existing.png",
            1,
            new string('a', SceneAsset.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now));

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(sceneIndex: 0),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_scene_conflict", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RequiresProvenanceForExternalAsset()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAsset("scene-0.png", [1]);
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(origin: AssetOrigin.External),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_provenance_required", exception.ErrorCode);
        Assert.Empty(repository.Assets);
    }

    [Fact]
    public async Task Register_PersistsExternalProvenance()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAsset("scene-1.png", [1, 2]);
        var repository = new RecordingAssetRepository();
        var retrievedAt = AssetTestData.Now.AddDays(-1);

        var asset = await CreateWorkflow(repository, projectId, job).RegisterAsync(
            projectId,
            job.Id,
            AssetTestData.Request(
                sceneIndex: 1,
                path: "scene-1.png",
                origin: AssetOrigin.External,
                source: "https://example.test/asset",
                creator: "Photographer",
                license: "CC-BY-4.0",
                retrievedAt: retrievedAt),
            TestContext.Current.CancellationToken);

        Assert.NotNull(asset);
        var persisted = Assert.Single(repository.Assets);
        Assert.Equal(AssetOrigin.External, persisted.Origin);
        Assert.Equal("https://example.test/asset", persisted.Source);
        Assert.Equal("Photographer", persisted.Creator);
        Assert.Equal("CC-BY-4.0", persisted.License);
        Assert.Equal(retrievedAt, persisted.RetrievedAt);
    }

    [Fact]
    public async Task Register_RejectsMissingFile()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(path: "missing.png"),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_file_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Register_RejectsPathTraversal()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        var repository = new RecordingAssetRepository();

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => CreateWorkflow(repository, projectId, job).RegisterAsync(
                projectId,
                job.Id,
                AssetTestData.Request(path: "../outside.png"),
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task List_ReturnsAssetsOrderedByScene()
    {
        var projectId = Guid.NewGuid();
        var job = AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);
        WriteAsset("scene-0.png", [1]);
        WriteAsset("scene-1.png", [2]);
        var repository = new RecordingAssetRepository();
        var workflow = CreateWorkflow(repository, projectId, job);
        var cancellationToken = TestContext.Current.CancellationToken;

        await workflow.RegisterAsync(
            projectId,
            job.Id,
            AssetTestData.Request(sceneIndex: 1, path: "scene-1.png"),
            cancellationToken);
        await workflow.RegisterAsync(
            projectId,
            job.Id,
            AssetTestData.Request(sceneIndex: 0, path: "scene-0.png"),
            cancellationToken);

        var assets = await workflow.ListAsync(projectId, cancellationToken);

        Assert.Equal(2, assets.Count);
        Assert.Equal(0, assets[0].SceneIndex);
        Assert.Equal(1, assets[1].SceneIndex);
    }

    private AssetCollectionWorkflow CreateWorkflow(
        RecordingAssetRepository repository,
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

    private void WriteAsset(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
