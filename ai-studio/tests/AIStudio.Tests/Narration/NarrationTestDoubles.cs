using AIStudio.Application.Narration;
using AIStudio.Domain.Narration;

namespace AIStudio.Tests.Narration;

internal sealed class RecordingNarrationRepository : INarrationRepository
{
    public NarrationTrack? Track { get; private set; }

    public int SaveCount { get; private set; }

    public Task<NarrationTrack?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Track?.ContentProjectId == contentProjectId ? Track : null);
    }

    public void Add(NarrationTrack track) => Track = track;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}
