using Xunit;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Application.Transfer;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Domain.Scripts;
using AIStudio.Domain.Subtitles;
using AIStudio.Domain.Tracking;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Transfer;
using Microsoft.Extensions.Options;

namespace AIStudio.Tests.Transfer;

internal sealed class TransferTestStore : IDisposable
{
    private TransferTestStore(string assetRoot, LocalProjectTransferStore store)
    {
        AssetRoot = assetRoot;
        Store = store;
    }

    public string AssetRoot { get; }

    public LocalProjectTransferStore Store { get; }

    public string ExportRoot => Path.Combine(AssetRoot, "exports");

    public static TransferTestStore Create(ProjectTransferLimits? limits = null)
    {
        var effective = limits ?? ProjectTransferLimits.Default;
        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-transfer-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var assetOptions = Options.Create(new AssetStorageOptions { RootPath = root });
        var assetStore = new LocalAssetFileStore(assetOptions);
        var transferOptions = Options.Create(new ProjectTransferOptions
        {
            RootPath = "exports",
            MaxFileBytes = effective.MaxFileBytes,
            MaxPackageBytes = effective.MaxPackageBytes,
            MaxManifestBytes = effective.MaxManifestBytes,
            MaxFileCount = effective.MaxFileCount
        });
        return new TransferTestStore(
            root,
            new LocalProjectTransferStore(assetStore, transferOptions));
    }

    public string WriteAssetFile(string relativePath, byte[] bytes)
    {
        var absolute = Path.Combine(
            AssetRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllBytes(absolute, bytes);
        return relativePath;
    }

    public string AssetPath(string relativePath) =>
        Path.Combine(AssetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public byte[] ReadPackage(string fileName) =>
        File.ReadAllBytes(Path.Combine(ExportRoot, fileName));

    public void Dispose()
    {
        if (Directory.Exists(AssetRoot))
        {
            Directory.Delete(AssetRoot, recursive: true);
        }
    }
}

internal sealed class FakeProjectTransferRepository : IProjectTransferRepository
{
    public ProjectTransferSnapshot? Snapshot { get; set; }

    public bool Exists { get; set; }

    public ProjectTransferManifest? Imported { get; private set; }

    public Func<ProjectTransferManifest, Exception?>? ImportFailure { get; set; }

    public Func<ProjectTransferManifest, ProjectImportCounts>? ImportResult { get; set; }

    public Task<bool> ExistsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Exists || Snapshot?.Project.Id == contentProjectId);

    public Task<ProjectTransferSnapshot?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            Snapshot?.Project.Id == contentProjectId ? Snapshot : null);

    public Task<ProjectImportCounts> ImportAsync(
        ProjectTransferManifest manifest,
        CancellationToken cancellationToken)
    {
        Imported = manifest;
        var failure = ImportFailure?.Invoke(manifest);
        if (failure is not null)
        {
            throw failure;
        }

        return Task.FromResult(
            ImportResult?.Invoke(manifest)
            ?? new ProjectImportCounts(
                manifest.ReviewedScript is null ? 0 : 1,
                manifest.SourceJobs.Count,
                manifest.SceneAssets.Count,
                manifest.NarrationTrack is null ? 0 : 1,
                manifest.SubtitleTrack is null ? 0 : 1,
                manifest.FinalApproval is null ? 0 : 1,
                manifest.PublicationRecord is null ? 0 : 1,
                manifest.MetricSnapshots.Count,
                manifest.TimeEntries.Count,
                manifest.CostEntries.Count));
    }
}

internal static class TransferTestData
{
    public const string RenderPath = "renders/video.mp4";
    public const string ScenePath = "renders/scene-0.png";
    public const string NarrationPath = "renders/narration.wav";
    public const string SubtitlePath = "renders/subtitles.srt";

    public static readonly byte[] RenderBytes = [1, 2, 3, 4, 5, 6, 7, 8];

