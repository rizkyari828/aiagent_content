using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Approvals;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Approvals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class FinalApprovalVerticalSliceTests
{
    [Fact]
    public async Task ApproveAndPublish_PersistAndReloadThroughPostgreSql()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ConnectionStrings__DefaultConnection before running integration tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);
        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"Final approval integration {Guid.NewGuid():N}",
            "Disposable approval/publication vertical-slice row.",
            now);
        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-approval-slice-tests",
            Guid.NewGuid().ToString("N"));
        var relativePath = $"renders/{project.Id:N}/video.mp4";
        var artifactPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        var artifactBytes = Encoding.UTF8.GetBytes($"approval-slice-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(artifactPath, artifactBytes, cancellationToken);
        var contentHash = Convert
            .ToHexString(SHA256.HashData(artifactBytes))
            .ToLowerInvariant();

        var renderResult = new RenderVideoResult(
            relativePath,
            contentHash,
            artifactBytes.Length,
            12,
            1280,
            720,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            contentHash);
        var qaResult = new FinalVideoQaResult(
            relativePath,
            contentHash,
            12,
            1280,
            720,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: false);

        var renderJob = Job.Create(
            project.Id,
            JobType.RenderVideo,
            "approval-render",
            "{}",
            now,
            maxRetries: 0);
        renderJob.Start("approval-slice", now.AddMinutes(2), now.AddSeconds(1));
        renderJob.Succeed(renderResult.Serialize(), now.AddSeconds(2));

        var qaPayload = JsonSerializer.Serialize(
            new FinalVideoQaJobPayload(project.Id, renderJob.Id),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var qaJob = Job.Create(
            project.Id,
            JobType.FinalVideoQa,
            "approval-qa",
            qaPayload,
            now.AddSeconds(3),
            maxRetries: 0);
        qaJob.Start("approval-slice", now.AddMinutes(2), now.AddSeconds(4));
        qaJob.Succeed(qaResult.Serialize(), now.AddSeconds(5));

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.ContentProjects.Add(project);
                setup.Jobs.Add(renderJob);
                setup.Jobs.Add(qaJob);
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using (var context = new ApplicationDbContext(options))
            {
                var workflow = CreateApprovalWorkflow(context, factory, root);

                var result = await workflow.ApproveAsync(
                    project.Id,
                    renderJob.Id,
                    "youtube",
                    "manual upload prepared",
                    "operator-1",
                    cancellationToken);

                Assert.NotNull(result);
                Assert.True(result.Created);
                Assert.Equal(renderJob.Id, result.Approval.SourceRenderJobId);
                Assert.Equal(contentHash, result.Approval.ArtifactContentHash);
                Assert.Equal(artifactBytes.Length, result.Approval.ArtifactByteSize);
                Assert.Equal(1, result.Approval.Revision);

                var repeated = await workflow.ApproveAsync(
                    project.Id,
                    renderJob.Id,
                    "youtube",
                    "manual upload prepared",
                    "operator-1",
                    cancellationToken);
                Assert.NotNull(repeated);
                Assert.False(repeated.Created);
                Assert.Equal(1, repeated.Approval.Revision);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var persisted = await verification.FinalApprovals
                    .AsNoTracking()
                    .SingleAsync(
                        approval => approval.ContentProjectId == project.Id,
                        cancellationToken);
                Assert.Equal(renderJob.Id, persisted.SourceRenderJobId);
                Assert.Equal("youtube", persisted.Destination);
                Assert.Equal(1, persisted.Revision);
                Assert.Equal(1, await verification.FinalApprovals.CountAsync(
                    approval => approval.ContentProjectId == project.Id,
                    cancellationToken));
            }

            await using (var context = new ApplicationDbContext(options))
            {
                var approvalWorkflow = CreateApprovalWorkflow(context, factory, root);
                var publicationWorkflow = new PublicationWorkflow(
                    new PublicationRepository(context),
                    new ContentProjectReader(factory),
                    approvalWorkflow,
                    TimeProvider.System);

                var publication = await publicationWorkflow.CreateAsync(
                    project.Id,
                    "youtube",
                    "https://youtu.be/dQw4w9WgXcQ",
                    "dQw4w9WgXcQ",
                    now.AddHours(-2),
                    "first manual upload",
                    cancellationToken);

                Assert.NotNull(publication);
                Assert.True(publication.Created);
                Assert.Equal(contentHash, publication.Publication.ArtifactContentHash);

                var repeated = await publicationWorkflow.CreateAsync(
                    project.Id,
                    "youtube",
                    "https://youtu.be/dQw4w9WgXcQ",
                    "dQw4w9WgXcQ",
                    now.AddHours(-2),
                    "first manual upload",
                    cancellationToken);
                Assert.NotNull(repeated);
                Assert.False(repeated.Created);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var persisted = await verification.PublicationRecords
                    .AsNoTracking()
                    .SingleAsync(
                        record => record.ContentProjectId == project.Id,
                        cancellationToken);
                Assert.Equal("youtube", persisted.Platform);
                Assert.Equal("dQw4w9WgXcQ", persisted.VideoId);
                Assert.Equal(contentHash, persisted.ArtifactContentHash);
                Assert.Equal(1, persisted.FinalApprovalRevision);
                Assert.Equal(1, await verification.PublicationRecords.CountAsync(
                    record => record.ContentProjectId == project.Id,
                    cancellationToken));
            }

            await File.WriteAllBytesAsync(
                artifactPath,
                Encoding.UTF8.GetBytes("tampered-artifact"),
                cancellationToken);

            await using (var context = new ApplicationDbContext(options))
            {
                var workflow = CreateApprovalWorkflow(context, factory, root);
                var status = await workflow.EvaluateAsync(project.Id, cancellationToken);

                Assert.NotNull(status);
                Assert.Equal(FinalApprovalState.Invalid, status.State);
                Assert.Equal("final_approval_artifact_mismatch", status.Reason);
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.PublicationRecords
                .Where(record => record.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.FinalApprovals
                .Where(approval => approval.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Jobs
                .Where(job => job.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RequestChanges_RoundTripsDecisionNoteAndDecidedAt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ConnectionStrings__DefaultConnection before running integration tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);
        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"Review decision integration {Guid.NewGuid():N}",
            "Disposable changes_requested vertical-slice row.",
            now);
        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-review-decision-slice-tests",
            Guid.NewGuid().ToString("N"));
        var relativePath = $"renders/{project.Id:N}/video.mp4";
        var artifactPath = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        var artifactBytes = Encoding.UTF8.GetBytes($"review-decision-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(artifactPath, artifactBytes, cancellationToken);
        var contentHash = Convert
            .ToHexString(SHA256.HashData(artifactBytes))
            .ToLowerInvariant();
        var renderResult = new RenderVideoResult(
            relativePath,
            contentHash,
            artifactBytes.Length,
            12,
            1280,
            720,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            contentHash);
        var renderJob = Job.Create(
            project.Id,
            JobType.RenderVideo,
            "review-decision-render",
            "{}",
            now,
            maxRetries: 0);
        renderJob.Start("review-decision", now.AddMinutes(2), now.AddSeconds(1));
        renderJob.Succeed(renderResult.Serialize(), now.AddSeconds(2));

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.ContentProjects.Add(project);
                setup.Jobs.Add(renderJob);
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using (var context = new ApplicationDbContext(options))
            {
                var workflow = CreateApprovalWorkflow(context, factory, root);

                var result = await workflow.RequestChangesAsync(
                    project.Id,
                    renderJob.Id,
                    "youtube",
                    "  Subtitles are cut off in scene 3  ",
                    "operator-1",
                    cancellationToken);

                Assert.NotNull(result);
                Assert.True(result.Created);
                Assert.Equal(
                    FinalApprovalDecision.ChangesRequested,
                    result.Approval.Decision);
                Assert.Equal(
                    "Subtitles are cut off in scene 3",
                    result.Approval.DecisionNote);
                Assert.Equal(1, result.Approval.Revision);

                var status = await workflow.EvaluateAsync(project.Id, cancellationToken);
                Assert.NotNull(status);
                Assert.Equal(FinalApprovalState.ChangesRequested, status.State);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var persisted = await verification.FinalApprovals
                    .AsNoTracking()
                    .SingleAsync(
                        approval => approval.ContentProjectId == project.Id,
                        cancellationToken);
                Assert.Equal(
                    FinalApprovalDecision.ChangesRequested,
                    persisted.Decision);
                Assert.Equal("Subtitles are cut off in scene 3", persisted.DecisionNote);
                Assert.Equal(1, persisted.Revision);
                Assert.NotEqual(default, persisted.DecidedAt);
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.FinalApprovals
                .Where(approval => approval.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Jobs
                .Where(job => job.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LegacyApprovalRowWithoutDecision_DefaultsToApproved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ConnectionStrings__DefaultConnection before running integration tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"Legacy approval default {Guid.NewGuid():N}",
            "Disposable pre-review-decision row.",
            now);
        var renderJob = Job.Create(
            project.Id,
            JobType.RenderVideo,
            "legacy-approval-render",
            "{}",
            now,
            maxRetries: 0);
        var approvalId = Guid.NewGuid();
        const string artifactPath = "renders/legacy/video.mp4";
        var contentHash = new string('a', 64);

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.ContentProjects.Add(project);
                setup.Jobs.Add(renderJob);
                await setup.SaveChangesAsync(cancellationToken);

                // Simulates a row written before the decision columns existed: the
                // decision column is omitted, so PostgreSQL applies its 'approved'
                // default, and decided_at is supplied because the new column is
                // required (the migration backfills legacy rows from approved_at).
                await setup.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO final_approvals
                        (id, content_project_id, source_render_job_id, artifact_path,
                         artifact_content_hash, artifact_byte_size, destination, notes,
                         approver_label, revision, approved_at, created_at, updated_at,
                         decided_at)
                    VALUES
                        ({approvalId}, {project.Id}, {renderJob.Id}, {artifactPath},
                         {contentHash}, {4096L}, {"youtube"}, {(string?)null},
                         {(string?)null}, {1}, {now}, {now}, {now}, {now})
                    """,
                    cancellationToken);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var persisted = await verification.FinalApprovals
                    .AsNoTracking()
                    .SingleAsync(
                        approval => approval.Id == approvalId,
                        cancellationToken);

                Assert.Equal(
                    FinalApprovalDecision.Approved,
                    persisted.Decision);
                Assert.Null(persisted.DecisionNote);
                Assert.True(
                    (persisted.DecidedAt - now).Duration()
                    < TimeSpan.FromSeconds(1));
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.FinalApprovals
                .Where(approval => approval.Id == approvalId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Jobs
                .Where(job => job.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static FinalApprovalWorkflow CreateApprovalWorkflow(
        ApplicationDbContext context,
        IDbContextFactory<ApplicationDbContext> factory,
        string root) =>
        new(
            new FinalApprovalRepository(context),
            new ContentProjectReader(factory),
            new JobReader(factory),
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            TimeProvider.System);

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
