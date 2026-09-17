using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Scripts;

public interface IScriptReviewRepository
{
    Task<ReviewedScript?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(ReviewedScript script);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
