using AIStudio.Application.Assets;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Assets;

internal static class AssetTestData
{
    public static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public static JobSnapshot StoryboardJob(
        Guid contentProjectId,
        string? result = null,
        JobStatus status = JobStatus.Succeeded,
        JobType type = JobType.GenerateStoryboard)
    {
        var now = Now;
        return new JobSnapshot(
            Guid.NewGuid(),
            contentProjectId,
            type,
            status,
            0,
            2,
            result,
            null,
            null,
            now,
            now,
            now,
            now);
    }

    public static RegisterSceneAsset Request(
        int sceneIndex = 0,
        string path = "scene-0.png",
        AssetType type = AssetType.Image,
        AssetOrigin origin = AssetOrigin.Local,
        string? source = null,
        string? creator = null,
        string? license = null,
        DateTimeOffset? retrievedAt = null) =>
        new(sceneIndex, path, type, origin, source, creator, license, retrievedAt);
}

internal sealed class RecordingAssetRepository : IAssetRepository
{
    private readonly List<SceneAsset> assets = [];

    public IReadOnlyList<SceneAsset> Assets => assets;

    public int SaveCount { get; private set; }

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

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class StubJobReader(JobSnapshot? job) : IJobReader
{
    public Task<JobSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(job?.Id == id ? job : null);
    }
}

internal sealed class AssetStubTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
