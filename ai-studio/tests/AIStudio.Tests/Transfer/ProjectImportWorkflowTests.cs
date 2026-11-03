using Xunit;
using System.Security.Cryptography;
using AIStudio.Application.Transfer;
using AIStudio.Domain.Approvals;

namespace AIStudio.Tests.Transfer;

public sealed class ProjectImportWorkflowTests
{
    [Fact]
    public async Task Import_WritesFilesAndCallsRepositoryWithManifest()
    {
        using var fixture = TransferTestStore.Create();
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var manifest = TransferTestData.Manifest(
            projectId,
            [("scenes/one.bin", bytes)]);
        var package = TransferTestData.Build(manifest, [("scenes/one.bin", bytes)]);
        var repository = new FakeProjectTransferRepository();
        var workflow = new ProjectImportWorkflow(repository, fixture.Store);

        var result = await workflow.ImportAsync(
            new MemoryStream(package),
            TestContext.Current.CancellationToken);

        Assert.Equal(projectId, result.ContentProjectId);
        Assert.Equal(1, result.Files);
        Assert.Equal(bytes, File.ReadAllBytes(fixture.AssetPath("scenes/one.bin")));
        Assert.NotNull(repository.Imported);
        Assert.Equal(projectId, repository.Imported.Project.Id);
    }

    [Fact]
    public async Task Import_V1PackageWithoutDecision_DefaultsToApproved()
    {
        using var fixture = TransferTestStore.Create();
        var manifest = TransferTestData.ManifestWithApproval(
            ProjectPackageSerializer.MinimumSchemaVersion,
            decision: null,
            withDecidedAt: false);
        var package = TransferTestData.Build(
            manifest,
            [(TransferTestData.RenderPath, TransferTestData.RenderBytes)]);
        var repository = new FakeProjectTransferRepository();
        var workflow = new ProjectImportWorkflow(repository, fixture.Store);

        await workflow.ImportAsync(
            new MemoryStream(package),
            TestContext.Current.CancellationToken);

        Assert.NotNull(repository.Imported);
        var approval = repository.Imported!.FinalApproval;
        Assert.NotNull(approval);
        Assert.Equal(FinalApprovalDecision.Approved, approval!.ResolveDecision());
        Assert.Equal(approval.ApprovedAt, approval.ResolveDecidedAt());
        Assert.Equal(
            TransferTestData.RenderBytes,
            File.ReadAllBytes(fixture.AssetPath(TransferTestData.RenderPath)));
    }

    [Fact]
    public async Task Import_UnknownDecision_IsRejectedBeforeWritingFiles()
    {
        using var fixture = TransferTestStore.Create();
        var manifest = TransferTestData.ManifestWithApproval(
            ProjectPackageSerializer.CurrentSchemaVersion,
            "rejected");
        var package = TransferTestData.Build(
            manifest,
            [(TransferTestData.RenderPath, TransferTestData.RenderBytes)]);
        var repository = new FakeProjectTransferRepository();
        var workflow = new ProjectImportWorkflow(repository, fixture.Store);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ImportAsync(
                new MemoryStream(package),
                TestContext.Current.CancellationToken));

        Assert.Equal("import_package_invalid", error.ErrorCode);
        Assert.Null(repository.Imported);
        Assert.False(File.Exists(fixture.AssetPath(TransferTestData.RenderPath)));
    }

    [Fact]
    public async Task Import_ExistingProject_ConflictsBeforeWritingFiles()
    {
        using var fixture = TransferTestStore.Create();
        var projectId = Guid.NewGuid();
        var manifest = TransferTestData.Manifest(
            projectId,
            [("scenes/one.bin", [1, 2, 3])]);
        var package = TransferTestData.Build(manifest, [("scenes/one.bin", [1, 2, 3])]);
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository { Exists = true },
            fixture.Store);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ImportAsync(
                new MemoryStream(package),
                TestContext.Current.CancellationToken));

        Assert.Equal("import_project_conflict", error.ErrorCode);
        Assert.False(File.Exists(fixture.AssetPath("scenes/one.bin")));
    }

    [Fact]
    public async Task Import_ExistingFile_IsNeverOverwritten()
    {
        using var fixture = TransferTestStore.Create();
        var projectId = Guid.NewGuid();
        var original = new byte[] { 9, 9, 9 };
        fixture.WriteAssetFile("scenes/one.bin", original);
        var manifest = TransferTestData.Manifest(
            projectId,
            [("scenes/one.bin", [1, 2, 3])]);
        var package = TransferTestData.Build(manifest, [("scenes/one.bin", [1, 2, 3])]);
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ImportAsync(
                new MemoryStream(package),
                TestContext.Current.CancellationToken));

        Assert.Equal("import_path_conflict", error.ErrorCode);
        Assert.Equal(original, File.ReadAllBytes(fixture.AssetPath("scenes/one.bin")));
    }

    [Fact]
    public async Task Import_RepositoryFailure_RemovesFilesWrittenByThisImport()
    {
        using var fixture = TransferTestStore.Create();
        var projectId = Guid.NewGuid();
        var first = new byte[] { 1, 2, 3 };
        var second = new byte[] { 4, 5, 6 };
        var manifest = TransferTestData.Manifest(
            projectId,
            [("scenes/a.bin", first), ("scenes/b.bin", second)]);
        var package = TransferTestData.Build(
            manifest,
            [("scenes/a.bin", first), ("scenes/b.bin", second)]);
        var repository = new FakeProjectTransferRepository
        {
            ImportFailure = _ => new InvalidOperationException("database unavailable")
        };
        var workflow = new ProjectImportWorkflow(repository, fixture.Store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.ImportAsync(
                new MemoryStream(package),
                TestContext.Current.CancellationToken));

        Assert.False(File.Exists(fixture.AssetPath("scenes/a.bin")));
        Assert.False(File.Exists(fixture.AssetPath("scenes/b.bin")));
    }

    [Fact]
    public async Task Import_InvalidZip_IsRejected()
    {
        using var fixture = TransferTestStore.Create();
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ImportAsync(
                new MemoryStream([1, 2, 3, 4]),
                TestContext.Current.CancellationToken));

        Assert.Equal("import_package_invalid", error.ErrorCode);
    }

    [Fact]
    public async Task Import_OversizedPackage_IsRejectedBeforeReading()
    {
        using var fixture = TransferTestStore.Create(
            new ProjectTransferLimits(
                MaxFileBytes: 10,
                MaxPackageBytes: 50,
                MaxManifestBytes: 1024,
                MaxFileCount: 10));
        var bytes = RandomNumberGenerator.GetBytes(200);
        var manifest = TransferTestData.Manifest(
            Guid.NewGuid(),
            [("scenes/one.bin", bytes)]);
        var package = TransferTestData.Build(manifest, [("scenes/one.bin", bytes)]);
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store);

        var error = await Assert.ThrowsAsync<ProjectTransferException>(
            () => workflow.ImportAsync(
                new MemoryStream(package),
                TestContext.Current.CancellationToken));

        Assert.Equal("import_too_large", error.ErrorCode);
    }
}
