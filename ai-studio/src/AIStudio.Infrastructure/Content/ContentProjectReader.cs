using AIStudio.Application.Content;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Content;

public sealed class ContentProjectReader(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IContentProjectReader
{
    public async Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ContentProjects
            .AsNoTracking()
            .Where(project => project.Id == id)
            .Select(project => new ContentProjectSnapshot(
                project.Id,
                project.Title,
                project.Brief))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
