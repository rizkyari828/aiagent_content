using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Transfer;

/// <summary>
/// Reads and verifies one package before anything is written to the database or the
/// approved root. The order is deliberate: schema version, safe entry paths, manifest
/// structure, then byte-level size and SHA-256 verification. Enums, hashes, and paths are
/// closed contracts, and unknown JSON members are rejected by the serializer.
/// </summary>
public sealed class ProjectPackageReader(ProjectTransferLimits limits)
{
    private const int BufferSize = 81_920;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;

    private readonly ProjectTransferLimits limits =
        limits ?? throw new ArgumentNullException(nameof(limits));

    public async Task<ProjectPackageContents> ReadAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        limits.EnsureValid();

        if (archive.Entries.Count > limits.MaxFileCount + 64)
        {
            throw TooLarge("The package contains more entries than the configured limit.");
        }

        var manifestEntries = archive.Entries
            .Where(entry => string.Equals(
                entry.FullName,
                ProjectPackageSerializer.ManifestEntryName,
                StringComparison.Ordinal))
            .ToArray();
        if (manifestEntries.Length != 1)
        {
            throw Invalid("The package must contain exactly one manifest.json entry.");
        }

        var manifestEntry = manifestEntries[0];

        if (manifestEntry.Length > limits.MaxManifestBytes)
        {
            throw TooLarge("The package manifest exceeds the configured size limit.");
        }

        var manifestBytes = await ReadEntryAsync(
            manifestEntry,
            limits.MaxManifestBytes,
            cancellationToken);
        var manifest = ProjectPackageSerializer.Deserialize(manifestBytes);

        if (manifest.SchemaVersion != ProjectPackageSerializer.CurrentSchemaVersion)
        {
            throw new ProjectTransferException(
                "import_schema_unsupported",
                $"Package schema version {manifest.SchemaVersion} is not supported; "
                + $"expected {ProjectPackageSerializer.CurrentSchemaVersion}.");
        }

        var packagedFiles = IndexFileEntries(archive);
        ValidateManifest(manifest, packagedFiles.Keys);

        var verified = new List<ProjectPackageFile>(manifest.Files.Count);
        long totalBytes = 0;

        foreach (var declared in manifest.Files)
        {
            if (!packagedFiles.TryGetValue(declared.Path, out var entry))
            {
                throw Integrity($"Packaged file '{declared.Path}' is missing from the ZIP.");
            }

            if (declared.ByteSize <= 0)
            {
                throw Integrity($"Packaged file '{declared.Path}' declares an empty size.");
            }

            if (declared.ByteSize > limits.MaxFileBytes)
            {
                throw TooLarge($"Packaged file '{declared.Path}' exceeds the file size limit.");
            }

            totalBytes += declared.ByteSize;
            if (totalBytes > limits.MaxPackageBytes)
            {
                throw TooLarge("The package exceeds the total size limit.");
            }

            var (byteSize, sha256) = await HashEntryAsync(entry, cancellationToken);
            if (byteSize != declared.ByteSize
                || !HashEquals(sha256, declared.Sha256)
                || byteSize != entry.Length)
            {
                throw Integrity(
                    $"Packaged file '{declared.Path}' does not match its declared size or SHA-256.");
            }

            verified.Add(new ProjectPackageFile(entry, declared.Path, byteSize, sha256));
        }

        var extra = packagedFiles.Keys
            .Except(manifest.Files.Select(file => file.Path), StringComparer.Ordinal)
            .ToArray();
        if (extra.Length > 0)
        {
            throw Invalid(
                $"The package contains {extra.Length} file(s) that are not listed in the manifest.");
        }

