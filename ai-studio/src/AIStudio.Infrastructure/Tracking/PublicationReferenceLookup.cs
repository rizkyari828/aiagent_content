using AIStudio.Application.Approvals;
using AIStudio.Application.Tracking;

namespace AIStudio.Infrastructure.Tracking;

/// <summary>
/// Composes the publishing module's existing application read port so a metric snapshot
/// can be bound to the publication record that already exists for the project. It reads
/// no other table, duplicates no publication rule, and never creates a publication.
/// </summary>
public sealed class PublicationReferenceLookup(IPublicationRepository publications)
    : IPublicationReferenceLookup
{
    public async Task<Guid?> FindPublicationRecordIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var record = await publications.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        return record?.Id;
    }
}
