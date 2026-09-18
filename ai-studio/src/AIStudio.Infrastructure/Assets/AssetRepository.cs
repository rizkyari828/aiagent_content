using AIStudio.Application.Assets;
using AIStudio.Domain.Assets;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Assets;

public sealed class AssetRepository(ApplicationDbContext dbContext) : IAssetRepository
{
    public Task<SceneAsset?> FindBySceneAsync(
        Guid contentProjectId,
        int sceneIndex,
        CancellationToken cancellationToken) =>
        dbContext.SceneAssets.SingleOrDefaultAsync(
            asset => asset.ContentProjectId == contentProjectId
                && asset.SceneIndex == sceneIndex,
            cancellationToken);

    public async Task<IReadOnlyList<SceneAsset>> ListByProjectAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        await dbContext.SceneAssets
            .AsNoTracking()
            .Where(asset => asset.ContentProjectId == contentProjectId)
            .OrderBy(asset => asset.SceneIndex)
            .ToListAsync(cancellationToken);

    public void Add(SceneAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        dbContext.SceneAssets.Add(asset);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
