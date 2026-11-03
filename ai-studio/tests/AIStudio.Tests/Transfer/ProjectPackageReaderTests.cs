using Xunit;
using System.IO.Compression;
using AIStudio.Application.Transfer;

namespace AIStudio.Tests.Transfer;

public sealed class ProjectPackageReaderTests
{
    [Fact]
    public async Task Read_ValidPackage_ReturnsVerifiedManifestAndFiles()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var manifest = TransferTestData.Manifest(projectId, [("scenes/one.bin", bytes)]);
        var package = TransferTestData.Build(manifest, [("scenes/one.bin", bytes)]);

        var contents = await ReadAsync(package);

        Assert.Equal(projectId, contents.Manifest.Project.Id);
        var file = Assert.Single(contents.Files);
        Assert.Equal("scenes/one.bin", file.Path);
        Assert.Equal(TransferTestData.Sha256(bytes), file.Sha256);
        Assert.Equal(4, file.ByteSize);
    }

    [Fact]
    public async Task Read_UnsupportedSchemaVersion_IsRejected()
    {
        var manifest = TransferTestData.Manifest(
            schemaVersion: ProjectPackageSerializer.CurrentSchemaVersion + 1);
        var package = TransferTestData.Build(manifest);

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_schema_unsupported", error.ErrorCode);
    }

    [Theory]
    [InlineData("../escape.bin")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../../escape.bin")]
    [InlineData("a\\escape.bin")]
    [InlineData("./escape.bin")]
    [InlineData("")]
    public async Task Read_UnsafeManifestPath_IsRejected(string path)
    {
        var manifest = TransferTestData.Manifest(
            files: [("safe.bin", [1])]) with
        {
            Files = [new ProjectManifestFile(path, new string('a', 64), 1)]
        };
        var package = TransferTestData.Build(manifest, [("safe.bin", [1])]);

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_path_invalid", error.ErrorCode);
    }

    [Theory]
    [InlineData("files/../escape.bin")]
    [InlineData("files//etc/passwd")]
    [InlineData("files/a\\escape.bin")]
    public async Task Read_UnsafeZipEntry_IsRejected(string entryName)
    {
        var manifest = TransferTestData.Manifest();
        var package = TransferTestData.BuildRaw(archive =>
        {
            {
                var entry = archive.CreateEntry(entryName);
                using var stream = entry.Open();
                stream.WriteByte(1);
            }

            WriteManifest(archive, manifest);
        });

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_path_invalid", error.ErrorCode);
    }

    [Fact]
    public async Task Read_SymbolicLinkEntry_IsRejected()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var manifest = TransferTestData.Manifest(files: [("scene.bin", bytes)]);
        var package = TransferTestData.BuildRaw(archive =>
        {
            var entry = archive.CreateEntry("files/scene.bin");
            entry.ExternalAttributes = 0xA000 << 16;
            using (var stream = entry.Open())
            {
                stream.Write(bytes);
            }

            WriteManifest(archive, manifest);
        });

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_path_invalid", error.ErrorCode);
    }

    [Fact]
    public async Task Read_HashMismatch_IsRejected()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var manifest = TransferTestData.Manifest() with
        {
            Files = [new ProjectManifestFile("scene.bin", new string('b', 64), 4)]
        };
        var package = TransferTestData.Build(manifest, [("scene.bin", bytes)]);

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_integrity_failed", error.ErrorCode);
    }

    [Fact]
    public async Task Read_SizeMismatch_IsRejected()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var manifest = TransferTestData.Manifest() with
        {
            Files = [new ProjectManifestFile("scene.bin", TransferTestData.Sha256(bytes), 5)]
        };
        var package = TransferTestData.Build(manifest, [("scene.bin", bytes)]);

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_integrity_failed", error.ErrorCode);
    }

    [Fact]
    public async Task Read_MissingFile_IsRejected()
    {
        var manifest = TransferTestData.Manifest(
            files: [("scene.bin", [1, 2, 3])]);
        var package = TransferTestData.Build(manifest);

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_integrity_failed", error.ErrorCode);
    }

    [Fact]
    public async Task Read_UnindexedFile_IsRejected()
    {
        var manifest = TransferTestData.Manifest();
        var package = TransferTestData.BuildRaw(archive =>
        {
            var entry = archive.CreateEntry("files/extra.bin");
            using (var stream = entry.Open())
            {
                stream.WriteByte(7);
            }

            WriteManifest(archive, manifest);
        });

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_package_invalid", error.ErrorCode);
    }

    [Fact]
    public async Task Read_FileOverLimit_IsRejected()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var manifest = TransferTestData.Manifest(files: [("scene.bin", bytes)]);
        var package = TransferTestData.Build(manifest, [("scene.bin", bytes)]);

        var error = await ReadErrorAsync(
            package,
            new ProjectTransferLimits(
                MaxFileBytes: 4,
                MaxPackageBytes: 8,
                MaxManifestBytes: 1024 * 1024,
                MaxFileCount: 10));

        Assert.Equal("import_too_large", error.ErrorCode);
    }

    [Fact]
    public async Task Read_PackageOverTotalLimit_IsRejected()
    {
        var first = new byte[] { 1, 2, 3, 4, 5 };
        var second = new byte[] { 6, 7, 8, 9, 10 };
        var manifest = TransferTestData.Manifest(
            files: [("a.bin", first), ("b.bin", second)]);
        var package = TransferTestData.Build(
            manifest,
            [("a.bin", first), ("b.bin", second)]);

        var error = await ReadErrorAsync(
            package,
            new ProjectTransferLimits(
                MaxFileBytes: 8,
                MaxPackageBytes: 8,
                MaxManifestBytes: 1024 * 1024,
                MaxFileCount: 10));

        Assert.Equal("import_too_large", error.ErrorCode);
    }

    [Fact]
    public async Task Read_ManifestOverLimit_IsRejected()
    {
        var manifest = TransferTestData.Manifest();
        var package = TransferTestData.Build(manifest);

        var error = await ReadErrorAsync(
            package,
            new ProjectTransferLimits(
                MaxFileBytes: 1024,
                MaxPackageBytes: 4096,
                MaxManifestBytes: 16,
                MaxFileCount: 10));

        Assert.Equal("import_too_large", error.ErrorCode);
    }

    [Fact]
    public async Task Read_SceneAssetHashThatDoesNotMatchFileIndex_IsRejected()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var manifest = TransferTestData.Manifest(projectId, [("scene.bin", bytes)]) with
        {
            SourceJobs =
            [
                new ProjectManifestJob(
                    jobId,
                    projectId,
                    AIStudio.Domain.Jobs.JobType.GenerateStoryboard,
                    AIStudio.Domain.Jobs.JobStatus.Succeeded,
                    "hash",
                    0,
                    0,
                    System.Text.Json.JsonDocument.Parse("{}").RootElement,
                    System.Text.Json.JsonDocument.Parse("{}").RootElement,
                    now.AddMinutes(-2),
                    now.AddMinutes(-1),
                    now.AddMinutes(-2),
                    now.AddMinutes(-1))
            ],
            SceneAssets =
            [
                new ProjectManifestSceneAsset(
                    Guid.NewGuid(),
                    projectId,
                    jobId,
                    0,
                    AIStudio.Domain.Assets.AssetType.Image,
                    "scene.bin",
                    bytes.Length,
                    new string('c', 64),
                    AIStudio.Domain.Assets.AssetOrigin.Local,
                    null,
                    null,
                    null,
                    null,
                    now)
            ]
        };
        var package = TransferTestData.Build(manifest, [("scene.bin", bytes)]);

        var error = await ReadErrorAsync(package);

        Assert.Equal("import_integrity_failed", error.ErrorCode);
    }

    [Theory]
    [InlineData("safe/path.bin", true)]
    [InlineData("file.mp4", true)]
    [InlineData("../escape", false)]
    [InlineData("a/../b", false)]
    [InlineData("/absolute", false)]
    [InlineData("C:\\windows", false)]
    [InlineData("a//b", false)]
    [InlineData("./a", false)]
    [InlineData("a\\b", false)]
    [InlineData(" a", false)]
    public void TryNormalize_EnforcesRelativePackagePaths(string path, bool expected)
    {
        Assert.Equal(expected, ProjectPackagePath.TryNormalize(path, out _));
    }

    private static async Task<ProjectPackageContents> ReadAsync(byte[] package)
    {
        using var stream = new MemoryStream(package);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var reader = new ProjectPackageReader(ProjectTransferLimits.Default);
        return await reader.ReadAsync(archive, TestContext.Current.CancellationToken);
    }

    private static async Task<ProjectTransferException> ReadErrorAsync(
        byte[] package,
        ProjectTransferLimits? limits = null)
    {
        using var stream = new MemoryStream(package);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var reader = new ProjectPackageReader(limits ?? ProjectTransferLimits.Default);
        return await Assert.ThrowsAsync<ProjectTransferException>(
            () => reader.ReadAsync(archive, TestContext.Current.CancellationToken));
    }

    private static void WriteManifest(ZipArchive archive, ProjectTransferManifest manifest)
    {
        var entry = archive.CreateEntry(ProjectPackageSerializer.ManifestEntryName);
        using var stream = entry.Open();
        stream.Write(ProjectPackageSerializer.Serialize(manifest));
    }
}