        return new ProjectPackageContents(manifest, verified);
    }

    private Dictionary<string, ZipArchiveEntry> IndexFileEntries(ZipArchive archive)
    {
        var files = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var caseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries)
        {
            if (string.Equals(
                    entry.FullName,
                    ProjectPackageSerializer.ManifestEntryName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.FullName.EndsWith('/'))
            {
                ValidateDirectoryEntry(entry.FullName);
                continue;
            }

            if (IsSymbolicLink(entry))
            {
                throw PathInvalid("Symbolic links are not allowed in a package.");
            }

            if (!entry.FullName.StartsWith(
                    ProjectPackageSerializer.FilesEntryPrefix,
                    StringComparison.Ordinal))
            {
                throw PathInvalid(
                    "Every package entry must be manifest.json or live under files/.");
            }

            var candidate = entry.FullName[ProjectPackageSerializer.FilesEntryPrefix.Length..];
            if (!ProjectPackagePath.TryNormalize(candidate, out var normalized))
            {
                throw PathInvalid($"Package entry '{entry.FullName}' is not a safe relative path.");
            }

            if (!files.TryAdd(normalized, entry))
            {
                throw Invalid($"Package entry '{normalized}' is duplicated.");
            }

            if (!caseInsensitive.Add(normalized))
            {
                throw PathInvalid(
                    $"Package entry '{normalized}' differs from another entry only by case.");
            }

            if (files.Count > limits.MaxFileCount)
            {
                throw TooLarge("The package contains more files than the configured limit.");
            }
        }

        return files;
    }

    private static void ValidateDirectoryEntry(string entryName)
    {
        if (!entryName.StartsWith(
                ProjectPackageSerializer.FilesEntryPrefix,
                StringComparison.Ordinal))
        {
            throw PathInvalid("Only files/ directory entries are allowed in a package.");
        }

        var candidate = entryName.TrimEnd('/');
        if (candidate.Length == ProjectPackageSerializer.FilesEntryPrefix.Length - 1)
        {
            return;
        }

        var relative = candidate[ProjectPackageSerializer.FilesEntryPrefix.Length..];
        if (relative.Length > 0 && !ProjectPackagePath.TryNormalize(relative, out _))
        {
            throw PathInvalid($"Package directory entry '{entryName}' is not a safe path.");
        }
    }

    private static void ValidateManifest(
        ProjectTransferManifest manifest,
        IReadOnlyCollection<string> packagedPaths)
    {
        if (manifest.Project is null)
        {
            throw Invalid("The manifest does not contain a project.");
        }

        if (manifest.SourceJobs is null
            || manifest.SceneAssets is null
            || manifest.MetricSnapshots is null
            || manifest.TimeEntries is null
            || manifest.CostEntries is null
            || manifest.Files is null)
        {
            throw Invalid("The manifest is missing a required collection.");
        }

        if (manifest.ExportedAtUtc == default)
        {
            throw Invalid("The manifest export timestamp is missing.");
        }

        var project = manifest.Project;
        if (project.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(project.Title)
            || project.Title.Length > 200
            || (project.Brief?.Length ?? 0) > 4_000
            || project.CreatedAt == default
            || project.UpdatedAt < project.CreatedAt)
        {
            throw Invalid("The manifest project is not valid.");
        }

        var jobs = ValidateJobs(manifest);
        ValidateReviewedScript(manifest, jobs);
        ValidateSceneAssets(manifest, jobs);
        ValidateMediaTrack(manifest.NarrationTrack, jobs, "narration");
        ValidateMediaTrack(manifest.SubtitleTrack, jobs, "subtitle");
        ValidateFinalApproval(manifest, jobs);
        ValidatePublication(manifest);
        ValidateTracking(manifest);

        var indexed = new Dictionary<string, ProjectManifestFile>(StringComparer.Ordinal);
        foreach (var file in manifest.Files)
        {
            if (!ProjectPackagePath.TryNormalize(file.Path, out var path))
            {
                throw PathInvalid($"Manifest file path '{file.Path}' is not safe.");
            }

            if (!string.Equals(path, file.Path, StringComparison.Ordinal))
            {
                throw PathInvalid($"Manifest file path '{file.Path}' is not normalized.");
            }

            if (!IsSha256(file.Sha256))
            {
                throw Integrity($"Manifest file '{file.Path}' does not carry a SHA-256 hash.");
            }

            if (file.ByteSize <= 0)
            {
                throw Integrity($"Manifest file '{file.Path}' declares an empty size.");
            }

            if (!indexed.TryAdd(file.Path, file))
            {
                throw Invalid($"Manifest file '{file.Path}' is listed more than once.");
            }
        }

        if (!packagedPaths.All(indexed.ContainsKey))
        {
            // IndexFileEntries and the manifest are compared again after hashing; this
            // early check is only about declared paths.
            throw Invalid("The package contains files that are not indexed by the manifest.");
        }

        foreach (var asset in manifest.SceneAssets)
        {
            RequireMatchingFile(
                indexed,
                asset.Path,
                asset.ContentHash,
                asset.ByteSize,
                $"scene asset {asset.SceneIndex}");
        }

        if (manifest.NarrationTrack is not null)
        {
            RequireMatchingFile(
                indexed,
                manifest.NarrationTrack.Path,
                manifest.NarrationTrack.ContentHash,
                manifest.NarrationTrack.ByteSize,
                "narration track");
        }

        if (manifest.SubtitleTrack is not null)
        {
            RequireMatchingFile(
                indexed,
                manifest.SubtitleTrack.Path,
                manifest.SubtitleTrack.ContentHash,
                manifest.SubtitleTrack.ByteSize,
                "subtitle track");
        }

        if (manifest.FinalApproval is not null)
        {
            RequireMatchingFile(
                indexed,
                manifest.FinalApproval.ArtifactPath,
                manifest.FinalApproval.ArtifactContentHash,
                manifest.FinalApproval.ArtifactByteSize,
                "final approval artifact");
        }
    }

    private static HashSet<Guid> ValidateJobs(ProjectTransferManifest manifest)
    {
        var ids = new HashSet<Guid>();
        foreach (var job in manifest.SourceJobs)
        {
            if (job.Id == Guid.Empty
                || job.ContentProjectId != manifest.Project.Id
                || !Enum.IsDefined(job.Type)
                || !Enum.IsDefined(job.Status)
                || string.IsNullOrWhiteSpace(job.InputVersionHash)
                || job.InputVersionHash.Length > 128
                || job.RetryCount < 0
                || job.MaxRetries is < 0 or > 10
                || job.RetryCount > job.MaxRetries
                || job.Payload.ValueKind != JsonValueKind.Object
                || job.Result is { ValueKind: not JsonValueKind.Object }
                || job.CreatedAt == default
                || job.UpdatedAt < job.CreatedAt
                || !ids.Add(job.Id))
            {
                throw Invalid($"Job '{job.Id}' is not a valid restorable job.");
            }

            if (job.Status is JobStatus.Queued or JobStatus.Running)
            {
                throw Invalid(
                    "A package can only contain terminal jobs; in-flight work is not transferable.");
            }

            var succeeded = job.Status == JobStatus.Succeeded;
            if (job.Status == JobStatus.Succeeded
                && (job.Result is null || job.StartedAt is null || job.CompletedAt is null))
            {
                throw Invalid($"Succeeded job '{job.Id}' is missing result or timestamps.");
            }

            if (job.Status == JobStatus.Failed
                && (job.Result is not null || job.StartedAt is null || job.CompletedAt is null))
            {
                throw Invalid($"Failed job '{job.Id}' has inconsistent evidence.");
            }

            if (!succeeded && job.Result is not null)
            {
                throw Invalid($"Only a succeeded job may carry a result ('{job.Id}').");
            }
        }

        return ids;
    }

    private static void ValidateReviewedScript(
        ProjectTransferManifest manifest,
        HashSet<Guid> jobs)
    {
        var script = manifest.ReviewedScript;
        if (script is null)
        {
            return;
        }

        if (script.Id == Guid.Empty
            || script.ContentProjectId != manifest.Project.Id
            || !jobs.Contains(script.SourceJobId)
            || script.Content.ValueKind != JsonValueKind.Object
            || !Enum.IsDefined(script.Status)
            || script.Revision < 1
            || script.CreatedAt == default
            || script.UpdatedAt < script.CreatedAt
            || (script.Status == ScriptReviewStatus.Approved)
                != (script.ApprovedAt is not null))
        {
            throw Invalid("The reviewed script is not valid.");
        }
    }

    private static void ValidateSceneAssets(
        ProjectTransferManifest manifest,
        HashSet<Guid> jobs)
    {
        var ids = new HashSet<Guid>();
        var scenes = new HashSet<int>();
        foreach (var asset in manifest.SceneAssets)
        {
            if (asset.Id == Guid.Empty
                || asset.ContentProjectId != manifest.Project.Id
                || !jobs.Contains(asset.SourceJobId)
                || asset.SceneIndex < 0
                || !scenes.Add(asset.SceneIndex)
                || !Enum.IsDefined(asset.Type)
                || !Enum.IsDefined(asset.Origin)
                || !ProjectPackagePath.TryNormalize(asset.Path, out _)
                || !IsSha256(asset.ContentHash)
                || asset.ByteSize < 0
                || asset.CreatedAt == default
                || !ids.Add(asset.Id))
            {
                throw Invalid($"Scene asset '{asset.Id}' is not valid.");
            }

            if (asset.Origin == AssetOrigin.External
                && (string.IsNullOrWhiteSpace(asset.Source)
                    || string.IsNullOrWhiteSpace(asset.License)))
            {
                throw Invalid(
                    $"External scene asset '{asset.Id}' is missing provenance or license.");
            }
        }
    }

    private static void ValidateMediaTrack(
        ProjectManifestMediaTrack? track,
        HashSet<Guid> jobs,
        string name)
    {
        if (track is null)
        {
            return;
        }

        if (track.Id == Guid.Empty
            || !jobs.Contains(track.SourceJobId)
            || !ProjectPackagePath.TryNormalize(track.Path, out _)
            || !IsSha256(track.ContentHash)
            || track.ByteSize < 0
            || !Enum.IsDefined(track.Origin)
            || track.CreatedAt == default)
        {
            throw Invalid($"The {name} track is not valid.");
        }

        if (track.Origin == AssetOrigin.External
            && (string.IsNullOrWhiteSpace(track.Source)
                || string.IsNullOrWhiteSpace(track.License)))
        {
            throw Invalid($"The {name} track is missing provenance or license.");
        }
    }

    private static void ValidateFinalApproval(
        ProjectTransferManifest manifest,
        HashSet<Guid> jobs)
    {
        var approval = manifest.FinalApproval;
        if (approval is null)
        {
            return;
        }

        var renderJob = approval.Id == Guid.Empty
            ? null
            : manifest.SourceJobs.SingleOrDefault(
                job => job.Id == approval.SourceRenderJobId);

        if (approval.Id == Guid.Empty
            || approval.ContentProjectId != manifest.Project.Id
            || renderJob is null
            || renderJob.Type != JobType.RenderVideo
            || renderJob.Status != JobStatus.Succeeded
            || !ProjectPackagePath.TryNormalize(approval.ArtifactPath, out _)
            || !IsSha256(approval.ArtifactContentHash)
            || approval.ArtifactByteSize <= 0
            || string.IsNullOrWhiteSpace(approval.Destination)
            || approval.Revision < 1
            || approval.ApprovedAt == default
            || approval.CreatedAt == default
            || approval.UpdatedAt < approval.CreatedAt)
        {
            throw Invalid("The final approval is not valid.");
        }
    }

    private static void ValidatePublication(ProjectTransferManifest manifest)
    {
        var publication = manifest.PublicationRecord;
        if (publication is null)
        {
            return;
        }

        if (manifest.FinalApproval is null
            || publication.Id == Guid.Empty
            || publication.ContentProjectId != manifest.Project.Id
            || publication.FinalApprovalId != manifest.FinalApproval.Id
            || publication.FinalApprovalRevision < 1
            || !IsSha256(publication.ArtifactContentHash)
            || string.IsNullOrWhiteSpace(publication.Platform)
            || string.IsNullOrWhiteSpace(publication.Url)
            || string.IsNullOrWhiteSpace(publication.VideoId)
            || publication.CreatedAt == default)
        {
            throw Invalid("The publication record is not valid.");
        }

        // A publication keeps the approval revision/artifact it authorized. The live
        // approval may have been revised afterwards (for example re-approved against a
        // newer render), so the two hashes are preserved independently rather than
        // forced to match on restore.
    }

    private static void ValidateTracking(ProjectTransferManifest manifest)
    {
        var metricIds = new HashSet<Guid>();
        var captures = new HashSet<DateTimeOffset>();
        foreach (var metric in manifest.MetricSnapshots)
        {
            if (metric.Id == Guid.Empty
                || metric.ContentProjectId != manifest.Project.Id
                || metric.CapturedAt == default
                || string.IsNullOrWhiteSpace(metric.Source)
                || !captures.Add(metric.CapturedAt)
                || !metricIds.Add(metric.Id))
            {
                throw Invalid($"Metric snapshot '{metric.Id}' is not valid.");
            }

            if (metric.PublicationRecordId is { } publicationId
                && (manifest.PublicationRecord is null
                    || publicationId != manifest.PublicationRecord.Id))
            {
                throw Invalid(
                    $"Metric snapshot '{metric.Id}' references an unknown publication.");
            }
        }

        var timeIds = new HashSet<Guid>();
        foreach (var entry in manifest.TimeEntries)
        {
            if (entry.Id == Guid.Empty
                || entry.ContentProjectId != manifest.Project.Id
                || !Enum.IsDefined(entry.Kind)
                || string.IsNullOrWhiteSpace(entry.Category)
                || entry.Minutes <= 0
                || entry.RecordedAt == default
                || !timeIds.Add(entry.Id))
            {
                throw Invalid($"Time entry '{entry.Id}' is not valid.");
            }
        }

        var costIds = new HashSet<Guid>();
        foreach (var entry in manifest.CostEntries)
        {
            if (entry.Id == Guid.Empty
                || entry.ContentProjectId != manifest.Project.Id
                || !Enum.IsDefined(entry.Kind)
                || string.IsNullOrWhiteSpace(entry.Category)
                || entry.Amount <= 0
                || string.IsNullOrWhiteSpace(entry.Currency)
                || entry.IncurredAt == default
                || !costIds.Add(entry.Id))
            {
                throw Invalid($"Cost entry '{entry.Id}' is not valid.");
            }
        }
    }

    private static void RequireMatchingFile(
        IReadOnlyDictionary<string, ProjectManifestFile> indexed,
        string path,
        string sha256,
        long byteSize,
        string description)
    {
        if (!indexed.TryGetValue(path, out var file))
        {
            throw Integrity(
                $"The {description} file '{path}' is not present in the manifest file index.");
        }

        if (file.ByteSize != byteSize
            || !HashEquals(file.Sha256, sha256))
        {
            throw Integrity(
                $"The {description} file '{path}' does not match the metadata hash and size.");
        }
    }

    private async Task<(long ByteSize, string Sha256)> HashEntryAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > limits.MaxFileBytes)
            {
                throw TooLarge("A packaged file exceeds the file size limit.");
            }

            hash.AppendData(buffer, 0, read);
        }

        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[BufferSize];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw TooLarge("The package manifest exceeds the configured size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        return (unixMode & UnixFileTypeMask) == UnixSymbolicLink;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool HashEquals(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static ProjectTransferException Invalid(string message) =>
        new("import_package_invalid", message);

    private static ProjectTransferException PathInvalid(string message) =>
        new("import_path_invalid", message);

    private static ProjectTransferException Integrity(string message) =>
        new("import_integrity_failed", message);

    private static ProjectTransferException TooLarge(string message) =>
        new("import_too_large", message);
}

public sealed record ProjectPackageContents(
    ProjectTransferManifest Manifest,
    IReadOnlyList<ProjectPackageFile> Files);

public sealed record ProjectPackageFile(
    ZipArchiveEntry Entry,
    string Path,
    long ByteSize,
    string Sha256);
