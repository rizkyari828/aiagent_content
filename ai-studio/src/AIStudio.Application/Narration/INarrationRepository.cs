using AIStudio.Domain.Narration;

namespace AIStudio.Application.Narration;

public interface INarrationRepository
{
    Task<NarrationTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(NarrationTrack track);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
