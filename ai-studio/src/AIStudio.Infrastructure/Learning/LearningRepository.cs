using AIStudio.Application.Learning;
using AIStudio.Domain.Learning;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Learning;

internal sealed class LearningRepository(ApplicationDbContext dbContext) : ILearningRepository
{
    public async Task<IReadOnlyList<LearningGuideline>> ListAsync(
        string? status,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.LearningGuidelines.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(g => g.Status == status);
        }

        return await query
            .OrderByDescending(g => g.UpdatedAt)
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public Task<LearningGuideline?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LearningGuidelines.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public void Add(LearningGuideline guideline) =>
        dbContext.LearningGuidelines.Add(guideline);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
