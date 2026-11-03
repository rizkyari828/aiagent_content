using AIStudio.Domain.Approvals;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Domain.Scripts;
using AIStudio.Domain.Subtitles;
using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Transfer;

/// <summary>
/// Complete, consistent read of everything that belongs to one ContentProject. The
/// export workflow maps it to a manifest; the repository port keeps EF out of
/// Application.
/// </summary>
public sealed record ProjectTransferSnapshot(
    ContentProject Project,
    ReviewedScript? ReviewedScript,
    IReadOnlyList<Job> Jobs,
    IReadOnlyList<SceneAsset> SceneAssets,
    NarrationTrack? NarrationTrack,
    SubtitleTrack? SubtitleTrack,
    FinalApproval? FinalApproval,
    PublicationRecord? PublicationRecord,
    IReadOnlyList<MetricSnapshot> MetricSnapshots,
    IReadOnlyList<TimeEntry> TimeEntries,
    IReadOnlyList<CostEntry> CostEntries);

public sealed record ProjectImportCounts(
    int ReviewedScripts,
    int SourceJobs,
    int SceneAssets,
    int NarrationTracks,
    int SubtitleTracks,
    int FinalApprovals,
    int PublicationRecords,
    int MetricSnapshots,
    int TimeEntries,
    int CostEntries);

public sealed record ProjectImportResult(
    Guid ContentProjectId,
    int Files,
    ProjectImportCounts Counts);
