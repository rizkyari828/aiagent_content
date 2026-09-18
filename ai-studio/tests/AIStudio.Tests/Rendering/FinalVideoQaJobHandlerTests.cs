using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FinalVideoQaJobHandlerTests : IDisposable
{
    private readonly string root;

    public FinalVideoQaJobHandlerTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-final-qa-handler-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Handler_PassesForValidArtifactWithSubtitle()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var relativePath = $"renders/{projectId:N}/video.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes),
            subtitleTrackId: Guid.NewGuid(),
            subtitleContentHash: new string('e', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(
                new MediaInspection(
                    DurationSeconds: 12.5,
                    HasVideo: true,
                    HasAudio: true,
                    HasSubtitle: true,
                    Width: 640,
                    Height: 480)));

        var json = await handler.ExecuteAsync(
            FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
            TestContext.Current.CancellationToken);

        var result = FinalVideoQaResult.Deserialize(json);
        Assert.Equal(relativePath, result.OutputPath);
        Assert.Equal(FinalVideoQaTestData.Hash(bytes), result.ContentHash);
        Assert.Equal(12.5, result.DurationSeconds);
        Assert.Equal(640, result.Width);
        Assert.Equal(480, result.Height);
        Assert.True(result.HasVideo);
        Assert.True(result.HasAudio);
        Assert.True(result.HasSubtitle);
    }

    [Fact]
    public async Task Handler_RejectsMissingArtifact()
    {
        var projectId = Guid.NewGuid();
        var renderResult = FinalVideoQaTestData.RenderResult(
            "renders/missing/video.mp4",
            new string('d', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(ValidInspection()));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_artifact_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsTamperedHash()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var relativePath = "renders/tampered.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            new string('d', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(ValidInspection()));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_hash_mismatch", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_HandlesProbeFailure()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var relativePath = "renders/probe-failure.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Failing(ProcessExecutionException.MediaProbeFailed));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_probe_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsInvalidDuration()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var relativePath = "renders/duration.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(
                new MediaInspection(
                    DurationSeconds: 0,
                    HasVideo: true,
                    HasAudio: true,
                    HasSubtitle: true,
                    Width: 640,
                    Height: 480)));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_duration_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingVideoStream()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var relativePath = "renders/no-video.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(
                new MediaInspection(
                    DurationSeconds: 12,
                    HasVideo: false,
                    HasAudio: true,
                    HasSubtitle: false,
                    Width: 0,
                    Height: 0)));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_video_stream_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingAudioStream()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var relativePath = "renders/no-audio.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(
                new MediaInspection(
                    DurationSeconds: 12,
                    HasVideo: true,
                    HasAudio: false,
                    HasSubtitle: false,
                    Width: 640,
                    Height: 480)));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_audio_stream_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsMissingExpectedSubtitleStream()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3 };
        var relativePath = "renders/no-subtitle.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes),
            subtitleTrackId: Guid.NewGuid(),
            subtitleContentHash: new string('e', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(
                new MediaInspection(
                    DurationSeconds: 12,
                    HasVideo: true,
                    HasAudio: true,
                    HasSubtitle: false,
                    Width: 640,
                    Height: 480)));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_subtitle_stream_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_PassesForBurnedInSubtitleWithoutStream()
    {
        var projectId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var relativePath = $"renders/{projectId:N}/burned-subtitle.mp4";
        WriteFile(relativePath, bytes);
        var renderResult = FinalVideoQaTestData.RenderResult(
            relativePath,
            FinalVideoQaTestData.Hash(bytes),
            subtitleTrackId: Guid.NewGuid(),
            subtitleContentHash: new string('e', RenderVideoResult.ContentHashLength),
            subtitleBurnedIn: true);
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(
                new MediaInspection(
                    DurationSeconds: 12,
                    HasVideo: true,
                    HasAudio: true,
                    HasSubtitle: false,
                    Width: 1280,
                    Height: 720)));

        var json = await handler.ExecuteAsync(
            FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
            TestContext.Current.CancellationToken);

        var result = FinalVideoQaResult.Deserialize(json);
        Assert.False(result.HasSubtitle);
    }

    [Fact]
    public async Task Handler_RejectsMissingRenderJob()
    {
        var projectId = Guid.NewGuid();
        var handler = CreateHandler(
            renderJob: null,
            FakeMediaInspector.Returning(ValidInspection()));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, Guid.NewGuid()),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_render_job_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Handler_RejectsUnfinishedRenderJob()
    {
        var projectId = Guid.NewGuid();
        var renderResult = FinalVideoQaTestData.RenderResult(
            "renders/unfinished.mp4",
            new string('d', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(
            projectId,
            renderResult,
            status: AIStudio.Domain.Jobs.JobStatus.Running);
        var handler = CreateHandler(
            renderJob,
            FakeMediaInspector.Returning(ValidInspection()));

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                FinalVideoQaTestData.ClaimedQaJob(projectId, renderJob.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_render_job_invalid", exception.ErrorCode);
    }

    private static MediaInspection ValidInspection() =>
        new(
            DurationSeconds: 12,
            HasVideo: true,
            HasAudio: true,
            HasSubtitle: false,
            Width: 640,
            Height: 480);

    private FinalVideoQaJobHandler CreateHandler(
        JobSnapshot? renderJob,
        IMediaInspector mediaInspector) =>
        new(
            new StubJobReader(renderJob),
            new LocalAssetFileStore(
                Options.Create(new AssetStorageOptions { RootPath = root })),
            mediaInspector);

    private void WriteFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
