using System.Reflection;
using AIStudio.Application.Transfer;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Domain.Scripts;
using AIStudio.Domain.Subtitles;
using AIStudio.Domain.Tracking;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Transfer;

/// <summary>
/// EF Core adapter for project transfer. Reads are projections over the owning tables.
/// Import reconstructs an exact historical snapshot (ids, statuses, revisions,
/// timestamps) in one transaction; because that is a restore rather than normal domain
/// evolution, the immutable private setters are restored explicitly instead of driving
/// state machines that would reject a legitimate historical value.
/// </summary>
public sealed class ProjectTransferRepository(ApplicationDbContext dbContext)
    : IProjectTransferRepository
{
    public Task<bool> ExistsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.ContentProjects
            .AsNoTracking()
            .AnyAsync(project => project.Id == contentProjectId, cancellationToken);

    public async Task<ProjectTransferSnapshot?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.ContentProjects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == contentProjectId,
                cancellationToken);
        if (project is null)
        {
            return null;
        }

        var jobs = await dbContext.Jobs
            .AsNoTracking()
            .Where(job => job.ContentProjectId == contentProjectId)
            .OrderBy(job => job.CreatedAt)
            .ThenBy(job => job.Id)
            .ToListAsync(cancellationToken);
        var reviewedScript = await dbContext.ReviewedScripts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                script => script.ContentProjectId == contentProjectId,
                cancellationToken);
        var sceneAssets = await dbContext.SceneAssets
            .AsNoTracking()
            .Where(asset => asset.ContentProjectId == contentProjectId)
            .OrderBy(asset => asset.SceneIndex)
            .ThenBy(asset => asset.Id)
            .ToListAsync(cancellationToken);
        var narration = await dbContext.NarrationTracks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                track => track.ContentProjectId == contentProjectId,
                cancellationToken);
        var subtitle = await dbContext.SubtitleTracks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                track => track.ContentProjectId == contentProjectId,
                cancellationToken);
        var approval = await dbContext.FinalApprovals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ContentProjectId == contentProjectId,
                cancellationToken);
        var publication = await dbContext.PublicationRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.ContentProjectId == contentProjectId,
                cancellationToken);
        var metrics = await dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(metric => metric.ContentProjectId == contentProjectId)
            .OrderBy(metric => metric.CapturedAt)
            .ThenBy(metric => metric.Id)
            .ToListAsync(cancellationToken);
        var timeEntries = await dbContext.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .OrderBy(entry => entry.RecordedAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var costEntries = await dbContext.CostEntries
            .AsNoTracking()
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .OrderBy(entry => entry.IncurredAt)
            .ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);

        return new ProjectTransferSnapshot(
            project,
            reviewedScript,
            jobs,
            sceneAssets,
            narration,
            subtitle,
            approval,
            publication,
            metrics,
            timeEntries,
            costEntries);
    }

    public async Task<ProjectImportCounts> ImportAsync(
        ProjectTransferManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var project = CreateProject(manifest.Project);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        try
        {
            dbContext.ContentProjects.Add(project);
            await dbContext.SaveChangesAsync(cancellationToken);

            var jobs = manifest.SourceJobs.Select(CreateJob).ToArray();
            dbContext.Jobs.AddRange(jobs);
            await dbContext.SaveChangesAsync(cancellationToken);

            ReviewedScript? script = null;
            if (manifest.ReviewedScript is not null)
            {
                script = CreateReviewedScript(manifest.ReviewedScript);
                dbContext.ReviewedScripts.Add(script);
            }

            foreach (var asset in manifest.SceneAssets)
            {
                dbContext.SceneAssets.Add(CreateSceneAsset(asset));
            }

            if (manifest.NarrationTrack is not null)
            {
                dbContext.NarrationTracks.Add(CreateNarration(manifest.NarrationTrack));
            }

            if (manifest.SubtitleTrack is not null)
            {
                dbContext.SubtitleTracks.Add(CreateSubtitle(manifest.SubtitleTrack));
            }

            FinalApproval? approval = null;
            if (manifest.FinalApproval is not null)
            {
                approval = CreateFinalApproval(manifest.FinalApproval);
                dbContext.FinalApprovals.Add(approval);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            PublicationRecord? publication = null;
            if (manifest.PublicationRecord is not null)
            {
                publication = CreatePublication(manifest.PublicationRecord);
                dbContext.PublicationRecords.Add(publication);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            foreach (var metric in manifest.MetricSnapshots)
            {
                dbContext.MetricSnapshots.Add(CreateMetric(metric));
            }

            foreach (var entry in manifest.TimeEntries)
            {
                dbContext.TimeEntries.Add(CreateTimeEntry(entry));
            }

            foreach (var entry in manifest.CostEntries)
            {
                dbContext.CostEntries.Add(CreateCostEntry(entry));
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ProjectImportCounts(
                script is null ? 0 : 1,
                jobs.Length,
                manifest.SceneAssets.Count,
                manifest.NarrationTrack is null ? 0 : 1,
                manifest.SubtitleTrack is null ? 0 : 1,
                approval is null ? 0 : 1,
                publication is null ? 0 : 1,
                manifest.MetricSnapshots.Count,
                manifest.TimeEntries.Count,
                manifest.CostEntries.Count);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ProjectTransferException(
                "import_project_conflict",
                "The package contains an identifier that already exists; v1 never overwrites.",
                exception);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ProjectTransferException(
                "import_package_invalid",
                "The package rows could not be stored under the current database constraints.",
                exception);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            await RollbackAsync(transaction, cancellationToken);
            throw new ProjectTransferException(
                "import_package_invalid",
                "The package contains a value that is not valid for this schema.",
                exception);
        }
        catch
        {
            await RollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private static ContentProject CreateProject(ProjectManifestProject source)
    {
        var project = ContentProject.Create(
            source.Title,
            source.Brief,
            source.CreatedAt,
            source.Id);
        Set(project, nameof(ContentProject.Status), source.Status);
        Set(project, nameof(ContentProject.UpdatedAt), source.UpdatedAt);
        return project;
    }

    private static Job CreateJob(ProjectManifestJob source)
    {
        var job = Job.Create(
            source.ContentProjectId,
            source.Type,
            source.InputVersionHash,
            source.Payload.GetRawText(),
            source.CreatedAt,
            source.MaxRetries,
            source.Id);
        Set(job, nameof(Job.Status), source.Status);
        Set(job, nameof(Job.RetryCount), source.RetryCount);
        Set(job, nameof(Job.Result), source.Result?.GetRawText());
        Set(job, nameof(Job.StartedAt), source.StartedAt);
        Set(job, nameof(Job.CompletedAt), source.CompletedAt);
        Set(job, nameof(Job.UpdatedAt), source.UpdatedAt);
        return job;
    }

    private static ReviewedScript CreateReviewedScript(ProjectManifestReviewedScript source)
    {
        var script = ReviewedScript.Create(
            source.ContentProjectId,
            source.SourceJobId,
            source.Content.GetRawText(),
            source.CreatedAt,
            source.Id);
        Set(script, nameof(ReviewedScript.Status), source.Status);
        Set(script, nameof(ReviewedScript.Revision), source.Revision);
        Set(script, nameof(ReviewedScript.UpdatedAt), source.UpdatedAt);
        Set(script, nameof(ReviewedScript.ApprovedAt), source.ApprovedAt);
        return script;
    }

    private static SceneAsset CreateSceneAsset(ProjectManifestSceneAsset source) =>
        SceneAsset.Create(
            source.ContentProjectId,
            source.SourceJobId,
            source.SceneIndex,
            source.Type,
            source.Path,
            source.ByteSize,
            source.ContentHash,
            source.Origin,
            source.Source,
            source.Creator,
            source.License,
            source.RetrievedAt,
            source.CreatedAt,
            source.Id);

    private static NarrationTrack CreateNarration(ProjectManifestMediaTrack source) =>
        NarrationTrack.Create(
            source.ContentProjectId,
            source.SourceJobId,
            source.Path,
            source.ByteSize,
            source.ContentHash,
            source.Origin,
            source.Source,
            source.Creator,
            source.License,
            source.RetrievedAt,
            source.CreatedAt,
            source.Id);

    private static SubtitleTrack CreateSubtitle(ProjectManifestMediaTrack source) =>
        SubtitleTrack.Create(
            source.ContentProjectId,
            source.SourceJobId,
            source.Path,
            source.ByteSize,
            source.ContentHash,
            source.Origin,
            source.Source,
            source.Creator,
            source.License,
            source.RetrievedAt,
            source.CreatedAt,
            source.Id);

    private static FinalApproval CreateFinalApproval(ProjectManifestFinalApproval source)
    {
        var approval = FinalApproval.Create(
            source.ContentProjectId,
            source.SourceRenderJobId,
            source.ArtifactPath,
            source.ArtifactContentHash,
            source.ArtifactByteSize,
            source.Destination,
            source.Notes,
            source.ApproverLabel,
            source.ApprovedAt,
            source.Id);
        Set(approval, nameof(FinalApproval.Revision), source.Revision);
        Set(approval, nameof(FinalApproval.CreatedAt), source.CreatedAt);
        Set(approval, nameof(FinalApproval.UpdatedAt), source.UpdatedAt);
        return approval;
    }

    private static PublicationRecord CreatePublication(ProjectManifestPublication source) =>
        PublicationRecord.Create(
            source.ContentProjectId,
            source.FinalApprovalId,
            source.FinalApprovalRevision,
            source.ArtifactContentHash,
            source.Platform,
            source.Url,
            source.VideoId,
            source.PublishedAt,
            source.Notes,
            source.CreatedAt,
            source.Id);

    private static MetricSnapshot CreateMetric(ProjectManifestMetricSnapshot source) =>
        MetricSnapshot.Create(
            source.ContentProjectId,
            source.PublicationRecordId,
            source.Views,
            source.Impressions,
            source.ClickThroughRate,
            source.AverageViewDurationSeconds,
            source.WatchTimeMinutes,
            source.SubscribersGained,
            source.CapturedAt,
            source.Source,
            source.Notes,
            source.CreatedAt,
            source.Id);

    private static TimeEntry CreateTimeEntry(ProjectManifestTimeEntry source) =>
        TimeEntry.Create(
            source.ContentProjectId,
            source.Kind,
            source.Category,
            source.Minutes,
            source.RecordedAt,
            source.Notes,
            source.CreatedAt,
            source.Id);

    private static CostEntry CreateCostEntry(ProjectManifestCostEntry source) =>
        CostEntry.Create(
            source.ContentProjectId,
            source.Amount,
            source.Currency,
            source.Kind,
            source.Category,
            source.IncurredAt,
            source.Notes,
            source.CreatedAt,
            source.Id);

    private static void Set(object entity, string propertyName, object? value)
    {
        var property = entity.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                $"Restore property '{propertyName}' was not found on '{entity.GetType().Name}'.");
        property.SetValue(entity, value);
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.PostgresException { SqlState: "23505" })
            {
                return true;
            }
        }

        return false;
    }

    private static async Task RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The transaction may already be completed when commit itself failed.
        }
    }
}
