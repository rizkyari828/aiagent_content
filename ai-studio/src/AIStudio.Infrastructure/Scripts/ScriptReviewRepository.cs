using AIStudio.Application.Scripts;
using AIStudio.Domain.Scripts;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Scripts;

public sealed class ScriptReviewRepository(ApplicationDbContext dbContext)
    : IScriptReviewRepository
{
    public Task<ReviewedScript?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.ReviewedScripts.SingleOrDefaultAsync(
            script => script.ContentProjectId == contentProjectId,
            cancellationToken);

    public void Add(ReviewedScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        dbContext.ReviewedScripts.Add(script);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
