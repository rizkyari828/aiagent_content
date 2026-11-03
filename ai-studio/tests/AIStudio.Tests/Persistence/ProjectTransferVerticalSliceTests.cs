using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIStudio.Application.Approvals;
using AIStudio.Application.Transfer;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Approvals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Infrastructure.Transfer;
using AIStudio.Tests.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class ProjectTransferVerticalSliceTests
{
    [Fact]
    public async Task ExportDeleteImport_RestoresRowsFilesAndFinalApprovalOnCleanDatabase()
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
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        using var fixture = TransferTestStore.Create();
        WriteFiles(fixture, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var projectId = snapshot.Project.Id;

        try
        {
            await SeedAsync(options, snapshot, cancellationToken);

            byte[] package;
            await using (var exportContext = new ApplicationDbContext(options))
            {
                var export = new ProjectExportWorkflow(
                    new ProjectTransferRepository(exportContext),
                    fixture.Store,
                    TimeProvider.System);
                var info = await export.ExportAsync(projectId, cancellationToken);
                package = fixture.ReadPackage(info.FileName);
                Assert.True(package.Length > 0);
            }

            await DeleteProjectAndFilesAsync(
                options,
                fixture,
                projectId,
                cancellationToken);
            await using (var empty = new ApplicationDbContext(options))
            {
                Assert.False(await empty.ContentProjects.AnyAsync(
                    project => project.Id == projectId,
                    cancellationToken));
            }

            await using (var importContext = new ApplicationDbContext(options))
            {
                var import = new ProjectImportWorkflow(
                    new ProjectTransferRepository(importContext),
                    fixture.Store);
                var result = await import.ImportAsync(
                    new MemoryStream(package),
                    cancellationToken);

                Assert.Equal(projectId, result.ContentProjectId);
                Assert.Equal(4, result.Files);
                Assert.Equal(1, result.Counts.ReviewedScripts);
                Assert.Equal(3, result.Counts.SourceJobs);
                Assert.Equal(1, result.Counts.SceneAssets);
                Assert.Equal(1, result.Counts.NarrationTracks);
                Assert.Equal(1, result.Counts.SubtitleTracks);
                Assert.Equal(1, result.Counts.FinalApprovals);
                Assert.Equal(1, result.Counts.PublicationRecords);
                Assert.Equal(1, result.Counts.MetricSnapshots);
                Assert.Equal(1, result.Counts.TimeEntries);
                Assert.Equal(1, result.Counts.CostEntries);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var project = await verification.ContentProjects
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.Id == projectId, cancellationToken);
                Assert.Equal(snapshot.Project.Title, project.Title);
                Assert.Equal(snapshot.Project.Brief, project.Brief);
                Assert.Equal(ContentStatus.ReadyToPublish, project.Status);
                AssertClose(snapshot.Project.CreatedAt, project.CreatedAt);
                AssertClose(snapshot.Project.UpdatedAt, project.UpdatedAt);

                var jobs = await verification.Jobs
                    .AsNoTracking()
                    .Where(job => job.ContentProjectId == projectId)
                    .OrderBy(job => job.CreatedAt)
                    .ToListAsync(cancellationToken);
                Assert.Equal(3, jobs.Count);
                Assert.Equal(
                    ["GenerateStoryboard", "RenderVideo", "FinalVideoQa"],
                    jobs.Select(job => job.Type.ToString()).ToArray());
                Assert.All(jobs, job => Assert.Equal(JobStatus.Succeeded, job.Status));
                Assert.NotNull(jobs[1].Result);
                Assert.Null(jobs[1].WorkerId);
                Assert.Null(jobs[1].LeaseExpiresAt);

                var script = await verification.ReviewedScripts
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(snapshot.ReviewedScript!.Id, script.Id);
                Assert.Equal("Approved", script.Status.ToString());
                Assert.Equal(1, script.Revision);
                Assert.NotNull(script.ApprovedAt);
                using (var expected = JsonDocument.Parse(snapshot.ReviewedScript.Content))
                using (var actual = JsonDocument.Parse(script.Content))
                {
                    Assert.True(JsonElement.DeepEquals(
                        expected.RootElement,
                        actual.RootElement));
                }

                var asset = await verification.SceneAssets
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(snapshot.SceneAssets[0].Id, asset.Id);
                Assert.Equal(TransferTestData.Sha256(sceneBytes), asset.ContentHash);
                Assert.Equal(sceneBytes.Length, asset.ByteSize);

                var narration = await verification.NarrationTracks
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(TransferTestData.Sha256(narrationBytes), narration.ContentHash);
                var subtitle = await verification.SubtitleTracks
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(TransferTestData.Sha256(subtitleBytes), subtitle.ContentHash);

                var approval = await verification.FinalApprovals
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(snapshot.FinalApproval!.Id, approval.Id);
                Assert.Equal(1, approval.Revision);
                Assert.Equal("youtube", approval.Destination);
                Assert.Equal(TransferTestData.Sha256(renderBytes), approval.ArtifactContentHash);
                Assert.Equal(FinalApprovalDecision.Approved, approval.Decision);
                Assert.Null(approval.DecisionNote);
                AssertClose(snapshot.FinalApproval.DecidedAt, approval.DecidedAt);
                AssertClose(snapshot.FinalApproval.ApprovedAt, approval.ApprovedAt);

                var publication = await verification.PublicationRecords
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(snapshot.PublicationRecord!.Id, publication.Id);
                Assert.Equal("dQw4w9WgXcQ", publication.VideoId);
                Assert.Equal(approval.Id, publication.FinalApprovalId);

                var metric = await verification.MetricSnapshots
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(120, metric.Views);
                Assert.Equal(publication.Id, metric.PublicationRecordId);

                var time = await verification.TimeEntries
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(45m, time.Minutes);
                var cost = await verification.CostEntries
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.ContentProjectId == projectId, cancellationToken);
                Assert.Equal(12.5m, cost.Amount);
                Assert.Equal("USD", cost.Currency);

                Assert.Equal(renderBytes, File.ReadAllBytes(fixture.AssetPath(TransferTestData.RenderPath)));
                Assert.Equal(sceneBytes, File.ReadAllBytes(fixture.AssetPath(TransferTestData.ScenePath)));
                Assert.Equal(narrationBytes, File.ReadAllBytes(fixture.AssetPath(TransferTestData.NarrationPath)));
                Assert.Equal(subtitleBytes, File.ReadAllBytes(fixture.AssetPath(TransferTestData.SubtitlePath)));

                var approvalWorkflow = new FinalApprovalWorkflow(
                    new FinalApprovalRepository(verification),
                    new ContentProjectReader(factory),
                    new JobReader(factory),
                    new LocalAssetFileStore(
                        Options.Create(new AssetStorageOptions
                        {
                            RootPath = fixture.AssetRoot
                        })),
                    TimeProvider.System);
                var status = await approvalWorkflow.EvaluateAsync(projectId, cancellationToken);
                Assert.NotNull(status);
                Assert.Equal(FinalApprovalState.Approved, status.State);
            }
        }
        finally
        {
            await DeleteProjectAndFilesAsync(
                options,
                fixture,
                projectId,
                cancellationToken);
        }
    }

    [Fact]
    public async Task ExportDeleteImport_RestoresChangesRequestedDecisionNoteAndTimestamp()
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
        const string note = "Subtitles are cut off in scene 3";
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes,
            decision: FinalApprovalDecision.ChangesRequested,
            decisionNote: note);
        using var fixture = TransferTestStore.Create();
        WriteFiles(fixture, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var projectId = snapshot.Project.Id;
        var sourceApproval = snapshot.FinalApproval!;

        try
        {
            await SeedAsync(options, snapshot, cancellationToken);

            byte[] package;
            await using (var exportContext = new ApplicationDbContext(options))
            {
                var export = new ProjectExportWorkflow(
                    new ProjectTransferRepository(exportContext),
                    fixture.Store,
                    TimeProvider.System);
                var info = await export.ExportAsync(projectId, cancellationToken);
                package = fixture.ReadPackage(info.FileName);
            }

            await DeleteProjectAndFilesAsync(
                options,
                fixture,
                projectId,
                cancellationToken);

            await using (var importContext = new ApplicationDbContext(options))
            {
                var import = new ProjectImportWorkflow(
                    new ProjectTransferRepository(importContext),
                    fixture.Store);
                var result = await import.ImportAsync(
                    new MemoryStream(package),
                    cancellationToken);
                Assert.Equal(1, result.Counts.FinalApprovals);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var approval = await verification.FinalApprovals
                    .AsNoTracking()
                    .SingleAsync(
                        candidate => candidate.ContentProjectId == projectId,
                        cancellationToken);
                Assert.Equal(FinalApprovalDecision.ChangesRequested, approval.Decision);
                Assert.Equal(note, approval.DecisionNote);
                Assert.Equal(sourceApproval.Revision, approval.Revision);
                AssertClose(sourceApproval.DecidedAt, approval.DecidedAt);
                AssertClose(sourceApproval.ApprovedAt, approval.ApprovedAt);
                AssertClose(sourceApproval.CreatedAt, approval.CreatedAt);
                AssertClose(sourceApproval.UpdatedAt, approval.UpdatedAt);

                var approvalWorkflow = new FinalApprovalWorkflow(
                    new FinalApprovalRepository(verification),
                    new ContentProjectReader(factory),
                    new JobReader(factory),
                    new LocalAssetFileStore(
                        Options.Create(new AssetStorageOptions
                        {
                            RootPath = fixture.AssetRoot
                        })),
                    TimeProvider.System);
                var status = await approvalWorkflow.EvaluateAsync(
                    projectId,
                    cancellationToken);
                Assert.NotNull(status);
                Assert.Equal(FinalApprovalState.ChangesRequested, status.State);
                Assert.Equal(note, status.Approval!.DecisionNote);
            }
        }
        finally
        {
            await DeleteProjectAndFilesAsync(
                options,
                fixture,
                projectId,
                cancellationToken);
        }
    }

    [Fact]
    public async Task Import_V1PackageWithoutDecision_RestoresApproved()
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
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        using var fixture = TransferTestStore.Create();
        WriteFiles(fixture, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var projectId = snapshot.Project.Id;
        var sourceApproval = snapshot.FinalApproval!;

        try
        {
            await SeedAsync(options, snapshot, cancellationToken);

            byte[] v1Package;
            await using (var exportContext = new ApplicationDbContext(options))
            {
                var export = new ProjectExportWorkflow(
                    new ProjectTransferRepository(exportContext),
                    fixture.Store,
                    TimeProvider.System);
                var info = await export.ExportAsync(projectId, cancellationToken);
                v1Package = RewriteAsSchemaVersion1(
                    fixture.ReadPackage(info.FileName));
            }

            await DeleteProjectAndFilesAsync(
                options,
                fixture,
                projectId,
                cancellationToken);

            await using (var importContext = new ApplicationDbContext(options))
            {
                var import = new ProjectImportWorkflow(
                    new ProjectTransferRepository(importContext),
                    fixture.Store);
                var result = await import.ImportAsync(
                    new MemoryStream(v1Package),
                    cancellationToken);
                Assert.Equal(1, result.Counts.FinalApprovals);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                var approval = await verification.FinalApprovals
                    .AsNoTracking()
                    .SingleAsync(
                        candidate => candidate.ContentProjectId == projectId,
                        cancellationToken);
                Assert.Equal(FinalApprovalDecision.Approved, approval.Decision);
                Assert.Null(approval.DecisionNote);
                AssertClose(sourceApproval.ApprovedAt, approval.DecidedAt);
                AssertClose(sourceApproval.ApprovedAt, approval.ApprovedAt);
            }
        }
        finally
        {
            await DeleteProjectAndFilesAsync(
                options,
                fixture,
                projectId,
                cancellationToken);
        }
    }

    [Fact]
    public async Task Import_IdentifierConflict_RollsBackProjectAndRemovesWrittenFiles()
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
        using var fixture = TransferTestStore.Create();
        var existingProject = ContentProject.Create(
            $"Existing transfer conflict {Guid.NewGuid():N}",
            "Row that owns the colliding scene asset id.",
            now);
        var existingJob = Job.Create(
            existingProject.Id,
            JobType.GenerateStoryboard,
            "existing",
            "{}",
            now,
            maxRetries: 0);
        existingJob.Start("transfer-conflict", now.AddMinutes(5), now.AddMinutes(1));
        existingJob.Succeed("{}", now.AddMinutes(2));
        var existingBytes = new byte[] { 1, 2, 3 };
        var existingAsset = SceneAsset.Create(
            existingProject.Id,
            existingJob.Id,
            0,
            AssetType.Image,
            "existing/scene.bin",
            existingBytes.Length,
            TransferTestData.Sha256(existingBytes),
            AssetOrigin.Local,
            null,
            "test",
            null,
            null,
            now);

        var newProjectId = Guid.NewGuid();
        var newJobId = Guid.NewGuid();
        var newBytes = new byte[] { 9, 8, 7, 6 };
        var manifest = TransferTestData.Manifest(
            newProjectId,
            [("rollback/scene.bin", newBytes)]) with
        {
            SourceJobs =
            [
                new ProjectManifestJob(
                    newJobId,
                    newProjectId,
                    JobType.GenerateStoryboard,
                    JobStatus.Succeeded,
                    "new",
                    0,
                    0,
                    JsonSerializer.Deserialize<JsonElement>("{}"),
                    JsonSerializer.Deserialize<JsonElement>("{}"),
                    now.AddMinutes(-2),
                    now.AddMinutes(-1),
                    now.AddMinutes(-2),
                    now.AddMinutes(-1))
            ],
            SceneAssets =
            [
                new ProjectManifestSceneAsset(
                    existingAsset.Id,
                    newProjectId,
                    newJobId,
                    0,
                    AssetType.Image,
                    "rollback/scene.bin",
                    newBytes.Length,
                    TransferTestData.Sha256(newBytes),
                    AssetOrigin.Local,
                    null,
                    "test",
                    null,
                    null,
                    now)
            ]
        };
        var package = TransferTestData.Build(
            manifest,
            [("rollback/scene.bin", newBytes)]);

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.ContentProjects.Add(existingProject);
                await setup.SaveChangesAsync(cancellationToken);
                setup.Jobs.Add(existingJob);
                await setup.SaveChangesAsync(cancellationToken);
                setup.SceneAssets.Add(existingAsset);
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using (var importContext = new ApplicationDbContext(options))
            {
                var import = new ProjectImportWorkflow(
                    new ProjectTransferRepository(importContext),
                    fixture.Store);
                var error = await Assert.ThrowsAsync<ProjectTransferException>(
                    () => import.ImportAsync(
                        new MemoryStream(package),
                        cancellationToken));
                Assert.Equal("import_project_conflict", error.ErrorCode);
            }

            await using (var verification = new ApplicationDbContext(options))
            {
                Assert.False(await verification.ContentProjects.AnyAsync(
                    project => project.Id == newProjectId,
                    cancellationToken));
                Assert.False(await verification.Jobs.AnyAsync(
                    job => job.ContentProjectId == newProjectId,
                    cancellationToken));
                Assert.Equal(1, await verification.SceneAssets.CountAsync(
                    asset => asset.Id == existingAsset.Id,
                    cancellationToken));
            }

            Assert.False(File.Exists(fixture.AssetPath("rollback/scene.bin")));
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.SceneAssets
                .Where(asset => asset.ContentProjectId == existingProject.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Jobs
                .Where(job => job.ContentProjectId == existingProject.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(project => project.Id == existingProject.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(project => project.Id == newProjectId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private static async Task SeedAsync(
        DbContextOptions<ApplicationDbContext> options,
        ProjectTransferSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var setup = new ApplicationDbContext(options);
        setup.ContentProjects.Add(snapshot.Project);
        await setup.SaveChangesAsync(cancellationToken);

        setup.Jobs.AddRange(snapshot.Jobs);
        await setup.SaveChangesAsync(cancellationToken);

        if (snapshot.ReviewedScript is not null)
        {
            setup.ReviewedScripts.Add(snapshot.ReviewedScript);
        }

        setup.SceneAssets.AddRange(snapshot.SceneAssets);
        if (snapshot.NarrationTrack is not null)
        {
            setup.NarrationTracks.Add(snapshot.NarrationTrack);
        }

        if (snapshot.SubtitleTrack is not null)
        {
            setup.SubtitleTracks.Add(snapshot.SubtitleTrack);
        }

        if (snapshot.FinalApproval is not null)
        {
            setup.FinalApprovals.Add(snapshot.FinalApproval);
        }

        await setup.SaveChangesAsync(cancellationToken);

        if (snapshot.PublicationRecord is not null)
        {
            setup.PublicationRecords.Add(snapshot.PublicationRecord);
            await setup.SaveChangesAsync(cancellationToken);
        }

        setup.MetricSnapshots.AddRange(snapshot.MetricSnapshots);
        setup.TimeEntries.AddRange(snapshot.TimeEntries);
        setup.CostEntries.AddRange(snapshot.CostEntries);
        await setup.SaveChangesAsync(cancellationToken);
    }

    private static async Task DeleteProjectAndFilesAsync(
        DbContextOptions<ApplicationDbContext> options,
        TransferTestStore fixture,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using (var cleanup = new ApplicationDbContext(options))
        {
            await cleanup.MetricSnapshots
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.TimeEntries
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.CostEntries
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.PublicationRecords
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.FinalApprovals
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.SceneAssets
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.NarrationTracks
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.SubtitleTracks
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ReviewedScripts
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Jobs
                .Where(entity => entity.ContentProjectId == projectId)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(entity => entity.Id == projectId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        foreach (var relativePath in new[]
        {
            TransferTestData.RenderPath,
            TransferTestData.ScenePath,
            TransferTestData.NarrationPath,
            TransferTestData.SubtitlePath
        })
        {
            var absolute = fixture.AssetPath(relativePath);
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
    }

    private static void WriteFiles(
        TransferTestStore fixture,
        byte[] renderBytes,
        byte[] sceneBytes,
        byte[] narrationBytes,
        byte[] subtitleBytes)
    {
        fixture.WriteAssetFile(TransferTestData.RenderPath, renderBytes);
        fixture.WriteAssetFile(TransferTestData.ScenePath, sceneBytes);
        fixture.WriteAssetFile(TransferTestData.NarrationPath, narrationBytes);
        fixture.WriteAssetFile(TransferTestData.SubtitlePath, subtitleBytes);
    }

    /// <summary>
    /// Rewrites one exported v2 package into the pre-decision v1 shape: schemaVersion 1
    /// and a final approval without the three decision members. Everything else (files,
    /// hashes, rows) stays byte-identical.
    /// </summary>
    private static byte[] RewriteAsSchemaVersion1(byte[] package)
    {
        using var source = new MemoryStream(package);
        using var sourceArchive = new ZipArchive(source, ZipArchiveMode.Read);
        using var target = new MemoryStream();
        using (var targetArchive = new ZipArchive(
            target,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach (var entry in sourceArchive.Entries)
            {
                if (string.Equals(
                        entry.FullName,
                        ProjectPackageSerializer.ManifestEntryName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var copy = targetArchive.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var output = copy.Open();
                input.CopyTo(output);
            }

            var manifestEntry = sourceArchive.GetEntry(
                ProjectPackageSerializer.ManifestEntryName)
                ?? throw new InvalidOperationException("The package manifest is missing.");
            JsonObject manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = JsonNode.Parse(manifestStream)?.AsObject()
                    ?? throw new InvalidOperationException("The package manifest is empty.");
            }

            manifest["schemaVersion"] = ProjectPackageSerializer.MinimumSchemaVersion;
            var approval = manifest["finalApproval"]?.AsObject()
                ?? throw new InvalidOperationException(
                    "The package does not contain a final approval.");
            approval.Remove("decision");
            approval.Remove("decisionNote");
            approval.Remove("decidedAt");

            var manifestCopy = targetArchive.CreateEntry(
                ProjectPackageSerializer.ManifestEntryName);
            using var manifestOutput = manifestCopy.Open();
            using var writer = new StreamWriter(manifestOutput);
            writer.Write(manifest.ToJsonString());
        }

        return target.ToArray();
    }

    private static void AssertClose(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True(
            (expected - actual).Duration() < TimeSpan.FromMilliseconds(1),
            $"Expected '{expected:O}' to equal '{actual:O}' within PostgreSQL timestamp precision.");

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
