using System.Text.Json;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateAudio;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Application.Rendering.AudioMixing;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GenerateAudioJobHandlerTests : IDisposable
{
    private readonly string root;

    public GenerateAudioJobHandlerTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-generate-audio-tests",
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
    public async Task ExecuteAsync_GeneratesAllThreeStages()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var speech = new FakeSpeechSynthesisProvider(root);
        var music = new FakeMusicGenerationProvider(root);
        var mixer = new FakeAudioMixer(root);
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        var result = await RunAsync(handler, projectId, storyboard.Id);

        Assert.Equal(storyboard.Id, result.StoryboardJobId);
        Assert.False(result.NarrationReused);
        Assert.False(result.MusicReused);
        Assert.False(result.MasterReused);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, music.CallCount);
        Assert.Equal(1, mixer.CallCount);

        Assert.Contains("Local AI Storyboard", speech.Requests[0].Text);
        Assert.Contains("Why local AI", speech.Requests[0].Text);
        Assert.Equal(90, music.Requests[0].Bpm);
        Assert.Equal(42, music.Requests[0].Seed);
        Assert.True(music.Requests[0].Instrumental);
        Assert.Equal(12, music.Requests[0].DurationSeconds);

        Assert.Equal(48000, result.Narration.SampleRate);
        Assert.Equal(1, result.Narration.Channels);
        Assert.Equal(2, result.Master.Channels);
        Assert.Equal(AudioProductionWorkspace.NarrationFileName, Path.GetFileName(result.Narration.Path));
        Assert.Equal(AudioProductionWorkspace.MasterFileName, Path.GetFileName(result.Master.Path));
    }

    [Fact]
    public async Task ExecuteAsync_ReusesEveryStageOnRetry()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var speech = new FakeSpeechSynthesisProvider(root);
        var music = new FakeMusicGenerationProvider(root);
        var mixer = new FakeAudioMixer(root);
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        await RunAsync(handler, projectId, storyboard.Id);
        var second = await RunAsync(handler, projectId, storyboard.Id);

        Assert.True(second.NarrationReused);
        Assert.True(second.MusicReused);
        Assert.True(second.MasterReused);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, music.CallCount);
        Assert.Equal(1, mixer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_MusicFailureThenRetryReusesNarration()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var speech = new FakeSpeechSynthesisProvider(root);
        var music = new FakeMusicGenerationProvider(root, enabled: false);
        var mixer = new FakeAudioMixer(root);
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        var failure = await Assert.ThrowsAsync<JobExecutionException>(
            () => RunAsync(handler, projectId, storyboard.Id));
        Assert.Equal("audio_music_failed", failure.ErrorCode);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(0, mixer.CallCount);

        music.IsEnabled = true;
        var retry = await RunAsync(handler, projectId, storyboard.Id);

        Assert.True(retry.NarrationReused);
        Assert.False(retry.MusicReused);
        Assert.False(retry.MasterReused);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, music.CallCount);
        Assert.Equal(1, mixer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_MixFailureThenRetryReusesNarrationAndMusic()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var speech = new FakeSpeechSynthesisProvider(root);
        var music = new FakeMusicGenerationProvider(root);
        var mixer = new FakeAudioMixer(root)
        {
            Handler = _ => throw new AudioMixException("audio_mix_failed", "synthetic mix failure")
        };
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        var failure = await Assert.ThrowsAsync<JobExecutionException>(
            () => RunAsync(handler, projectId, storyboard.Id));
        Assert.Equal("audio_mix_failed", failure.ErrorCode);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, music.CallCount);

        mixer.Handler = null;
        var retry = await RunAsync(handler, projectId, storyboard.Id);

        Assert.True(retry.NarrationReused);
        Assert.True(retry.MusicReused);
        Assert.False(retry.MasterReused);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, music.CallCount);
        // Only the failed mix stage is retried.
        Assert.Equal(2, mixer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RegeneratesOnlyMissingStage()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var speech = new FakeSpeechSynthesisProvider(root);
        var music = new FakeMusicGenerationProvider(root);
        var mixer = new FakeAudioMixer(root);
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        await RunAsync(handler, projectId, storyboard.Id);
        File.Delete(Path.Combine(
            root,
            AudioProductionWorkspace.FilePath(
                projectId,
                storyboard.Id,
                AudioProductionWorkspace.NarrationFileName)));

        var retry = await RunAsync(handler, projectId, storyboard.Id);

        // Same narration bytes -> same hash -> music and master stay valid.
        Assert.False(retry.NarrationReused);
        Assert.True(retry.MusicReused);
        Assert.True(retry.MasterReused);
        Assert.Equal(2, speech.CallCount);
        Assert.Equal(1, music.CallCount);
        Assert.Equal(1, mixer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ChangedVoiceInvalidatesNarrationAndMixOnly()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var counter = 0;
        var speech = new FakeSpeechSynthesisProvider(root)
        {
            BytesFactory = () => [1, 2, (byte)Interlocked.Increment(ref counter)]
        };
        var music = new FakeMusicGenerationProvider(root);
        var mixer = new FakeAudioMixer(root);
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        await RunAsync(handler, projectId, storyboard.Id, voice: "Formal");
        var retry = await RunAsync(handler, projectId, storyboard.Id, voice: "Playful");

        Assert.False(retry.NarrationReused);
        Assert.True(retry.MusicReused);
        Assert.False(retry.MasterReused);
        Assert.Equal(2, speech.CallCount);
        Assert.Equal(1, music.CallCount);
        Assert.Equal(2, mixer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DisabledSpeechFailsNarrationStage()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var speech = new FakeSpeechSynthesisProvider(root, enabled: false);
        var music = new FakeMusicGenerationProvider(root);
        var mixer = new FakeAudioMixer(root);
        var handler = CreateHandler(projectId, storyboard, speech, music, mixer);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => RunAsync(handler, projectId, storyboard.Id));

        Assert.Equal("audio_narration_failed", exception.ErrorCode);
        Assert.Equal(0, music.CallCount);
        Assert.Equal(0, mixer.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_MapsStageExceptionsToStageCodes()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);

        var speech = new FakeSpeechSynthesisProvider(root)
        {
            Handler = _ => throw new SpeechSynthesisException("speech_generation_failed", "boom")
        };
        var narrationFailure = await Assert.ThrowsAsync<JobExecutionException>(
            () => RunAsync(
                CreateHandler(
                    projectId,
                    storyboard,
                    speech,
                    new FakeMusicGenerationProvider(root),
                    new FakeAudioMixer(root)),
                projectId,
                storyboard.Id));
        Assert.Equal("audio_narration_failed", narrationFailure.ErrorCode);

        var music = new FakeMusicGenerationProvider(root)
        {
            Handler = _ => throw new MusicGenerationException("music_generation_failed", "boom")
        };
        var musicFailure = await Assert.ThrowsAsync<JobExecutionException>(
            () => RunAsync(
                CreateHandler(
                    projectId,
                    storyboard,
                    new FakeSpeechSynthesisProvider(root),
                    music,
                    new FakeAudioMixer(root)),
                projectId,
                storyboard.Id));
        Assert.Equal("audio_music_failed", musicFailure.ErrorCode);

        var mixer = new FakeAudioMixer(root)
        {
            Handler = _ => throw new AudioMixException("audio_mix_failed", "boom")
        };
        var mixFailure = await Assert.ThrowsAsync<JobExecutionException>(
            () => RunAsync(
                CreateHandler(
                    projectId,
                    storyboard,
                    new FakeSpeechSynthesisProvider(root),
                    new FakeMusicGenerationProvider(root),
                    mixer),
                projectId,
                storyboard.Id));
        Assert.Equal("audio_mix_failed", mixFailure.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new FakeSpeechSynthesisProvider(root),
            new FakeMusicGenerationProvider(root),
            new FakeAudioMixer(root));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.ExecuteAsync(
                CreateJob(projectId, storyboard.Id),
                cancellation.Token));

        Assert.False(Directory.Exists(Path.Combine(
            root,
            AudioProductionWorkspace.Directory(projectId, storyboard.Id))));
    }

    [Fact]
    public void Handler_DoesNotTakeTheGpuGate()
    {
        var constructor = Assert.Single(typeof(GenerateAudioJobHandler).GetConstructors());
        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => typeof(IGpuResourceGate).IsAssignableFrom(parameter.ParameterType));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsWrongJobType()
    {
        var projectId = Guid.NewGuid();
        var storyboard = Storyboard(projectId);
        var handler = CreateHandler(
            projectId,
            storyboard,
            new FakeSpeechSynthesisProvider(root),
            new FakeMusicGenerationProvider(root),
            new FakeAudioMixer(root));
        var wrong = new ClaimedJob(
            Guid.NewGuid(),
            projectId,
            JobType.RenderVideo,
            "input",
            "{}",
            0,
            2,
            false);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(wrong, TestContext.Current.CancellationToken));

        Assert.Equal("audio_wrong_job_type", exception.ErrorCode);
    }

    private static JobSnapshot Storyboard(Guid projectId) =>
        AssetTestData.StoryboardJob(projectId, GenerateStoryboardTestData.ValidResult);

    private async Task<GenerateAudioResult> RunAsync(
        GenerateAudioJobHandler handler,
        Guid projectId,
        Guid storyboardJobId,
        string voice = "Formal") =>
        GenerateAudioResult.Deserialize(
            await handler.ExecuteAsync(
                CreateJob(projectId, storyboardJobId, voice),
                TestContext.Current.CancellationToken));

    private static ClaimedJob CreateJob(
        Guid projectId,
        Guid storyboardJobId,
        string voice = "Formal") =>
        new(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateAudio,
            "input-v1",
            JsonSerializer.Serialize(
                new GenerateAudioJobPayload(projectId, storyboardJobId, voice),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            0,
            2,
            false);

    private GenerateAudioJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot storyboard,
        FakeSpeechSynthesisProvider speech,
        FakeMusicGenerationProvider music,
        FakeAudioMixer mixer)
    {
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var workspace = new AudioProductionWorkspace(
            new LocalAssetFileStore(storage),
            new FakeAudioInspector());

        return new GenerateAudioJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            workspace,
            speech,
            music,
            mixer);
    }
}