    public static ProjectTransferManifest Manifest(
        Guid? projectId = null,
        IReadOnlyList<(string Path, byte[] Bytes)>? files = null,
        int schemaVersion = ProjectPackageSerializer.CurrentSchemaVersion)
    {
        var id = projectId ?? Guid.NewGuid();
        var entries = (files ?? [])
            .Select(file => new ProjectManifestFile(
                file.Path,
                Sha256(file.Bytes),
                file.Bytes.Length))
            .ToArray();

        return new ProjectTransferManifest(
            schemaVersion,
            DateTimeOffset.UtcNow,
            "test",
            new ProjectManifestProject(
                id,
                "Transfer fixture",
                "Disposable project transfer fixture.",
                ContentStatus.Draft,
                DateTimeOffset.UtcNow.AddHours(-2),
                DateTimeOffset.UtcNow.AddHours(-1)),
            null,
            [],
            [],
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            entries);
    }

    /// <summary>
    /// Minimal manifest that carries a valid final approval bound to one succeeded
    /// RenderVideo job and one packaged artifact. Used to exercise the decision contract.
    /// </summary>
    public static ProjectTransferManifest ManifestWithApproval(
        int schemaVersion,
        string? decision,
        string? decisionNote = null,
        bool withDecidedAt = true)
    {
        var projectId = Guid.NewGuid();
        var renderJobId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var manifest = Manifest(projectId, [(RenderPath, RenderBytes)], schemaVersion);

        return manifest with
        {
            SourceJobs =
            [
                new ProjectManifestJob(
                    renderJobId,
                    projectId,
                    JobType.RenderVideo,
                    JobStatus.Succeeded,
                    "render-hash",
                    0,
                    0,
                    Json("{}"),
                    Json("{}"),
                    now.AddMinutes(-10),
                    now.AddMinutes(-8),
                    now.AddMinutes(-9),
                    now.AddMinutes(-8))
            ],
            FinalApproval = new ProjectManifestFinalApproval(
                Guid.NewGuid(),
                projectId,
                renderJobId,
                RenderPath,
                Sha256(RenderBytes),
                RenderBytes.Length,
                "youtube",
                null,
                "operator",
                1,
                now.AddMinutes(-8),
                now.AddMinutes(-8),
                now.AddMinutes(-8),
                decision,
                decisionNote,
                withDecidedAt ? now.AddMinutes(-8) : null)
        };
    }

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static byte[] Build(
        ProjectTransferManifest manifest,
        IReadOnlyList<(string Path, byte[] Bytes)>? files = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files ?? [])
            {
                var entry = archive.CreateEntry(
                    ProjectPackageSerializer.FilesEntryPrefix + file.Path);
                using var stream = entry.Open();
                stream.Write(file.Bytes);
            }

