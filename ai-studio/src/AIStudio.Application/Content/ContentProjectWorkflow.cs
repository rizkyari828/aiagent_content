using AIStudio.Application.Jobs;
using AIStudio.Domain.Content;

namespace AIStudio.Application.Content;

/// <summary>
/// Content-project read/list/update workflow. It owns only the safe, operator-editable
/// fields: status is never writable here, and the only status change lives in
/// <see cref="ContentReadinessWorkflow"/> behind the readiness gate.
/// </summary>
public sealed class ContentProjectWorkflow(
    IContentProjectRepository projects,
    IProjectJobReader jobs,
    TimeProvider timeProvider)
{
    public const int MinimumTake = 1;
    public const int MaximumTake = 100;
    public const int DefaultTake = 20;

    public async Task<IReadOnlyList<ContentProjectSummary>> ListAsync(
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < MinimumTake or > MaximumTake)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take),
                $"take must be between {MinimumTake} and {MaximumTake}.");
        }

        return await projects.ListAsync(take, cancellationToken);
    }

    /// <summary>Returns <c>null</c> when the content project does not exist.</summary>
    public async Task<ContentProjectDetails?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var project = await projects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var lastJob = await jobs.FindLatestAsync(contentProjectId, cancellationToken);
        return ToDetails(project, lastJob);
    }

    /// <summary>
    /// Replaces the editable fields (title, brief). Returns <c>null</c> when the content
    /// project does not exist and throws <see cref="ArgumentException"/> for invalid
    /// values. An unchanged request is a no-op so <c>updatedAt</c> stays stable.
    /// </summary>
    public async Task<ContentProjectDetails?> UpdateAsync(
        Guid contentProjectId,
        string title,
        string? brief,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        var project = await projects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        if (project.UpdateDetails(title, brief, timeProvider.GetUtcNow()))
        {
            await projects.SaveChangesAsync(cancellationToken);
        }

        var lastJob = await jobs.FindLatestAsync(contentProjectId, cancellationToken);
        return ToDetails(project, lastJob);
    }

    internal static ContentProjectDetails ToDetails(
        ContentProject project,
        ProjectJobSummary? lastJob) =>
        new(
            project.Id,
            project.Title,
            project.Brief,
            project.Status,
            project.CreatedAt,
            project.UpdatedAt,
            lastJob);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }
}
