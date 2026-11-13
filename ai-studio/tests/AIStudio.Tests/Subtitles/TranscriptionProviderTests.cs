using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Subtitles;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Narration;
using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Subtitles;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Subtitles;

public sealed class TranscriptionProviderTests : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        $"aistudio-transcribe-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task FasterWhisper_DisabledThrowsStableCode()
    {
        var provider = new FasterWhisperTranscriptionProvider(
            Options.Create(new TranscriptionOptions { Enabled = false }),
            new FakeProcessRunner((_, _) => new ProcessResult(0, string.Empty, string.Empty)),
            new RecordingGpuGate());

        var exception = await Assert.ThrowsAsync<TranscriptionException>(() =>
            provider.TranscribeAsync(
                new TranscriptionRequest("/tmp/missing.wav"),
                TestContext.Current.CancellationToken));

        Assert.False(provider.IsEnabled);
        Assert.Equal("transcription_disabled", exception.ErrorCode);
    }

    [Fact]
    public async Task FasterWhisper_MissingAudioThrowsInvalidInput()
    {
        var provider = new FasterWhisperTranscriptionProvider(
            Options.Create(new TranscriptionOptions { Enabled = true }),
            new FakeProcessRunner((_, _) => new ProcessResult(0, string.Empty, string.Empty)),
            new RecordingGpuGate());

        var exception = await Assert.ThrowsAsync<TranscriptionException>(() =>
            provider.TranscribeAsync(
                new TranscriptionRequest(Path.Combine(rootPath, "missing.wav")),
                TestContext.Current.CancellationToken));

        Assert.Equal("transcription_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public async Task FasterWhisper_AcquiresGpuGateAndReadsGeneratedSrt()
    {
        Directory.CreateDirectory(rootPath);
        var audioPath = Path.Combine(rootPath, "narration.wav");
        await File.WriteAllBytesAsync(audioPath, [1, 2, 3, 4], TestContext.Current.CancellationToken);

        var scriptPath = Path.Combine(rootPath, "transcribe.py");
        await File.WriteAllTextAsync(scriptPath, "# stub", TestContext.Current.CancellationToken);

        var gpuGate = new RecordingGpuGate();
        var runner = new FakeProcessRunner((request, _) =>
        {
            var reqIndex = Array.IndexOf(request.Arguments.ToArray(), "--request");
            var resIndex = Array.IndexOf(request.Arguments.ToArray(), "--result");
            var reqJson = File.ReadAllText(request.Arguments[reqIndex + 1]);
            using var doc = JsonDocument.Parse(reqJson);
            var outputPath = doc.RootElement.GetProperty("outputPath").GetString()!;

            File.WriteAllText(
                outputPath,
                "1\n00:00:00,000 --> 00:00:02,000\nHalo dunia.\n");
            File.WriteAllText(
                request.Arguments[resIndex + 1],
                """{"ok":true,"language":"id","segmentCount":1}""");
            return new ProcessResult(0, string.Empty, string.Empty);
        });

        var provider = new FasterWhisperTranscriptionProvider(
            Options.Create(new TranscriptionOptions
            {
                Enabled = true,
                FasterWhisper = new FasterWhisperOptions
                {
                    ScriptPath = scriptPath,
                    Model = "small",
                    DefaultLanguage = "id"
                }
            }),
            runner,
            gpuGate);

        var result = await provider.TranscribeAsync(
            new TranscriptionRequest(audioPath),
            TestContext.Current.CancellationToken);

        Assert.Equal([GpuWorkloads.Transcription], gpuGate.AcquiredWorkloads);
        Assert.True(gpuGate.Released);
        Assert.Equal("id", result.DetectedLanguage);
        Assert.Equal(1, result.SegmentCount);
        Assert.Contains("Halo dunia.", result.SrtContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranscribeEndpoint_PersistsSrtAndReturnsOk()
    {
        Directory.CreateDirectory(rootPath);
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var fileStore = new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = rootPath }));

        var narrationFile = await fileStore.WriteAsync(
            "audio/narration.wav",
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);

        var narration = NarrationTrack.Create(
            projectId,
            jobId,
            narrationFile.RelativePath,
            narrationFile.ByteSize,
            narrationFile.ContentHash,
            AssetOrigin.Local,
            source: "voxcpm2",
            creator: null,
            license: null,
            retrievedAt: null,
            DateTimeOffset.UtcNow);

        var subtitleRepo = new InMemorySubtitleRepository();
        var workflow = new SubtitleWorkflow(
            subtitleRepo,
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Demo", "Brief")),
            new StubJobReader(),
            fileStore,
            TimeProvider.System);

        var provider = new StubTranscriptionProvider(
            isEnabled: true,
            new TranscriptionResult(
                "1\n00:00:00,000 --> 00:00:02,000\nBaris satu.\n",
                "id",
                1));

        var result = await SubtitleEndpoints.TranscribeSubtitleAsync(
            projectId.ToString(),
            new TranscribeSubtitleRequest("id"),
            workflow,
            new StubNarrationRepository(narration),
            provider,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<SubtitleResponse>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("subtitles/generated.srt", ok.Value.Path);
        Assert.Equal("local", ok.Value.Origin);
        Assert.Equal("faster-whisper", ok.Value.Source);
        Assert.NotNull(subtitleRepo.Track);
    }

    [Fact]
    public async Task TranscribeEndpoint_Returns503WhenTranscriptionDisabled()
    {
        Directory.CreateDirectory(rootPath);
        var projectId = Guid.NewGuid();
        var fileStore = new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = rootPath }));

        var narration = NarrationTrack.Create(
            projectId,
            Guid.NewGuid(),
            "audio/narration.wav",
            10,
            new string('a', 64),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        var workflow = new SubtitleWorkflow(
            new InMemorySubtitleRepository(),
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Demo", "Brief")),
            new StubJobReader(),
            fileStore,
            TimeProvider.System);

        var result = await SubtitleEndpoints.TranscribeSubtitleAsync(
            projectId.ToString(),
            null,
            workflow,
            new StubNarrationRepository(narration),
            new StubTranscriptionProvider(isEnabled: false, result: null),
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(503, problem.StatusCode);
    }

    [Fact]
    public async Task TranscribeEndpoint_Returns404WhenNarrationMissing()
    {
        Directory.CreateDirectory(rootPath);
        var projectId = Guid.NewGuid();
        var fileStore = new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = rootPath }));

        var workflow = new SubtitleWorkflow(
            new InMemorySubtitleRepository(),
            new StubContentProjectReader(new ContentProjectSnapshot(projectId, "Demo", "Brief")),
            new StubJobReader(),
            fileStore,
            TimeProvider.System);

        var result = await SubtitleEndpoints.TranscribeSubtitleAsync(
            projectId.ToString(),
            null,
            workflow,
            new StubNarrationRepository(null),
            new StubTranscriptionProvider(isEnabled: true, result: null),
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private sealed class RecordingGpuGate : IGpuResourceGate
    {
        public List<string> AcquiredWorkloads { get; } = [];
        public bool Released { get; private set; }

        public ValueTask<IAsyncDisposable> AcquireAsync(
            string workloadName,
            CancellationToken cancellationToken)
        {
            AcquiredWorkloads.Add(workloadName);
            return ValueTask.FromResult<IAsyncDisposable>(new Lease(() => Released = true));
        }

        private sealed class Lease(Action onDispose) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                onDispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeProcessRunner(
        Func<ProcessRunRequest, CancellationToken, ProcessResult> handler)
        : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }

    private sealed class StubTranscriptionProvider(
        bool isEnabled,
        TranscriptionResult? result) : ITranscriptionProvider
    {
        public bool IsEnabled => isEnabled;

        public Task<TranscriptionResult> TranscribeAsync(
            TranscriptionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(result!);
    }

    private sealed class InMemorySubtitleRepository : ISubtitleRepository
    {
        public SubtitleTrack? Track { get; private set; }

        public Task<SubtitleTrack?> FindByProjectIdAsync(
            Guid contentProjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Track?.ContentProjectId == contentProjectId ? Track : null);

        public void Add(SubtitleTrack track) => Track = track;

        public Task UpdateAsync(SubtitleTrack track, CancellationToken cancellationToken)
        {
            Track = track;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(1);
    }

    private sealed class StubContentProjectReader(ContentProjectSnapshot? project)
        : IContentProjectReader
    {
        public Task<ContentProjectSnapshot?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(project?.Id == id ? project : null);
    }

    private sealed class StubJobReader : IJobReader
    {
        public Task<JobSnapshot?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<JobSnapshot?>(null);
    }

    private sealed class StubNarrationRepository(NarrationTrack? track)
        : INarrationRepository
    {
        public Task<NarrationTrack?> FindByProjectIdAsync(
            Guid contentProjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(track?.ContentProjectId == contentProjectId ? track : null);

        public void Add(NarrationTrack trackToAdd)
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
