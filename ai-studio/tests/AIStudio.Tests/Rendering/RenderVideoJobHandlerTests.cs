using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Application.Rendering.Narration;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Narration;
using AIStudio.Domain.Subtitles;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using AIStudio.Tests.Subtitles;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class RenderVideoJobHandlerTests : IDisposable
{
    private readonly string root;

    public RenderVideoJobHandlerTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-render-handler-tests",
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
    public async Task Handler_RendersWhenInputsMatchRecordedHashes()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1, 2, 3 };
        var scene1 = new byte[] { 4, 5 };
        var narrationBytes = new byte[] { 6, 7, 8, 9 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var narration = RenderVideoTestData.Narration(
            projectId,
            storyboard.Id,
            "narration.wav",
            narrationBytes);
        var outputBytes = new byte[] { 42, 42, 42 };
        var processRunner = FakeFfmpeg.WritingOutput(outputBytes);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            narration,
            processRunner);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal($"renders/{projectId:N}/{storyboard.Id:N}.mp4", result.OutputPath);
        Assert.Equal(RenderVideoTestData.Hash(outputBytes), result.ContentHash);
        Assert.Equal(outputBytes.Length, result.ByteSize);
        Assert.Equal(2, result.SceneCount);
        Assert.Equal(storyboard.Id, result.StoryboardJobId);
        Assert.Equal(narration.Id, result.NarrationTrackId);
        Assert.Equal(RenderVideoTestData.Hash(narrationBytes), result.NarrationContentHash);
        Assert.True(File.Exists(Path.Combine(
            root,
            "renders",
            projectId.ToString("N"),
            $"{storyboard.Id:N}.mp4")));
    }

    [Fact]
    public async Task Handler_RejectsChangedAssetHash()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var bytes = new byte[] { 1, 2, 3 };
        WriteFile("scene-0.png", bytes);
        var processRunner = new FakeProcessRunner();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(
                    projectId,
                    storyboard.Id,
                    0,
                    "scene-0.png",
                    bytes,
                    contentHash: new string('a', 64)),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", [4])
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [5]),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsChangedNarrationHash()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", [3, 3]);
        var processRunner = new FakeProcessRunner();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                [3, 3],
                contentHash: new string('b', 64)),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_narration_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsMissingAssetFile()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var processRunner = new FakeProcessRunner();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "missing.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "also-missing.png", [2])
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [3]),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_unreadable", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsIncompleteSceneAssets()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var processRunner = new FakeProcessRunner();
        WriteFile("scene-0.png", [1]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", [1])],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", [3]),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_assets_incomplete", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_RejectsMissingNarration()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var processRunner = new FakeProcessRunner();
        WriteFile("scene-0.png", [1]);
        WriteFile("scene-1.png", [2]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", [2])
            ],
            narration: null,
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_audio_missing", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_MapsNonZeroRendererExitToJobErrorCode()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var processRunner = FakeFfmpeg.FailingRender("synthetic failure");
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                narrationBytes),
            processRunner);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_failed", exception.ErrorCode);
        Assert.Equal(2, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_IncludesMatchingSubtitle()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var storyboardScenes = GenerateStoryboardResult
            .Deserialize(GenerateStoryboardTestData.ValidResult)
            .Scenes;
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        var subtitleBytes =
            "1\n00:00:00,000 --> 00:00:02,000\nHello\n\n2\n00:00:02,000 --> 00:00:04,000\nWorld\n"u8.ToArray();
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        WriteFile("subtitle.srt", subtitleBytes);
        var subtitle = SubtitleTrack.Create(
            projectId,
            storyboard.Id,
            "subtitle.srt",
            subtitleBytes.Length,
            RenderVideoTestData.Hash(subtitleBytes),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);
        var canonicalSubtitle = Path.Combine(root, "subtitle.srt");
        string? derivedSubtitlePath = null;
        string? derivedSubtitle = null;
        var processRunner = FakeFfmpeg.WritingOutput(
            [42, 42],
            onFfmpeg: request =>
            {
                derivedSubtitlePath = SubtitlePath(Filter(request.Arguments));
                derivedSubtitle = derivedSubtitlePath is null
                    ? null
                    : File.ReadAllText(derivedSubtitlePath);
            });
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                narrationBytes),
            processRunner,
            subtitle);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal(subtitle.Id, result.SubtitleTrackId);
        Assert.Equal(RenderVideoTestData.Hash(subtitleBytes), result.SubtitleContentHash);
        Assert.True(result.SubtitleBurnedIn);

        var ffmpegArguments = processRunner.Requests[1].Arguments;
        var filter = Filter(ffmpegArguments);
        Assert.Contains("subtitles=", filter);
        Assert.DoesNotContain("mov_text", ffmpegArguments);

        // Durable rendering re-times the canonical cues from SceneTiming and burns
        // that derived file in; the canonical subtitle asset must stay untouched.
        Assert.NotNull(derivedSubtitle);
        Assert.NotEqual(canonicalSubtitle, derivedSubtitlePath);
        Assert.Contains("Hello", derivedSubtitle);
        Assert.Contains("World", derivedSubtitle);
        Assert.DoesNotContain("Why local AI", derivedSubtitle);
        var contentDurations = FfmpegCommandPlan.ResolveContentDurations(
            [
                new SceneMediaInput(
                    Path.Combine(root, "scene-0.png"),
                    AssetType.Image,
                    Weight: SceneTiming.WeightFor(
                        storyboardScenes[0].Heading,
                        storyboardScenes[0].Visual)),
                new SceneMediaInput(
                    Path.Combine(root, "scene-1.png"),
                    AssetType.Image,
                    Weight: SceneTiming.WeightFor(
                        storyboardScenes[1].Heading,
                        storyboardScenes[1].Visual))
            ],
            12);
        Assert.Contains(SubtitleTimeline.Timestamp(contentDurations[0]), derivedSubtitle);
        Assert.Equal(subtitleBytes, File.ReadAllBytes(canonicalSubtitle));
    }

    [Fact]
    public async Task Handler_RejectsChangedSubtitleHash()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        WriteFile("subtitle.srt", [9, 9]);
        var subtitle = SubtitleTrack.Create(
            projectId,
            storyboard.Id,
            "subtitle.srt",
            2,
            new string('d', SubtitleTrack.ContentHashLength),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);
        var processRunner = new FakeProcessRunner();
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                narrationBytes),
            processRunner,
            subtitle);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.ExecuteAsync(
                RenderVideoTestData.CreateJob(projectId, storyboard.Id),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_subtitle_hash_mismatch", exception.ErrorCode);
        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public async Task Handler_IgnoresSubtitleFromAnotherStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        WriteFile("subtitle.srt", [9]);
        var subtitle = SubtitleTrack.Create(
            projectId,
            Guid.NewGuid(),
            "subtitle.srt",
            1,
            RenderVideoTestData.Hash([9]),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            AssetTestData.Now);
        var processRunner = FakeFfmpeg.WritingOutput([42]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(
                projectId,
                storyboard.Id,
                "narration.wav",
                narrationBytes),
            processRunner,
            subtitle);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Null(result.SubtitleTrackId);
        Assert.Null(result.SubtitleContentHash);
        Assert.DoesNotContain("mov_text", processRunner.Requests[1].Arguments);
    }

    [Fact]
    public async Task Handler_TreatsGeneratedVisualsAsGraphicForTransitions()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var processRunner = FakeFfmpeg.WritingOutput([42, 42]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(
                    projectId,
                    storyboard.Id,
                    0,
                    "scene-0.png",
                    scene0,
                    source: SceneAssetProvenance.GeneratedVisualSource),
                RenderVideoTestData.SceneAsset(
                    projectId,
                    storyboard.Id,
                    1,
                    "scene-1.png",
                    scene1,
                    source: SceneAssetProvenance.GeneratedVisualSource)
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", narrationBytes),
            processRunner);

        await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var filter = Filter(processRunner);
        Assert.Contains("xfade=transition=fadeblack", filter);
        Assert.DoesNotContain("xfade=transition=fade:", filter);
    }

    [Fact]
    public async Task Handler_KeepsCrossfadeForManualAssets()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var processRunner = FakeFfmpeg.WritingOutput([42, 42]);
        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            RenderVideoTestData.Narration(projectId, storyboard.Id, "narration.wav", narrationBytes),
            processRunner);

        await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var filter = Filter(processRunner);
        Assert.Contains("xfade=transition=fade:", filter);
        Assert.DoesNotContain("fadeblack", filter);
    }

    [Fact]
    public async Task Handler_UsesMasteredAudioWhenWorkspaceHasValidMaster()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        var masterBytes = new byte[] { 9, 9, 9 };
        var masterRelative = AudioProductionWorkspace.FilePath(
            projectId,
            storyboard.Id,
            AudioProductionWorkspace.MasterFileName);
        WriteFile(masterRelative, masterBytes);
        var processRunner = FakeFfmpeg.WritingOutput([42, 42]);
        var workspace = await SeedMasterAsync(
            processRunner,
            projectId,
            storyboard.Id,
            masterRelative,
            masterBytes);

        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            narration: null,
            processRunner,
            workspace: workspace);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal(RenderAudioSources.Mastered, result.AudioSource);
        Assert.Equal(masterRelative, result.MasteredAudioPath);
        Assert.Equal(RenderVideoTestData.Hash(masterBytes), result.MasteredAudioContentHash);
        Assert.Null(result.NarrationTrackId);
        Assert.Null(result.NarrationContentHash);

        var ffmpegRequest = Assert.Single(
            processRunner.Requests,
            request => request.FileName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Path.Combine(root, masterRelative), ffmpegRequest.Arguments);
    }

    [Fact]
    public async Task Handler_FallsBackToNarrationWhenMasterFileIsMissing()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        var narrationBytes = new byte[] { 3 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        WriteFile("narration.wav", narrationBytes);
        var narration = RenderVideoTestData.Narration(
            projectId,
            storyboard.Id,
            "narration.wav",
            narrationBytes);
        var processRunner = FakeFfmpeg.WritingOutput([42, 42]);
        var masterRelative = AudioProductionWorkspace.FilePath(
            projectId,
            storyboard.Id,
            AudioProductionWorkspace.MasterFileName);
        // Manifest points at a master that no longer exists on disk.
        var workspace = await SeedMasterAsync(
            processRunner,
            projectId,
            storyboard.Id,
            masterRelative,
            [9, 9, 9],
            writeMasterFile: false);

        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            narration,
            processRunner,
            workspace: workspace);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal(RenderAudioSources.Narration, result.AudioSource);
        Assert.Equal(narration.Id, result.NarrationTrackId);
        Assert.Equal(RenderVideoTestData.Hash(narrationBytes), result.NarrationContentHash);
    }

    private async Task<AudioProductionWorkspace> SeedMasterAsync(
        IProcessRunner processRunner,
        Guid projectId,
        Guid storyboardJobId,
        string masterRelative,
        byte[] masterBytes,
        bool writeMasterFile = true)
    {
        if (writeMasterFile)
        {
            WriteFile(masterRelative, masterBytes);
        }

        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var workspace = new AudioProductionWorkspace(
            new LocalAssetFileStore(storage),
            new FfprobeMediaInspector(
                Options.Create(new RenderingOptions()),
                processRunner));

        await workspace.SaveManifestAsync(
            projectId,
            storyboardJobId,
            new AudioProductionManifest(
                AudioProductionWorkspace.Version,
                new Dictionary<string, AudioProductionStage>
                {
                    [AudioProductionWorkspace.MasterStage] = new AudioProductionStage(
                        "master-fingerprint",
                        masterRelative,
                        RenderVideoTestData.Hash(masterBytes),
                        masterBytes.Length,
                        12,
                        0,
                        0)
                }),
            TestContext.Current.CancellationToken);

        return workspace;
    }

    [Fact]
    public async Task Handler_UsesSpeechDrivenDurationsAndNarrativeSubtitles()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var scene0 = new byte[] { 1 };
        var scene1 = new byte[] { 2 };
        WriteFile("scene-0.png", scene0);
        WriteFile("scene-1.png", scene1);
        var processRunner = FakeFfmpeg.WritingOutput([42, 42], durationSeconds: 4.25);
        var workspace = await SeedNarrativeAsync(
            processRunner,
            projectId,
            storyboard.Id);

        var handler = CreateHandler(
            projectId,
            storyboard,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 0, "scene-0.png", scene0),
                RenderVideoTestData.SceneAsset(projectId, storyboard.Id, 1, "scene-1.png", scene1)
            ],
            narration: null,
            processRunner,
            workspace: workspace);

        var resultJson = await handler.ExecuteAsync(
            RenderVideoTestData.CreateJob(projectId, storyboard.Id),
            TestContext.Current.CancellationToken);

        var result = RenderVideoResult.Deserialize(resultJson);
        Assert.Equal(RenderAudioSources.Mastered, result.AudioSource);
        Assert.Equal("narrative", result.SubtitleSource);
        Assert.Null(result.SubtitleTrackId);
        Assert.True(result.SubtitleBurnedIn);

        // Speech-driven scene durations reach the planner: crossfade offset is
        // scene0 (3.2s) minus the transition (0.35s).
        var ffmpeg = Assert.Single(
            processRunner.Requests,
            request => request.FileName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase));
        var filter = Filter(ffmpeg.Arguments);
        Assert.Contains("offset=2.85", filter);
    }

    private async Task<AudioProductionWorkspace> SeedNarrativeAsync(
        IProcessRunner processRunner,
        Guid projectId,
        Guid storyboardJobId)
    {
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var workspace = new AudioProductionWorkspace(
            new LocalAssetFileStore(storage),
            new FfprobeMediaInspector(
                Options.Create(new RenderingOptions()),
                processRunner));

        var masterBytes = new byte[] { 9, 9 };
        var masterRelative = AudioProductionWorkspace.FilePath(
            projectId,
            storyboardJobId,
            AudioProductionWorkspace.MasterFileName);
        WriteFile(masterRelative, masterBytes);

        var scenes = new[]
        {
            new AudioSceneStage(
                0, "narration/scene_0.wav", new string('a', 64), 1, 48000, 1,
                2.95, 0.25, 0.0, 3.2, 0.25, 0.45, "Narration for Why local AI."),
            new AudioSceneStage(
                1, "narration/scene_1.wav", new string('b', 64), 1, 48000, 1,
                0.95, 2.85, 2.85, 1.4, 0.25, 0.45, "Narration for Run the workflow.")
        };

        await workspace.SaveManifestAsync(
            projectId,
            storyboardJobId,
            new AudioProductionManifest(
                AudioProductionWorkspace.Version,
                new Dictionary<string, AudioProductionStage>
                {
                    [AudioProductionWorkspace.MasterStage] = new AudioProductionStage(
                        "master-fingerprint",
                        masterRelative,
                        RenderVideoTestData.Hash(masterBytes),
                        masterBytes.Length,
                        4.25,
                        0,
                        0)
                },
                scenes,
                NarrativeTiming.TransitionSeconds,
                3.9,
                4.25),
            TestContext.Current.CancellationToken);

        return workspace;
    }

    private static string Filter(FakeProcessRunner processRunner) =>
        Filter(processRunner.Requests[1].Arguments);

    private static string Filter(IReadOnlyList<string> arguments) =>
        arguments[arguments.ToList().IndexOf("-filter_complex") + 1];

    private static string? SubtitlePath(string filter)
    {
        const string marker = "subtitles=filename='";
        var start = filter.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = filter.IndexOf('\'', start);
        return end < 0 ? null : filter[start..end];
    }

    private RenderVideoJobHandler CreateHandler(
        Guid projectId,
        JobSnapshot storyboard,
        IReadOnlyList<SceneAsset> assets,
        NarrationTrack? narration,
        IProcessRunner processRunner,
        SubtitleTrack? subtitle = null,
        AudioProductionWorkspace? workspace = null)
    {
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var rendering = Options.Create(new RenderingOptions());
        var fileStore = new LocalAssetFileStore(storage);
        var inspector = new FfprobeMediaInspector(rendering, processRunner);

        return new RenderVideoJobHandler(
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new StubAssetRepository([.. assets]),
            new StubNarrationRepository(narration),
            new StubSubtitleRepository(subtitle),
            fileStore,
            new FfmpegVideoRenderer(rendering, storage, processRunner, inspector),
            workspace ?? new AudioProductionWorkspace(fileStore, inspector));
    }

    private void WriteFile(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
