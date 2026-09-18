using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Subtitles;

namespace AIStudio.Application.Subtitles;

public sealed class SubtitleWorkflow(
    ISubtitleRepository subtitles,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    IAssetFileStore fileStore,
    TimeProvider timeProvider)
{
    public const string SupportedExtension = ".srt";

    public async Task<SubtitleSnapshot?> RegisterAsync(
        Guid contentProjectId,
        Guid jobId,
        RegisterSubtitle request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(jobId, nameof(jobId));

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        await RequireStoryboardAsync(contentProjectId, jobId, cancellationToken);

        var existing = await subtitles.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (existing is not null)
        {
            throw Error(
                "subtitle_conflict",
                $"Content project '{contentProjectId}' already has a subtitle track.");
        }

        if (!string.Equals(
            Path.GetExtension(request.Path),
            SupportedExtension,
            StringComparison.OrdinalIgnoreCase))
        {
            throw Error(
                "subtitle_format_unsupported",
                $"Only '{SupportedExtension}' subtitle files are supported.");
        }

        if (request.Origin == AssetOrigin.External
            && (string.IsNullOrWhiteSpace(request.Source)
                || string.IsNullOrWhiteSpace(request.License)))
        {
            throw Error(
                "subtitle_provenance_required",
                "Externally sourced subtitles require a source and license.");
        }

        AssetFileInfo file;
        try
        {
            file = fileStore.Register(request.Path);
        }
        catch (AssetCollectionException exception)
        {
            throw Error(exception.ErrorCode, exception.Message, exception);
        }

        var track = SubtitleTrack.Create(
            contentProjectId,
            jobId,
            file.RelativePath,
            file.ByteSize,
            file.ContentHash,
            request.Origin,
            request.Source,
            request.Creator,
            request.License,
            request.RetrievedAt,
            timeProvider.GetUtcNow());

        subtitles.Add(track);
        await subtitles.SaveChangesAsync(cancellationToken);
        return ToSnapshot(track);
    }

    public async Task<SubtitleSnapshot?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        var track = await subtitles.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        return track is null ? null : ToSnapshot(track);
    }

    private async Task RequireStoryboardAsync(
        Guid contentProjectId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await jobs.FindByIdAsync(jobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw Error(
                "subtitle_storyboard_not_found",
                $"GenerateStoryboard job '{jobId}' does not exist for this content project.");
        }

        if (job.Type != JobType.GenerateStoryboard
            || job.Status != JobStatus.Succeeded
            || job.Result is null)
        {
            throw Error(
                "subtitle_storyboard_invalid",
                "Subtitles require a completed GenerateStoryboard job result.");
        }

        try
        {
            GenerateStoryboardResult.Deserialize(job.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "subtitle_storyboard_invalid",
                "The storyboard result is not a valid structured storyboard.",
                exception);
        }
    }

    private static SubtitleSnapshot ToSnapshot(SubtitleTrack track) =>
        new(
            track.Id,
            track.ContentProjectId,
            track.SourceJobId,
            track.Path,
            track.ByteSize,
            track.ContentHash,
            track.Origin,
            track.Source,
            track.Creator,
            track.License,
            track.RetrievedAt,
            track.CreatedAt);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static SubtitleException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}

public sealed record RegisterSubtitle(
    string Path,
    AssetOrigin Origin,
    string? Source,
    string? Creator,
    string? License,
    DateTimeOffset? RetrievedAt);
