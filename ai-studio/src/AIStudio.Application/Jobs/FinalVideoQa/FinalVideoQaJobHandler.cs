using AIStudio.Application.Assets;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.FinalVideoQa;

public sealed class FinalVideoQaJobHandler(
    IJobReader jobs,
    IAssetFileStore fileStore,
    IMediaInspector mediaInspector) : IJobHandler
{
    public bool CanHandle(JobType type) => type == JobType.FinalVideoQa;

    public async Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();

        if (job.Type != JobType.FinalVideoQa)
        {
            throw new JobExecutionException(
                "final_video_qa_wrong_job_type",
                $"FinalVideoQa handler cannot execute job type {job.Type}.");
        }

        var payload = FinalVideoQaJobPayload.Deserialize(job.Payload);
        if (payload.ContentProjectId != job.ContentProjectId)
        {
            throw new JobExecutionException(
                "final_video_qa_invalid_payload",
                "FinalVideoQa payload contentProjectId does not match the claimed job.");
        }

        var renderResult = await ReadRenderResultAsync(
            payload.ContentProjectId,
            payload.RenderJobId,
            cancellationToken);

        AssetFileInfo artifact;
        try
        {
            artifact = fileStore.Register(renderResult.OutputPath);
        }
        catch (AssetCollectionException exception)
        {
            throw new JobExecutionException(
                MapArtifactErrorCode(exception.ErrorCode),
                exception.Message,
                exception);
        }

        if (!string.Equals(
            artifact.ContentHash,
            renderResult.ContentHash,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new JobExecutionException(
                "qa_hash_mismatch",
                $"'{renderResult.OutputPath}' does not match the recorded render hash.");
        }

        var inspection = await InspectAsync(artifact.AbsolutePath, cancellationToken);

        if (inspection.DurationSeconds <= 0)
        {
            throw new JobExecutionException(
                "qa_duration_invalid",
                "The rendered output duration must be greater than zero.");
        }

        if (!inspection.HasVideo)
        {
            throw new JobExecutionException(
                "qa_video_stream_missing",
                "The rendered output is missing a video stream.");
        }

        if (!inspection.HasAudio)
        {
            throw new JobExecutionException(
                "qa_audio_stream_missing",
                "The rendered output is missing an audio stream.");
        }

        if (inspection.Width <= 0 || inspection.Height <= 0)
        {
            throw new JobExecutionException(
                "qa_resolution_invalid",
                "The rendered output video dimensions are not valid.");
        }

        // Burned-in subtitles are pixels, not a stream; only embedded subtitles are probed.
        if (renderResult.SubtitleTrackId is not null
            && !renderResult.SubtitleBurnedIn
            && !inspection.HasSubtitle)
        {
            throw new JobExecutionException(
                "qa_subtitle_stream_missing",
                "The rendered output is missing the expected subtitle stream.");
        }

        var result = new FinalVideoQaResult(
            artifact.RelativePath,
            artifact.ContentHash,
            inspection.DurationSeconds,
            inspection.Width,
            inspection.Height,
            inspection.HasVideo,
            inspection.HasAudio,
            inspection.HasSubtitle);

        return result.Serialize();
    }

    private async Task<RenderVideoResult> ReadRenderResultAsync(
        Guid contentProjectId,
        Guid renderJobId,
        CancellationToken cancellationToken)
    {
        var renderJob = await jobs.FindByIdAsync(renderJobId, cancellationToken);
        if (renderJob is null || renderJob.ContentProjectId != contentProjectId)
        {
            throw new JobExecutionException(
                "qa_render_job_not_found",
                $"RenderVideo job '{renderJobId}' does not exist for this content project.");
        }

        if (renderJob.Type != JobType.RenderVideo
            || renderJob.Status != JobStatus.Succeeded
            || renderJob.Result is null)
        {
            throw new JobExecutionException(
                "qa_render_job_invalid",
                "Final QA requires a completed RenderVideo job result.");
        }

        try
        {
            return RenderVideoResult.Deserialize(renderJob.Result);
        }
        catch (JobExecutionException exception)
        {
            throw new JobExecutionException(
                "qa_render_result_invalid",
                "The RenderVideo result is not a valid structured result.",
                exception);
        }
    }

    private async Task<MediaInspection> InspectAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mediaInspector.InspectAsync(absolutePath, cancellationToken);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new JobExecutionException(
                "qa_probe_unavailable",
                "ffprobe is not available to inspect the rendered output.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new JobExecutionException(
                "qa_probe_timeout",
                "Probing the rendered output timed out.",
                exception);
        }
        catch (ProcessExecutionException exception)
        {
            throw new JobExecutionException(
                "qa_probe_failed",
                "The rendered output could not be decoded or probed.",
                exception);
        }
    }

    private static string MapArtifactErrorCode(string errorCode) =>
        errorCode switch
        {
            "asset_file_not_found" => "qa_artifact_missing",
            "asset_file_unreadable" => "qa_artifact_unreadable",
            _ => "qa_artifact_invalid"
        };
}
