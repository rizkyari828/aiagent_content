using AIStudio.Domain.Content;

namespace AIStudio.Application.Content;

/// <summary>
/// Content-project write/read boundary owned by the content module. Reads are
/// no-tracking except <see cref="FindByIdAsync"/>, which returns the tracked aggregate
/// used by the explicit update/transition workflows.
/// </summary>
public interface IContentProjectRepository
{
    Task<ContentProject?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentProjectSummary>> ListAsync(
        int take,
        CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
