using System.Text.Json;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Transfer;

/// <summary>
/// Versioned, self-contained description of one ContentProject. The manifest names every
/// entity that is restored and every file that is packaged; media bytes live under
/// <c>files/&lt;relative asset path&gt;</c> and are addressed only by relative path plus
/// SHA-256. No filesystem absolute path, credential, log, model, or build artifact is
/// representable here.
/// </summary>
public sealed record ProjectTransferManifest(
    int SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    string? AppVersion,
    ProjectManifestProject Project,
    ProjectManifestReviewedScript? ReviewedScript,
    IReadOnlyList<ProjectManifestJob> SourceJobs,
    IReadOnlyList<ProjectManifestSceneAsset> SceneAssets,
    ProjectManifestMediaTrack? NarrationTrack,
    ProjectManifestMediaTrack? SubtitleTrack,
    ProjectManifestFinalApproval? FinalApproval,
    ProjectManifestPublication? PublicationRecord,
    IReadOnlyList<ProjectManifestMetricSnapshot> MetricSnapshots,
    IReadOnlyList<ProjectManifestTimeEntry> TimeEntries,
    IReadOnlyList<ProjectManifestCostEntry> CostEntries,
    IReadOnlyList<ProjectManifestFile> Files);

public sealed record ProjectManifestProject(
    Guid Id,
    string Title,
    string? Brief,
    ContentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectManifestReviewedScript(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    JsonElement Content,
    ScriptReviewStatus Status,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt);

/// <summary>
/// Terminal job history needed for referential integrity and for the A3 approval
/// evidence. Worker identity, leases, and error text are deliberately omitted.
/// </summary>
public sealed record ProjectManifestJob(
    Guid Id,
    Guid ContentProjectId,
    JobType Type,
    JobStatus Status,
    string InputVersionHash,
    int RetryCount,
    int MaxRetries,
    JsonElement Payload,
    JsonElement? Result,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record ProjectManifestSceneAsset(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    int SceneIndex,
    AssetType Type,
    string Path,
    long ByteSize,
    string ContentHash,
    AssetOrigin Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset CreatedAt);

public sealed record ProjectManifestMediaTrack(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    string Path,
    long ByteSize,
    string ContentHash,
    AssetOrigin Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt,
    DateTimeOffset CreatedAt);

public sealed record ProjectManifestFinalApproval(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceRenderJobId,
    string ArtifactPath,
    string ArtifactContentHash,
    long ArtifactByteSize,
    string Destination,
    string? Notes,
    string? ApproverLabel,
    int Revision,
    DateTimeOffset ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProjectManifestPublication(
    Guid Id,
    Guid ContentProjectId,
    Guid FinalApprovalId,
    int FinalApprovalRevision,
    string ArtifactContentHash,
    string Platform,
    string Url,
    string VideoId,
    DateTimeOffset PublishedAt,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record ProjectManifestMetricSnapshot(
    Guid Id,
    Guid ContentProjectId,
    Guid? PublicationRecordId,
    int? Views,
    int? Impressions,
    decimal? ClickThroughRate,
    decimal? AverageViewDurationSeconds,
    decimal? WatchTimeMinutes,
    int? SubscribersGained,
    DateTimeOffset CapturedAt,
    string Source,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record ProjectManifestTimeEntry(
    Guid Id,
    Guid ContentProjectId,
    TimeEntryKind Kind,
    string Category,
    decimal Minutes,
    DateTimeOffset RecordedAt,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record ProjectManifestCostEntry(
    Guid Id,
    Guid ContentProjectId,
    decimal Amount,
    string Currency,
    CostEntryKind Kind,
    string Category,
    DateTimeOffset IncurredAt,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record ProjectManifestFile(
    string Path,
    string Sha256,
    long ByteSize);
