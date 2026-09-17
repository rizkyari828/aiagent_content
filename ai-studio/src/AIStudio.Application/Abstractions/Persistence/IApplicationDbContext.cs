using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Abstractions.Persistence;

public interface IApplicationDbContext
{
    void Add(ContentProject project);

    void Add(Job job);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
