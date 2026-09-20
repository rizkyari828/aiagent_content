using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FfmpegSceneVisualRendererTests
{
    private static SceneVisualBrief Brief() => new(
        0,
        "Video 1",
        "AI di Laptop Tanpa Internet",
        SceneVisualLayout.Hero,
        SceneVisualPalette.Ocean,
        [new SceneVisualCard("Offline", "Tanpa internet", SceneVisualIcon.Laptop)]);

    [Fact]
    public async Task RenderPngAsync_ReturnsRasterizedPngAndUsesSeparateArguments()
    {
        var png = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        var runner = new FakeProcessRunner(request =>
        {
            var output = request.Arguments[^1];
            File.WriteAllBytes(output, png);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var renderer = CreateRenderer(runner);

        var result = await renderer.RenderPngAsync(Brief(), TestContext.Current.CancellationToken);

        Assert.Equal(png, result);
        var request = Assert.Single(runner.Requests);
        Assert.DoesNotContain(request.Arguments, argument => argument.Contains("-i ") || argument.StartsWith("sh "));
        Assert.Contains("-i", request.Arguments);
        Assert.Contains("-frames:v", request.Arguments);
        Assert.EndsWith("scene.png", request.Arguments[^1]);
    }

    [Fact]
    public async Task RenderPngAsync_ReportsFailedRasterization()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessResult(1, string.Empty, "synthetic rasterize failure"));
        var renderer = CreateRenderer(runner);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderPngAsync(Brief(), TestContext.Current.CancellationToken));

        Assert.Equal("visual_render_failed", exception.ErrorCode);
        Assert.Contains("synthetic rasterize failure", exception.Message);
    }

    [Fact]
    public async Task RenderPngAsync_MapsMissingToolToToolUnavailable()
    {
        var runner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(
                ProcessExecutionException.StartFailed,
                "tool not found"));
        var renderer = CreateRenderer(runner);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderPngAsync(Brief(), TestContext.Current.CancellationToken));

        Assert.Equal("visual_render_tool_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAnimationAsync_EncodesChoreographedFrames()
    {
        var mp4 = new byte[] { 0, 0, 0, 1, 102, 116, 121, 112 };
        var runner = new FakeProcessRunner(request =>
        {
            File.WriteAllBytes(request.Arguments[^1], mp4);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var renderer = CreateRenderer(runner);
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Yang Kamu Butuhkan",
            SceneVisualLayout.Cards,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop"), new SceneVisualCard("Ollama")]);
        var choreography = SceneChoreographyPlanner.Build(brief, 0.5);

        var result = await renderer.RenderAnimationAsync(
            brief,
            choreography,
            0.5,
            TestContext.Current.CancellationToken);

        Assert.Equal(mp4, result);
        var encode = runner.Requests[^1].Arguments;
        Assert.Contains("libx264", encode);
        Assert.Contains(encode, argument => argument.EndsWith("frame_%04d.png", StringComparison.Ordinal));
        // Several rasterized frames plus the encode step.
        Assert.True(runner.Requests.Count >= 7);
        Assert.All(
            runner.Requests,
            request => Assert.DoesNotContain(
                request.Arguments,
                argument => argument.Contains("-i ") || argument.StartsWith("sh ")));
    }

    [Fact]
    public async Task RenderImageMotionAsync_AddsSlowPushAndOverlayMotion()
    {
        var mp4 = new byte[] { 0, 0, 0, 1, 102, 116, 121, 112 };
        var runner = new FakeProcessRunner(request =>
        {
            File.WriteAllBytes(request.Arguments[^1], mp4);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var renderer = CreateRenderer(runner);

        var result = await renderer.RenderImageMotionAsync(
            new byte[] { 137, 80, 78, 71 },
            "AI LOKAL",
            SceneVisualPalette.Ocean,
            0.5,
            TestContext.Current.CancellationToken);

        Assert.Equal(mp4, result);
        var encode = runner.Requests[^1].Arguments.ToList();
        var filter = encode[encode.IndexOf("-filter_complex") + 1];
        Assert.Contains("zoompan=", filter);
        Assert.Contains("overlay=", filter);
        Assert.Contains(encode, argument => argument.EndsWith("overlay_%04d.png", StringComparison.Ordinal));
    }

    private static FfmpegSceneVisualRenderer CreateRenderer(IProcessRunner runner) =>
        new(Options.Create(new RenderingOptions()), runner);
}
