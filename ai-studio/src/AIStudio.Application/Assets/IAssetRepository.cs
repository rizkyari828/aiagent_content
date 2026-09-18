using AIStudio.Domain.Assets;

namespace AIStudio.Application.Assets;

public interface IAssetRepository
{
    Task<SceneAsset?> FindBySceneAsync(
        Guid contentProjectId,
        int sceneIndex,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SceneAsset>> ListByProjectAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(SceneAsset asset);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
