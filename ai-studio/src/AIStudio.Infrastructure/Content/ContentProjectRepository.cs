using AIStudio.Application.Content;
using AIStudio.Domain.Content;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Content;

public sealed class ContentProjectRepository(ApplicationDbContext dbContext)
    : IContentProjectRepository
{
    public Task<ContentProject?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.ContentProjects.SingleOrDefaultAsync(
            project => project.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<ContentProjectSummary>> ListAsync(
        int take,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.ContentProjects
            .AsNoTracking()
            .OrderByDescending(project => project.UpdatedAt)
            .ThenBy(project => project.Id)
            .Take(take)
            .Select(project => new ContentProjectSummary(
                project.Id,
                project.Title,
                project.Status,
                project.UpdatedAt))
            .ToListAsync(cancellationToken);

        return items;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
