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
    public void Build_ComposesDeterministicArgumentsForImagesAndVideo()
    {
        var scenes = new[]
        {
            new SceneMediaInput("/root/scene-0.png", AssetType.Image),
            new SceneMediaInput("/root/scene-1.mp4", AssetType.Video)
        };

        var arguments = FfmpegCommandPlan.Build(
            scenes,
            "/root/narration.wav",
            "/root/renders/out.part",
            10,
            1280,
            720,
            30);

        Assert.Equal("/root/renders/out.part", arguments[^1]);
        Assert.Equal(1, arguments.Count(argument => argument == "-loop"));
        Assert.Equal(3, arguments.Count(argument => argument == "-i"));
        Assert.Contains("/root/narration.wav", arguments);

        var filter = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("[0:v]", filter);
        Assert.Contains("[1:v]", filter);
        Assert.Contains("fps=30", filter);
        Assert.Contains("concat=n=2:v=1:a=0[v]", filter);

        Assert.Contains("[v]", arguments);
        Assert.Contains("2:a", arguments);
        Assert.Contains(arguments, argument => argument.EndsWith("libx264"));
        Assert.Contains(arguments, argument => argument.Equals("aac"));
    }

    [Fact]
    public void Build_KeepsArgumentsAsSeparateListItems()
    {
        var arguments = FfmpegCommandPlan.Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            "/root/narration.wav",
            "/root/out.part",
            5,
            640,
            360,
            24);

        Assert.DoesNotContain(arguments, argument => argument.Contains("-i /root"));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("sh "));
        Assert.Contains("-filter_complex", arguments);
    }

    [Fact]
    public void Build_AddsSoftSubtitleInputAndMapWhenProvided()
    {
        var arguments = FfmpegCommandPlan.Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            "/root/narration.wav",
            "/root/out.part",
            5,
            640,
            360,
            24,
            "/root/subtitle.srt");

        Assert.Contains("/root/subtitle.srt", arguments);
        Assert.Contains("2:s:0", arguments);
        Assert.Contains("-c:s", arguments);
        Assert.Contains("mov_text", arguments);
    }

    [Fact]
    public void Build_OmitsSubtitleWhenNotProvided()
    {
        var arguments = FfmpegCommandPlan.Build(
            [new SceneMediaInput("/root/scene-0.png", AssetType.Image)],
            "/root/narration.wav",
            "/root/out.part",
            5,
            640,
            360,
            24);

        Assert.DoesNotContain("-c:s", arguments);
        Assert.DoesNotContain("mov_text", arguments);
    }
}
