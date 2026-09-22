namespace AIStudio.Application.IdentityAssets;

/// <summary>
/// Registry-owned durable metadata. It persists identity-asset metadata only —
/// never bytes, physical paths, storage keys, or provider details. Implementations
/// own the physical mapping and keep it private, mirroring the byte authority split
/// of <see cref="IIdentityAssetStore"/>. The port is synchronous because the
/// registry contract is synchronous and the workload is a small local set.
/// </summary>
public interface IIdentityAssetMetadataPersistence
{
    /// <summary>
    /// Loads every durable metadata record. Called once while the registry starts so
    /// Draft/Approved state, approval times, and version history survive restart.
    /// </summary>
    IReadOnlyList<IdentityAsset> LoadAll();

    /// <summary>
    /// Durably creates a new metadata record. A colliding <c>(id, version)</c> must
    /// fail rather than overwrite durable state.
    /// </summary>
    void Create(IdentityAsset asset);

    /// <summary>Durably replaces an existing metadata record (the approval transition).</summary>
    void Update(IdentityAsset asset);
}