            var manifestEntry = archive.CreateEntry(
                ProjectPackageSerializer.ManifestEntryName);
            using var manifestStream = manifestEntry.Open();
            manifestStream.Write(ProjectPackageSerializer.Serialize(manifest));
        }

        return buffer.ToArray();
    }

    public static byte[] BuildRaw(Action<ZipArchive> write)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            write(archive);
        }

        return buffer.ToArray();
    }

    public static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Full snapshot with every restorable row, used by export and round-trip tests.</summary>
    public static ProjectTransferSnapshot Snapshot(
        out byte[] renderBytes,
        out byte[] sceneBytes,
        out byte[] narrationBytes,
        out byte[] subtitleBytes,
        FinalApprovalDecision decision = FinalApprovalDecision.Approved,
        string? decisionNote = null)
    {
        var now = DateTimeOffset.UtcNow;
        renderBytes = [10, 20, 30, 40];
        sceneBytes = [50, 60, 70];
        narrationBytes = [80, 90];
        subtitleBytes = [11, 22, 33, 44];

        var project = ContentProject.Create(
            "Transfer round-trip project",
            "Everything the package must restore.",
            now.AddMinutes(-180));
        project.MarkReadyToPublish(now.AddMinutes(-60));

        var storyboardJob = Job.Create(
            project.Id,
            JobType.GenerateStoryboard,
            "storyboard-hash",
            """{"contentProjectId":"fixture"}""",
            now.AddMinutes(-120),
            maxRetries: 1);
        storyboardJob.Start(
            "transfer-test",
            now.AddMinutes(-114),
            now.AddMinutes(-119));
        storyboardJob.Succeed(
            """{"title":"Storyboard","scenes":[]}""",
            now.AddMinutes(-118));

        var script = ReviewedScript.Create(
            project.Id,
            storyboardJob.Id,
            """{"title":"Approved","openingHook":"Hook","sections":[],"closing":"Close"}""",
            now.AddMinutes(-118));
        script.Approve(now.AddMinutes(-117));

        var narration = NarrationTrack.Create(
            project.Id,
            storyboardJob.Id,
            NarrationPath,
            narrationBytes.Length,
            Sha256(narrationBytes),
            AssetOrigin.Local,
            null,
            "test",
            null,
            null,
            now.AddMinutes(-116));
        var subtitle = SubtitleTrack.Create(
            project.Id,
            storyboardJob.Id,
            SubtitlePath,
            subtitleBytes.Length,
            Sha256(subtitleBytes),
            AssetOrigin.Local,
            null,
            "test",
            null,
            null,
            now.AddMinutes(-115));
        var scene = SceneAsset.Create(
            project.Id,
            storyboardJob.Id,
            0,
            AssetType.Image,
            ScenePath,
            sceneBytes.Length,
            Sha256(sceneBytes),
            AssetOrigin.Local,
            null,
            "test",
            null,
            null,
            now.AddMinutes(-114));

        var renderResult = new AIStudio.Application.Jobs.RenderVideo.RenderVideoResult(
            RenderPath,
            Sha256(renderBytes),
            renderBytes.Length,
            12.5,
            1280,
            720,
            storyboardJob.Id,
            narration.Id,
            2,
            narration.ContentHash,
            subtitle.Id,
            subtitle.ContentHash,
            SubtitleBurnedIn: true);
        var renderJob = Job.Create(
            project.Id,
            JobType.RenderVideo,
            "render-hash",
            "{}",
            now.AddMinutes(-90),
            maxRetries: 0);
        renderJob.Start(
            "transfer-test",
            now.AddMinutes(-84),
            now.AddMinutes(-89));
        renderJob.Succeed(renderResult.Serialize(), now.AddMinutes(-88));

        var qaPayload = System.Text.Json.JsonSerializer.Serialize(
            new AIStudio.Application.Jobs.FinalVideoQa.FinalVideoQaJobPayload(
                project.Id,
                renderJob.Id),
            new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web));
        var qaResult = new AIStudio.Application.Jobs.FinalVideoQa.FinalVideoQaResult(
            RenderPath,
            Sha256(renderBytes),
            12.5,
            1280,
            720,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: true);
        var qaJob = Job.Create(
            project.Id,
            JobType.FinalVideoQa,
            "qa-hash",
            qaPayload,
            now.AddMinutes(-88),
            maxRetries: 0);
        qaJob.Start(
            "transfer-test",
            now.AddMinutes(-82),
            now.AddMinutes(-87));
        qaJob.Succeed(qaResult.Serialize(), now.AddMinutes(-86));

        var approval = FinalApproval.Create(
            project.Id,
            renderJob.Id,
            RenderPath,
            Sha256(renderBytes),
            renderBytes.Length,
            "youtube",
            "manual upload prepared",
            "operator-transfer",
            now.AddMinutes(-50),
            decision: decision,
            decisionNote: decisionNote);
        var publication = PublicationRecord.Create(
            project.Id,
            approval.Id,
            approval.Revision,
            approval.ArtifactContentHash,
            "youtube",
            "https://youtu.be/dQw4w9WgXcQ",
            "dQw4w9WgXcQ",
            now.AddMinutes(-40),
            "manual publish",
            now.AddMinutes(-39));
        var metric = MetricSnapshot.Create(
            project.Id,
            publication.Id,
            120,
            1200,
            0.1m,
            8.5m,
            17m,
            3,
            now.AddMinutes(-30),
            "manual",
            "first capture",
            now.AddMinutes(-30));
        var time = TimeEntry.Create(
            project.Id,
            TimeEntryKind.Human,
            "editing",
            45,
            now.AddMinutes(-20),
            "edit pass",
            now.AddMinutes(-20));
        var cost = CostEntry.Create(
            project.Id,
            12.5m,
            "usd",
            CostEntryKind.Actual,
            "tools",
            now.AddMinutes(-10),
            "stock music",
            now.AddMinutes(-10));

        return new ProjectTransferSnapshot(
            project,
            script,
            [storyboardJob, renderJob, qaJob],
            [scene],
            narration,
            subtitle,
            approval,
            publication,
            [metric],
            [time],
            [cost]);
    }
}
