using AIStudio.Application.Rendering;
using AIStudio.Domain.Assets;
using AIStudio.Infrastructure.Rendering;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FfmpegCommandPlanTests
{
    [Fact]
    public void SceneDurationSeconds_DividesNarrationAcrossScenes()
    {
        Assert.Equal(10, FfmpegCommandPlan.SceneDurationSeconds(30, 3));
    }

    [Fact]
    public void SceneDurationSeconds_CrossfadeCompensatesForOverlap()
    {
        Assert.Equal(
            (30 + 2 * 1.5) / 3,
            FfmpegCommandPlan.SceneDurationSeconds(30, 3, SceneTransition.Crossfade, 1.5));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    public void SceneDurationSeconds_RejectsNonPositiveNarration(double narration, int scenes)
    {
        var exception = Assert.Throws<RenderVideoException>(
            () => FfmpegCommandPlan.SceneDurationSeconds(narration, scenes));

        Assert.Equal("render_narration_duration_invalid", exception.ErrorCode);
    }

    [Fact]
    public void SceneDurationSeconds_RejectsEmptyStoryboard()
    {
        var exception = Assert.Throws<RenderVideoException>(
            () => FfmpegCommandPlan.SceneDurationSeconds(10, 0));

        Assert.Equal("render_scene_count_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Build_DefaultAppliesMotionCycleAndCrossfade()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image),
                new SceneMediaInput("/root/scene-2.png", AssetType.Image),
                new SceneMediaInput("/root/scene-3.png", AssetType.Image)
            ],
            narrationSeconds: 12);

        var filter = Filter(arguments);

        Assert.Equal(4, CountOccurrences(filter, "zoompan="));
        Assert.Contains("z='1+0.08*on/", filter);
        Assert.Contains("z='1.08-0.08*on/", filter);
        // Safety default: centered zoom only, never a pan that could crop content.
        Assert.DoesNotContain("(iw-iw/zoom)*on/", filter);
        Assert.DoesNotContain("(iw-iw/zoom)*(1-on/", filter);
        Assert.Equal(3, CountOccurrences(filter, "xfade=transition=fade"));
        Assert.DoesNotContain("concat=", filter);
        Assert.Contains("[v]", arguments);
        Assert.Contains("4:a", arguments);
    }

    [Fact]
    public void Build_CrossfadeFadesThroughBackgroundForGraphicScenes()
    {
        var arguments = Build(
            [
                new SceneMediaInput(
                    "/root/scene-0.png",
                    AssetType.Image,
                    VisualKind: SceneVisualKind.Graphic),
                new SceneMediaInput(
                    "/root/scene-1.png",
                    AssetType.Image,
                    VisualKind: SceneVisualKind.Graphic)
            ],
            narrationSeconds: 10);

        var filter = Filter(arguments);

        // Text-heavy scenes must not overlay two text layers; fade through black.
        Assert.Contains("xfade=transition=fadeblack", filter);
        Assert.DoesNotContain("xfade=transition=fade:", filter);
    }

    [Fact]
    public void Build_CrossfadeKeepsFadeForPhotographicScenes()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image)
            ],
            narrationSeconds: 10);

        var filter = Filter(arguments);

        // Absent classification preserves the previous crossfade behavior.
        Assert.Contains("xfade=transition=fade:", filter);
        Assert.DoesNotContain("fadeblack", filter);
    }

    [Fact]
    public void Build_CrossfadeUsesGraphicPolicyOnlyAtGraphicBoundaries()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image),
                new SceneMediaInput(
                    "/root/scene-2.png",
                    AssetType.Image,
                    VisualKind: SceneVisualKind.Graphic)
            ],
            narrationSeconds: 15);

        var filter = Filter(arguments);

        Assert.Equal(1, CountOccurrences(filter, "xfade=transition=fadeblack"));
        Assert.Equal(1, CountOccurrences(filter, "xfade=transition=fade:"));
    }

    [Fact]
    public void Build_ExplicitPanIsOptInForRichVisuals()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image, SceneMotion.PanRight),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image, SceneMotion.PanLeft)
            ],
            narrationSeconds: 10,
            transition: SceneTransition.Cut);

        var filter = Filter(arguments);

        Assert.Contains("z='1.08'", filter);
        Assert.Contains("(iw-iw/zoom)*on/", filter);
        Assert.Contains("(iw-iw/zoom)*(1-on/", filter);
    }

    [Fact]
    public void Build_CutTransitionConcatenatesScenes()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image)
            ],
            narrationSeconds: 10,
            transition: SceneTransition.Cut,
            enableMotion: false);

        var filter = Filter(arguments);

        Assert.Contains("concat=n=2:v=1:a=0[v]", filter);
        Assert.DoesNotContain("zoompan=", filter);
        Assert.DoesNotContain("xfade=", filter);
    }

    [Fact]
    public void Build_FadeTransitionFadesEveryScene()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image)
            ],
            narrationSeconds: 10,
            transition: SceneTransition.Fade,
            enableMotion: false);

        var filter = Filter(arguments);

        Assert.Equal(2, CountOccurrences(filter, "fade=t=in:st=0"));
        Assert.Equal(2, CountOccurrences(filter, "fade=t=out:st="));
        Assert.Contains("concat=n=2:v=1:a=0[v]", filter);
    }

    [Fact]
    public void Build_ExplicitMotionOverridesDefaultCycle()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image, SceneMotion.SlowZoomOut),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image, SceneMotion.None)
            ],
            narrationSeconds: 10,
            transition: SceneTransition.Cut);

        var filter = Filter(arguments);

        Assert.Contains("z='1.08-0.08*on/", filter);
        Assert.Equal(1, CountOccurrences(filter, "zoompan="));
    }

    [Fact]
    public void Build_AddsStyledSubtitleBurnIn()
    {
        var arguments = Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            narrationSeconds: 5,
            subtitlePath: "/root/subtitle.srt",
            subtitle: new SubtitleStyle { FontSize = 28, Outline = 3, MarginVertical = 48 });

        var filter = Filter(arguments);

        Assert.Contains("subtitles=filename='/root/subtitle.srt'", filter);
        Assert.Contains("force_style='", filter);
        Assert.Contains("FontSize=28", filter);
        Assert.Contains("Outline=3", filter);
        Assert.Contains("MarginV=48", filter);
        // Styled subtitles are burned in; no soft subtitle stream is embedded.
        Assert.DoesNotContain("-c:s", arguments);
        Assert.DoesNotContain("mov_text", arguments);
    }

    [Fact]
    public void Build_UsesSmallerReadableSubtitleDefaults()
    {
        var arguments = Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            narrationSeconds: 5,
            subtitlePath: "/root/subtitle.srt");

        var filter = Filter(arguments);

        Assert.Contains("FontSize=16", filter);
        Assert.Contains("Outline=1", filter);
        Assert.Contains("MarginV=18", filter);
        Assert.Contains("PrimaryColour=&H00FFFFFF", filter);
        Assert.Contains("OutlineColour=&H00000000", filter);
    }

    [Fact]
    public void Build_OmitsSubtitleWhenNotProvided()
    {
        var arguments = Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            narrationSeconds: 5);

        Assert.DoesNotContain("-c:s", arguments);
        Assert.DoesNotContain("mov_text", arguments);
        Assert.DoesNotContain("subtitles=", Filter(arguments));
    }

    [Fact]
    public void Build_MixesDuckedBackgroundMusic()
    {
        var arguments = Build(
            [
                new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image)
            ],
            narrationSeconds: 12,
            music: new BackgroundMusic("/root/music.wav", Volume: 0.2, Duck: true));

        var filter = Filter(arguments);

        Assert.Contains("[2:a]loudnorm=I=-16:TP=-1.5:LRA=11,aresample=48000,asetpts=PTS-STARTPTS[nargain]", filter);
        Assert.Contains("[nargain]asplit=2[nar][duckkey]", filter);
        Assert.Contains("highpass=f=120", filter);
        Assert.Contains("volume=0.2", filter);
        Assert.Contains("afade=t=in:st=0", filter);
        Assert.Contains("afade=t=out:st=", filter);
        Assert.Contains("sidechaincompress=", filter);
        Assert.Contains("ratio=4", filter);
        Assert.Contains("amix=inputs=2:duration=first:normalize=0[a]", filter);
        Assert.Contains("-stream_loop", arguments);
        Assert.Contains("-1", arguments);
        Assert.Contains("[a]", arguments);
    }

    [Fact]
    public void Build_MixesBackgroundMusicWithoutDucking()
    {
        var arguments = Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            narrationSeconds: 5,
            music: new BackgroundMusic("/root/music.wav", Volume: 0.3, Duck: false));

        var filter = Filter(arguments);

        Assert.DoesNotContain("sidechaincompress", filter);
        Assert.Contains("amix=inputs=2:duration=first:normalize=0[a]", filter);
    }

    [Fact]
    public void Build_MixesPerSceneSoundEffectWithDelay()
    {
        var arguments = Build(
            [
                new SceneMediaInput(
                    "/root/scene-0.png",
                    AssetType.Image,
                    SoundEffect: new SceneSoundEffect("/root/sfx.wav", 1.5, 0.7)),
                new SceneMediaInput("/root/scene-1.png", AssetType.Image)
            ],
            narrationSeconds: 10,
            transition: SceneTransition.Cut,
            enableMotion: false);

        var filter = Filter(arguments);

        Assert.Contains("adelay=1500:all=1", filter);
        Assert.Contains("volume=0.7", filter);
        Assert.Contains("amix=inputs=2:duration=first:normalize=0[a]", filter);
        Assert.Contains("/root/sfx.wav", arguments);
    }

    [Fact]
    public void Build_RejectsTransitionLongerThanScene()
    {
        var exception = Assert.Throws<RenderVideoException>(
            () => FfmpegCommandPlan.Build(
                [
                    new SceneMediaInput("/root/scene-0.png", AssetType.Image),
                    new SceneMediaInput("/root/scene-1.png", AssetType.Image)
                ],
                "/root/narration.wav",
                "/root/out.part",
                Settings(0.5, transition: SceneTransition.Crossfade, transitionDuration: 0.5)));

        Assert.Equal("render_transition_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Build_KeepsArgumentsAsSeparateListItems()
    {
        var arguments = Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            narrationSeconds: 5);

        Assert.DoesNotContain(arguments, argument => argument.Contains("-i /root"));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("sh "));
        Assert.Contains("-filter_complex", arguments);
    }

    private static IReadOnlyList<string> Build(
        IReadOnlyList<SceneMediaInput> scenes,
        double narrationSeconds,
        SceneTransition transition = SceneTransition.Crossfade,
        bool enableMotion = true,
        string? subtitlePath = null,
        SubtitleStyle? subtitle = null,
        BackgroundMusic? music = null) =>
        FfmpegCommandPlan.Build(
            scenes,
            "/root/narration.wav",
            "/root/out.part",
            Settings(
                narrationSeconds,
                transition,
                enableMotion: enableMotion,
                subtitlePath: subtitlePath,
                subtitle: subtitle,
                music: music));

    private static VideoRenderSettings Settings(
        double narrationSeconds,
        SceneTransition transition = SceneTransition.Crossfade,
        double transitionDuration = 0.5,
        bool enableMotion = true,
        string? subtitlePath = null,
        SubtitleStyle? subtitle = null,
        BackgroundMusic? music = null) =>
        new(
            1280,
            720,
            30,
            narrationSeconds,
            transition,
            transitionDuration,
            enableMotion,
            subtitlePath,
            subtitle,
            music);

    private static string Filter(IReadOnlyList<string> arguments) =>
        arguments[arguments.ToList().IndexOf("-filter_complex") + 1];

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
