using Xunit;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Transfer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AIStudio.Tests.Transfer;

public sealed class ProjectTransferApiContractTests
{
    [Fact]
    public async Task Import_ReturnsCreatedSummaryAndStableFields()
    {
        using var fixture = TransferTestStore.Create();
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var package = TransferTestData.Build(
            TransferTestData.Manifest(projectId, [("scenes/a.bin", bytes)]),
            [("scenes/a.bin", bytes)]);
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store);
        using var stream = new MemoryStream(package);
        var file = new FormFile(stream, 0, package.Length, "file", "project.aistudio.zip");

        var result = await ProjectTransferEndpoints.ImportAsync(
            file,
            workflow,
            TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<ProjectImportResponse>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal(projectId, created.Value.ContentProjectId);
        Assert.Equal(1, created.Value.Files);
        Assert.Equal(0, created.Value.SourceJobs);
        Assert.StartsWith(
            $"/api/content-projects/{projectId}",
            created.Location,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_Conflict_Returns409WithStableCode()
    {
        using var fixture = TransferTestStore.Create();
        var bytes = new byte[] { 1, 2, 3 };
        var package = TransferTestData.Build(
            TransferTestData.Manifest(Guid.NewGuid(), [("scenes/a.bin", bytes)]),
            [("scenes/a.bin", bytes)]);
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository { Exists = true },
            fixture.Store);
        using var stream = new MemoryStream(package);
        var file = new FormFile(stream, 0, package.Length, "file", "project.aistudio.zip");

        var result = await ProjectTransferEndpoints.ImportAsync(
            file,
            workflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("import_project_conflict", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Import_MissingFile_ReturnsValidationProblem()
    {
        using var fixture = TransferTestStore.Create();
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store);

        var result = await ProjectTransferEndpoints.ImportAsync(
            null,
            workflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal("import_package_invalid", problem.ProblemDetails.Extensions["code"]);
        var validation = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.Contains("file", validation.Errors.Keys);
    }

    [Fact]
    public async Task Export_ReturnsPackageMetadata()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);

        var result = await ProjectTransferEndpoints.ExportAsync(
            snapshot.Project.Id.ToString(),
            workflow,
            TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<ProjectExportResponse>>(result);
        Assert.NotNull(created.Value);
        Assert.EndsWith(".aistudio.zip", created.Value.FileName, StringComparison.Ordinal);
        Assert.True(created.Value.ByteSize > 0);
        Assert.NotNull(created.Value.Sha256);
        Assert.Equal(64, created.Value.Sha256.Length);
    }

    [Fact]
    public async Task Export_MalformedIdentifier_ReturnsValidationProblem()
    {
        using var fixture = TransferTestStore.Create();
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store,
            TimeProvider.System);

        var result = await ProjectTransferEndpoints.ExportAsync(
            "not-a-guid",
            workflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal("export_invalid_request", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Export_UnknownProject_Returns404WithStableCode()
    {
        using var fixture = TransferTestStore.Create();
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store,
            TimeProvider.System);

        var result = await ProjectTransferEndpoints.ExportAsync(
            Guid.NewGuid().ToString(),
            workflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
        Assert.Equal("export_project_not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task List_ReturnsPackagesForProject()
    {
        using var fixture = TransferTestStore.Create();
        var snapshot = TransferTestData.Snapshot(
            out var renderBytes,
            out var sceneBytes,
            out var narrationBytes,
            out var subtitleBytes);
        WriteFiles(fixture, renderBytes, sceneBytes, narrationBytes, subtitleBytes);
        var workflow = new ProjectExportWorkflow(
            new FakeProjectTransferRepository { Snapshot = snapshot },
            fixture.Store,
            TimeProvider.System);
        await workflow.ExportAsync(
            snapshot.Project.Id,
            TestContext.Current.CancellationToken);

        var result = await ProjectTransferEndpoints.ListAsync(
            snapshot.Project.Id.ToString(),
            workflow,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<ProjectExportResponse[]>>(result);
        var package = Assert.Single(ok.Value!);
        Assert.Null(package.Sha256);
        Assert.True(package.ByteSize > 0);
    }

    [Fact]
    public async Task Export_UnsafeZipEntry_IsRejectedAsInvalidPackage()
    {
        using var fixture = TransferTestStore.Create();
        var workflow = new ProjectImportWorkflow(
            new FakeProjectTransferRepository(),
            fixture.Store);
        var package = TransferTestData.BuildRaw(archive =>
        {
            {
                var entry = archive.CreateEntry("files/../escape.bin");
                using var stream = entry.Open();
                stream.WriteByte(1);
            }

            var manifest = archive.CreateEntry(
                ProjectPackageSerializer.ManifestEntryName);
            using var manifestStream = manifest.Open();
            manifestStream.Write(
                ProjectPackageSerializer.Serialize(TransferTestData.Manifest()));
        });

        using var stream = new MemoryStream(package);
        var file = new FormFile(stream, 0, package.Length, "file", "project.aistudio.zip");
        var result = await ProjectTransferEndpoints.ImportAsync(
            file,
            workflow,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal("import_path_invalid", problem.ProblemDetails.Extensions["code"]);
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
}
