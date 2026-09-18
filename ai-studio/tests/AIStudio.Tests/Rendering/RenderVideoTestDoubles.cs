using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Tests.Assets;

namespace AIStudio.Tests.Rendering;

internal static class RenderVideoTestData
{
    public static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static SceneAsset SceneAsset(
        Guid contentProjectId,
        Guid storyboardJobId,
        int sceneIndex,
        string path,
        byte[] bytes,
        AssetType type = AssetType.Image,
        string? contentHash = null) =>
        AIStudio.Domain.Assets.SceneAsset.Create(
            contentProjectId,
            storyboardJobId,
            sceneIndex,
            type,
            path,
            bytes.Length,
            contentHash ?? Hash(bytes),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);

    public static NarrationTrack Narration(
        Guid contentProjectId,
        Guid storyboardJobId,
        string path,
        byte[] bytes,
        string? contentHash = null) =>
        NarrationTrack.Create(
            contentProjectId,
            storyboardJobId,
            path,
            bytes.Length,
            contentHash ?? Hash(bytes),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);

    public static ClaimedJob CreateJob(
        Guid contentProjectId,
        Guid storyboardJobId)
    {
        var payload = JsonSerializer.Serialize(
            new RenderVideoJobPayload(contentProjectId, storyboardJobId),
            JsonOptions);
        return new ClaimedJob(
            Guid.NewGuid(),
            contentProjectId,
            JobType.RenderVideo,
            "input-v1",
            payload,
            0,
            2,
            false);
    }
}

internal sealed class RecordingDbContext : IApplicationDbContext
{
    public Job? AddedJob { get; private set; }

    public int SaveCount { get; private set; }

    public void Add(ContentProject project) =>
        throw new InvalidOperationException("Project creation is not used by this workflow.");

    public void Add(Job job) => AddedJob = job;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class StubAssetRepository : IAssetRepository
{
    private readonly List<SceneAsset> assets;

    public StubAssetRepository(params SceneAsset[] assets) => this.assets = [.. assets];

    public Task<SceneAsset?> FindBySceneAsync(
        Guid contentProjectId,
        int sceneIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            assets.SingleOrDefault(
                asset => asset.ContentProjectId == contentProjectId
                    && asset.SceneIndex == sceneIndex));
    }

    public Task<IReadOnlyList<SceneAsset>> ListByProjectAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SceneAsset> result = assets
            .Where(asset => asset.ContentProjectId == contentProjectId)
            .OrderBy(asset => asset.SceneIndex)
            .ToArray();
        return Task.FromResult(result);
    }

    public void Add(SceneAsset asset) => assets.Add(asset);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(1);
}

internal sealed class StubNarrationRepository(NarrationTrack? narration)
    : INarrationRepository
{
    public Task<NarrationTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            narration?.ContentProjectId == contentProjectId ? narration : null);
    }

    public void Add(NarrationTrack track)
    {
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(1);
}

internal sealed class RecordingVideoRenderer(
    VideoRenderOutput? output = null,
    RenderVideoException? failure = null) : IVideoRenderer
{
    public int CallCount { get; private set; }

    public VideoRenderRequest? Request { get; private set; }

    public Task<VideoRenderOutput> RenderAsync(
        VideoRenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        Request = request;

        if (failure is not null)
        {
            throw failure;
        }

        return Task.FromResult(
            output ?? new VideoRenderOutput(
                request.RelativeOutputPath,
                new string('f', 64),
                4_096,
                12.5,
                1280,
                720));
    }
}
