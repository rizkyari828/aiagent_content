using System.IO.Compression;
using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Transfer;

/// <summary>
/// Builds one deterministic ZIP package for an existing ContentProject. The package is
/// written to a temporary file and promoted atomically only after every file matched its
/// persisted SHA-256 and size, so a failed export never leaves a partial package behind.
/// </summary>
public sealed class ProjectExportWorkflow(
    IProjectTransferRepository repository,
    IProjectTransferStore store,
    TimeProvider timeProvider)
{
    public async Task<ProjectPackageInfo> ExportAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw Invalid("A non-empty content project identifier is required.");
        }

        var snapshot = await repository.FindAsync(contentProjectId, cancellationToken);
        if (snapshot is null)
        {
            throw new ProjectTransferException(
                "export_project_not_found",
                $"Content project '{contentProjectId}' does not exist.");
        }

        if (snapshot.Jobs.Any(job => job.Status is JobStatus.Queued or JobStatus.Running))
        {
            throw new ProjectTransferException(
                "export_project_busy",
                "A package can only be exported from a project with no queued or running jobs.");
        }

        var exportedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var manifest = BuildManifest(snapshot, exportedAtUtc);
        var candidates = CollectFileCandidates(snapshot);
        var expectations = CollectFileExpectations(snapshot);

        return await store.WritePackageAsync(
            contentProjectId,
            exportedAtUtc,
            async (package, token) =>
            {
                using var archive = new ZipArchive(
                    package,
                    ZipArchiveMode.Create,
                    leaveOpen: true);

                var files = new List<ProjectManifestFile>(candidates.Count);
                long totalBytes = 0;

                foreach (var relativePath in candidates)
                {
                    var entry = archive.CreateEntry(
                        ProjectPackageSerializer.FilesEntryPrefix + relativePath,
                        CompressionLevel.NoCompression);
                    await using var destination = entry.Open();
                    var copy = await store.CopyAssetAsync(relativePath, destination, token);

                    totalBytes += copy.ByteSize;
                    if (totalBytes > store.Limits.MaxPackageBytes)
                    {
                        throw new ProjectTransferException(
                            "export_package_too_large",
                            "The project exceeds the configured package size limit.");
                    }

                    if (expectations.TryGetValue(relativePath, out var expected))
                    {
                        foreach (var item in expected)
                        {
                            if (copy.ByteSize != item.ByteSize
                                || !string.Equals(
                                    copy.Sha256,
                                    item.Sha256,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                throw new ProjectTransferException(
                                    "export_integrity_failed",
                                    $"File '{relativePath}' no longer matches the hashed "
                                    + $"{item.Description} metadata.");
                            }
                        }
                    }

                    files.Add(new ProjectManifestFile(
                        relativePath,
                        copy.Sha256,
                        copy.ByteSize));
                }

                var completed = manifest with
                {
                    Files = files
                        .OrderBy(file => file.Path, StringComparer.Ordinal)
                        .ToArray()
                };

                var manifestEntry = archive.CreateEntry(
                    ProjectPackageSerializer.ManifestEntryName,
                    CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await manifestStream.WriteAsync(
                    ProjectPackageSerializer.Serialize(completed),
                    token);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectPackageInfo>> ListAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw Invalid("A non-empty content project identifier is required.");
        }

        if (!await repository.ExistsAsync(contentProjectId, cancellationToken))
        {
            throw new ProjectTransferException(
                "export_project_not_found",
                $"Content project '{contentProjectId}' does not exist.");
        }

        return store.ListPackages(contentProjectId);
    }

    private static ProjectTransferManifest BuildManifest(
        ProjectTransferSnapshot snapshot,
        DateTimeOffset exportedAtUtc) =>
        new(
            ProjectPackageSerializer.CurrentSchemaVersion,
            exportedAtUtc,
            ProjectPackageSerializer.CurrentAppVersion(),
            new ProjectManifestProject(
                snapshot.Project.Id,
                snapshot.Project.Title,
                snapshot.Project.Brief,
                snapshot.Project.Status,
                snapshot.Project.CreatedAt,
                snapshot.Project.UpdatedAt),
            snapshot.ReviewedScript is null
                ? null
                : new ProjectManifestReviewedScript(
                    snapshot.ReviewedScript.Id,
                    snapshot.ReviewedScript.ContentProjectId,
                    snapshot.ReviewedScript.SourceJobId,
                    ParseJson(snapshot.ReviewedScript.Content),
                    snapshot.ReviewedScript.Status,
                    snapshot.ReviewedScript.Revision,
                    snapshot.ReviewedScript.CreatedAt,
                    snapshot.ReviewedScript.UpdatedAt,
                    snapshot.ReviewedScript.ApprovedAt),
            snapshot.Jobs
                .OrderBy(job => job.CreatedAt)
                .ThenBy(job => job.Id)
                .Select(job => new ProjectManifestJob(
                    job.Id,
                    job.ContentProjectId,
                    job.Type,
                    job.Status,
                    job.InputVersionHash,
                    job.RetryCount,
                    job.MaxRetries,
                    ParseJson(job.Payload),
                    job.Result is null ? null : ParseJson(job.Result),
                    job.CreatedAt,
                    job.UpdatedAt,
                    job.StartedAt,
                    job.CompletedAt))
                .ToArray(),
            snapshot.SceneAssets
                .OrderBy(asset => asset.SceneIndex)
                .ThenBy(asset => asset.Id)
                .Select(asset => new ProjectManifestSceneAsset(
                    asset.Id,
                    asset.ContentProjectId,
                    asset.SourceJobId,
                    asset.SceneIndex,
                    asset.Type,
                    asset.Path,
                    asset.ByteSize,
                    asset.ContentHash,
                    asset.Origin,
                    asset.Source,
                    asset.Creator,
                    asset.License,
                    asset.RetrievedAt,
                    asset.CreatedAt))
                .ToArray(),
            snapshot.NarrationTrack is null
                ? null
                : new ProjectManifestMediaTrack(
                    snapshot.NarrationTrack.Id,
                    snapshot.NarrationTrack.ContentProjectId,
                    snapshot.NarrationTrack.SourceJobId,
                    snapshot.NarrationTrack.Path,
                    snapshot.NarrationTrack.ByteSize,
                    snapshot.NarrationTrack.ContentHash,
                    snapshot.NarrationTrack.Origin,
                    snapshot.NarrationTrack.Source,
                    snapshot.NarrationTrack.Creator,
                    snapshot.NarrationTrack.License,
                    snapshot.NarrationTrack.RetrievedAt,
                    snapshot.NarrationTrack.CreatedAt),
            snapshot.SubtitleTrack is null
                ? null
                : new ProjectManifestMediaTrack(
                    snapshot.SubtitleTrack.Id,
                    snapshot.SubtitleTrack.ContentProjectId,
                    snapshot.SubtitleTrack.SourceJobId,
                    snapshot.SubtitleTrack.Path,
                    snapshot.SubtitleTrack.ByteSize,
                    snapshot.SubtitleTrack.ContentHash,
                    snapshot.SubtitleTrack.Origin,
                    snapshot.SubtitleTrack.Source,
                    snapshot.SubtitleTrack.Creator,
                    snapshot.SubtitleTrack.License,
                    snapshot.SubtitleTrack.RetrievedAt,
                    snapshot.SubtitleTrack.CreatedAt),
            snapshot.FinalApproval is null
                ? null
                : new ProjectManifestFinalApproval(
                    snapshot.FinalApproval.Id,
                    snapshot.FinalApproval.ContentProjectId,
                    snapshot.FinalApproval.SourceRenderJobId,
                    snapshot.FinalApproval.ArtifactPath,
                    snapshot.FinalApproval.ArtifactContentHash,
                    snapshot.FinalApproval.ArtifactByteSize,
                    snapshot.FinalApproval.Destination,
                    snapshot.FinalApproval.Notes,
                    snapshot.FinalApproval.ApproverLabel,
                    snapshot.FinalApproval.Revision,
                    snapshot.FinalApproval.ApprovedAt,
                    snapshot.FinalApproval.CreatedAt,
                    snapshot.FinalApproval.UpdatedAt),
            snapshot.PublicationRecord is null
                ? null
                : new ProjectManifestPublication(
                    snapshot.PublicationRecord.Id,
                    snapshot.PublicationRecord.ContentProjectId,
                    snapshot.PublicationRecord.FinalApprovalId,
                    snapshot.PublicationRecord.FinalApprovalRevision,
                    snapshot.PublicationRecord.ArtifactContentHash,
                    snapshot.PublicationRecord.Platform,
                    snapshot.PublicationRecord.Url,
                    snapshot.PublicationRecord.VideoId,
                    snapshot.PublicationRecord.PublishedAt,
                    snapshot.PublicationRecord.Notes,
                    snapshot.PublicationRecord.CreatedAt),
            snapshot.MetricSnapshots
                .OrderBy(metric => metric.CapturedAt)
                .ThenBy(metric => metric.Id)
                .Select(metric => new ProjectManifestMetricSnapshot(
                    metric.Id,
                    metric.ContentProjectId,
                    metric.PublicationRecordId,
                    metric.Views,
                    metric.Impressions,
                    metric.ClickThroughRate,
                    metric.AverageViewDurationSeconds,
                    metric.WatchTimeMinutes,
                    metric.SubscribersGained,
                    metric.CapturedAt,
                    metric.Source,
                    metric.Notes,
                    metric.CreatedAt))
                .ToArray(),
            snapshot.TimeEntries
                .OrderBy(entry => entry.RecordedAt)
                .ThenBy(entry => entry.Id)
                .Select(entry => new ProjectManifestTimeEntry(
                    entry.Id,
                    entry.ContentProjectId,
                    entry.Kind,
                    entry.Category,
                    entry.Minutes,
                    entry.RecordedAt,
                    entry.Notes,
                    entry.CreatedAt))
                .ToArray(),
            snapshot.CostEntries
                .OrderBy(entry => entry.IncurredAt)
                .ThenBy(entry => entry.Id)
                .Select(entry => new ProjectManifestCostEntry(
                    entry.Id,
                    entry.ContentProjectId,
                    entry.Amount,
                    entry.Currency,
                    entry.Kind,
                    entry.Category,
                    entry.IncurredAt,
                    entry.Notes,
                    entry.CreatedAt))
                .ToArray(),
            []);

    private List<string> CollectFileCandidates(ProjectTransferSnapshot snapshot)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in CollectFileExpectations(snapshot).Keys)
        {
            paths.Add(path);
        }

        foreach (var job in snapshot.Jobs)
        {
            if (job.Result is null)
            {
                continue;
            }

            if (job.Type == JobType.RenderVideo)
            {
                try
                {
                    var result = RenderVideoResult.Deserialize(job.Result);
                    paths.Add(result.OutputPath);
                    if (result.MasteredAudioPath is not null)
                    {
                        paths.Add(result.MasteredAudioPath);
                    }
                }
                catch (JobExecutionException)
                {
                    // Historical evidence that no longer parses is kept in the manifest,
                    // but it cannot contribute a file candidate.
                }
            }
            else if (job.Type == JobType.FinalVideoQa)
            {
                try
                {
                    paths.Add(FinalVideoQaResult.Deserialize(job.Result).OutputPath);
                }
                catch (JobExecutionException)
                {
                }
            }
        }

        var normalized = new List<string>(paths.Count);
        var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!ProjectPackagePath.TryNormalize(path, out var safePath))
            {
                throw new ProjectTransferException(
                    "export_path_invalid",
                    $"Project metadata contains an unsafe asset path: '{path}'.");
            }

            if (!caseInsensitive.Add(safePath))
            {
                throw new ProjectTransferException(
                    "export_path_invalid",
                    "Project metadata contains paths that collide on a case-insensitive filesystem.");
            }

            normalized.Add(safePath);
        }

        return normalized;
    }

    private static Dictionary<string, List<ExpectedFile>> CollectFileExpectations(
        ProjectTransferSnapshot snapshot)
    {
        var expectations = new Dictionary<string, List<ExpectedFile>>(StringComparer.Ordinal);

        foreach (var asset in snapshot.SceneAssets)
        {
            Expect(
                expectations,
                asset.Path,
                asset.ContentHash,
                asset.ByteSize,
                $"scene asset {asset.SceneIndex}");
        }

        if (snapshot.NarrationTrack is not null)
        {
            Expect(
                expectations,
                snapshot.NarrationTrack.Path,
                snapshot.NarrationTrack.ContentHash,
                snapshot.NarrationTrack.ByteSize,
                "narration track");
        }

        if (snapshot.SubtitleTrack is not null)
        {
            Expect(
                expectations,
                snapshot.SubtitleTrack.Path,
                snapshot.SubtitleTrack.ContentHash,
                snapshot.SubtitleTrack.ByteSize,
                "subtitle track");
        }

        if (snapshot.FinalApproval is not null)
        {
            Expect(
                expectations,
                snapshot.FinalApproval.ArtifactPath,
                snapshot.FinalApproval.ArtifactContentHash,
                snapshot.FinalApproval.ArtifactByteSize,
                "final approval artifact");
        }

        return expectations;
    }

    private static void Expect(
        Dictionary<string, List<ExpectedFile>> expectations,
        string path,
        string sha256,
        long byteSize,
        string description)
    {
        if (!expectations.TryGetValue(path, out var items))
        {
            items = [];
            expectations[path] = items;
        }

        items.Add(new ExpectedFile(sha256, byteSize, description));
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ProjectTransferException Invalid(string message) =>
        new("export_project_not_found", message);

    private sealed record ExpectedFile(
        string Sha256,
        long ByteSize,
        string Description);
}
