using Xunit;
using System.IO.Compression;
using AIStudio.Application.Transfer;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Transfer;

public sealed class ProjectExportWorkflowTests
{
    [Fact]
    public async Task Export_WritesManifestAndEveryReferencedFile()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var repository = new FakeProjectTransferRepository { Snapshot = snapshot };
        var workflow = new ProjectExportWorkflow(
            repository,
            fixture.Store,
            TimeProvider.System);

        var package = await workflow.ExportAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);

        Assert.True(package.ByteSize > 0);
        Assert.NotNull(package.Sha256);
        Assert.StartsWith($"{snapshot.Project.Id:N}-", package.FileName);

        var bytes = fixture.ReadPackage(package.FileName);
        Assert.Equal(package.ByteSize, bytes.Length);
        Assert.Equal(package.Sha256, TransferTestData.Sha256(bytes));

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);
        var manifest = ProjectPackageSerializer.Deserialize(
            ReadEntry(manifestEntry));

        Assert.Equal(snapshot.Project.Id, manifest.Project.Id);
        Assert.Equal(snapshot.Project.Title, manifest.Project.Title);
        Assert.Equal(ContentStatus.ReadyToPublish, manifest.Project.Status);
        Assert.NotNull(manifest.ReviewedScript);
        Assert.Equal(1, manifest.ReviewedScript.Revision);
        Assert.Equal(3, manifest.SourceJobs.Count);
        Assert.Equal(JobType.RenderVideo, manifest.SourceJobs[1].Type);
        Assert.Single(manifest.SceneAssets);
        Assert.NotNull(manifest.NarrationTrack);
        Assert.NotNull(manifest.SubtitleTrack);
        Assert.Equal(
            ProjectPackageSerializer.CurrentSchemaVersion,
            manifest.SchemaVersion);
        Assert.NotNull(manifest.FinalApproval);
        Assert.Equal("youtube", manifest.FinalApproval.Destination);
        Assert.Equal(FinalApprovalDecisionTokens.Approved, manifest.FinalApproval.Decision);
        Assert.Null(manifest.FinalApproval.DecisionNote);
        Assert.Equal(
            snapshot.FinalApproval!.DecidedAt,
            manifest.FinalApproval.DecidedAt);
        Assert.NotNull(manifest.PublicationRecord);
        Assert.Equal("dQw4w9WgXcQ", manifest.PublicationRecord.VideoId);
        Assert.Single(manifest.MetricSnapshots);
        Assert.Single(manifest.TimeEntries);
        Assert.Single(manifest.CostEntries);
        Assert.Equal(4, manifest.Files.Count);

        foreach (var file in manifest.Files)
        {
            var entry = archive.GetEntry($"files/{file.Path}");
            Assert.NotNull(entry);
            var content = ReadEntry(entry);
            Assert.Equal(file.ByteSize, content.Length);
            Assert.Equal(file.Sha256, TransferTestData.Sha256(content));
            Assert.True(
                archive.Entries.Count(candidate =>
                    candidate.FullName == $"files/{file.Path}") == 1);
        }

        var listed = await workflow.ListAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);
        var listedPackage = Assert.Single(listed);
        Assert.Equal(package.FileName, listedPackage.FileName);
        Assert.Equal(package.ByteSize, listedPackage.ByteSize);
        Assert.Null(listedPackage.Sha256);
    }

    [Fact]
    public async Task Export_ChangesRequestedApproval_WritesDecisionNoteAndTimestamp()
    {
        using var fixture = TransferTestStore.Create();
        const string note = "Subtitles are cut off in scene 3";
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes,
            decision: FinalApprovalDecision.ChangesRequested,
            decisionNote: note);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var package = await workflow.ExportAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);

        using var stream = new MemoryStream(fixture.ReadPackage(package.FileName));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifest = ProjectPackageSerializer.Deserialize(
            ReadEntry(archive.GetEntry("manifest.json")!));

        Assert.Equal(
            FinalApprovalDecisionTokens.ChangesRequested,
            manifest.FinalApproval!.Decision);
        Assert.Equal(note, manifest.FinalApproval.DecisionNote);
        Assert.Equal(
            snapshot.FinalApproval!.DecidedAt,
            manifest.FinalApproval.DecidedAt);
        Assert.Equal(
            FinalApprovalDecision.ChangesRequested,
            manifest.FinalApproval.ResolveDecision());
    }

    [Fact]
    public async Task Export_MissingArtifact_FailsWithStableCodeAndLeavesNoPackage()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        File.Delete(fixture.AssetPath(TransferTestData.ScenePath));
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                snapshot.Project.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("export_artifact_missing", error.ErrorCode);
        Assert.Empty(fixture.Store.ListPackages(snapshot.Project.Id));
    }

    [Fact]
    public async Task Export_PathOutsideRoot_FailsWithStableCode()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var unsafeAsset = AIStudio.Domain.Assets.SceneAsset.Create(
            snapshot.Project.Id,
            snapshot.Jobs[0].Id,
            1,
            AIStudio.Domain.Assets.AssetType.Image,
            "../outside.png",
            sceneBytes.Length,
            TransferTestData.Sha256(sceneBytes),
            AIStudio.Domain.Assets.AssetOrigin.Local,
            null,
            "test",
            null,
            null,
            DateTimeOffset.UtcNow);
        var unsafeSnapshot = snapshot with
        {
            SceneAssets = [.. snapshot.SceneAssets, unsafeAsset]
        };
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = unsafeSnapshot },
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                unsafeSnapshot.Project.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("export_path_invalid", error.ErrorCode);
    }

    [Fact]
    public async Task Export_ChangedFile_FailsIntegrityCheck()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        fixture.WriteAssetFile(TransferTestData.ScenePath, [9, 9, 9, 9]);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                snapshot.Project.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("export_integrity_failed", error.ErrorCode);
    }

    [Fact]
    public async Task Export_NonTerminalJob_IsRejected()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var queued = Job.Create(
            snapshot.Project.Id,
            JobType.GenerateIdea,
            "queued",
            "{}",
            DateTimeOffset.UtcNow);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository
            {
                Snapshot = snapshot with { Jobs = [.. snapshot.Jobs, queued] }
            },
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                snapshot.Project.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("export_project_busy", error.ErrorCode);
    }


    [Fact]
    public async Task Export_FileOverLimit_FailsWithStableCode()
    {
        using var fixture = TransferTestStore.Create(
            new ProjectTransferLimits(
                MaxFileBytes: 2,
                MaxPackageBytes: 8,
                MaxManifestBytes: 1024 * 1024,
                MaxFileCount: 10));
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                snapshot.Project.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("export_file_too_large", error.ErrorCode);
    }

    [Fact]
    public async Task Export_PackageOverTotalLimit_FailsWithStableCode()
    {
        using var fixture = TransferTestStore.Create(
            new ProjectTransferLimits(
                MaxFileBytes: 4,
                MaxPackageBytes: 8,
                MaxManifestBytes: 1024 * 1024,
                MaxFileCount: 10));
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                snapshot.Project.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("export_package_too_large", error.ErrorCode);
    }

    [Fact]
    public async Task Export_TransferRootSymlink_IsRejected()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var outside = Path.Combine(
            Path.GetTempPath(),
            "aistudio-transfer-outside",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(fixture.ExportRoot, outside);
        try
        {
            var workflow = new ProjectExportWorkflow(
                new FakeProjectTransferRepository { Snapshot = snapshot },
                fixture.Store,
                TimeProvider.System);

            var error = await Assert.ThrowsAsync<ProjectTransferException>(
                () => workflow.ExportAsync(
                    snapshot.Project.Id,
                    TestContext.Current.CancellationToken));

            Assert.Equal("export_path_invalid", error.ErrorCode);
            Assert.Empty(Directory.EnumerateFiles(outside));
        }
        finally
        {
            Directory.Delete(fixture.ExportRoot);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Export_UnknownProject_FailsWithNotFoundCode()
    {
        using var fixture = TransferTestStore.Create();
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ExportAsync(
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));

        Assert.Equal("export_project_not_found", error.ErrorCode);
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, snapshot, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var first = await workflow.ExportAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        var second = await workflow.ExportAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);

        var listed = await workflow.ListAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, listed.Count);
        Assert.Equal(second.FileName, listed[0].FileName);
        Assert.Equal(first.FileName, listed[1].FileName);
        Assert.True(listed[0].CreatedAtUtc >= listed[1].CreatedAtUtc);
    }

    private static void WriteFiles(
        TransferTestStore fixture,
        ProjectTransferSnapshot snapshot,
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

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
