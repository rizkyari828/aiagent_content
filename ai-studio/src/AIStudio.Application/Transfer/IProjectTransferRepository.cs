namespace AIStudio.Application.Transfer;

/// <summary>
/// Persistence boundary for one project transfer. Reads return domain rows; the import
/// write is the only operation allowed to create an exact historical snapshot inside a
/// single database transaction.
/// </summary>
public interface IProjectTransferRepository
{
    Task<bool> ExistsAsync(Guid contentProjectId, CancellationToken cancellationToken);

    Task<ProjectTransferSnapshot?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the manifest's project and related rows in one transaction. Throws
    /// <see cref="ProjectTransferException"/> with a stable code when the snapshot cannot
    /// be committed (including an identifier conflict).
    /// </summary>
    Task<ProjectImportCounts> ImportAsync(
        ProjectTransferManifest manifest,
        CancellationToken cancellationToken);
}
