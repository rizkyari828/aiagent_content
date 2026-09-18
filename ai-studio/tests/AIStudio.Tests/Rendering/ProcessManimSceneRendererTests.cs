using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class ProcessManimSceneRendererTests : IDisposable
{
    private readonly string scriptPath =
        Path.Combine(Path.GetTempPath(), $"aistudio-manim-script-{Guid.NewGuid():N}.py");

    public ProcessManimSceneRendererTests() => File.WriteAllText(scriptPath, string.Empty);

    public void Dispose()
    {
        if (File.Exists(scriptPath))
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task RenderAsync_RejectsUnsupportedTemplateWithoutLaunchingProcess()
    {
        var runner = new FakeProcessRunner();
        var renderer = CreateRenderer(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                SceneAnimationTemplate.None,
                Parameters(),
                TestContext.Current.CancellationToken));

        Assert.Equal("manim_template_invalid", exception.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task RenderAsync_LaunchesAllowlistedTemplateWithStructuredArguments()
    {
        var mp4 = new byte[] { 0, 0, 0, 1, 2, 3 };
        string? parametersJson = null;
        var runner = new FakeProcessRunner(request =>
        {
            parametersJson = File.ReadAllText(
                request.Arguments[request.Arguments.ToList().IndexOf("--params") + 1]);
            File.WriteAllBytes(request.Arguments[^1], mp4);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var renderer = CreateRenderer(runner, enabled: true);

        var result = await renderer.RenderAsync(
            SceneAnimationTemplate.LocalAiFlow,
            Parameters(),
            TestContext.Current.CancellationToken);

        Assert.Equal(mp4, result);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("python3-test", request.FileName);
        Assert.Equal(scriptPath, request.Arguments[0]);
        Assert.Contains("--template", request.Arguments);
        Assert.Contains("local_ai_flow", request.Arguments);
        Assert.Contains("--params", request.Arguments);
        Assert.Contains("--output", request.Arguments);
        // The process boundary uses separate arguments, never a shell string.
        Assert.DoesNotContain(
            request.Arguments,
            argument => argument.Contains(';') || argument.Contains("sh -c"));
        Assert.NotNull(parametersJson);
        Assert.Contains("\"primaryText\":\"Heading\"", parametersJson);
        Assert.Contains("\"kicker\":\"Video 1\"", parametersJson);
        Assert.Contains("\"palette\":\"Violet\"", parametersJson);
        // Palette values come from the shared SVG design source, not a second copy.
        Assert.Contains("\"accent\":\"#b9a7ff\"", parametersJson);
        Assert.Contains("\"background\":\"#14122b\"", parametersJson);
    }

    [Fact]
    public async Task RenderAsync_MapsNonZeroExitToRenderFailure()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessResult(1, string.Empty, "synthetic manim failure"));
        var renderer = CreateRenderer(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                SceneAnimationTemplate.ChatFlow,
                Parameters(),
                TestContext.Current.CancellationToken));

        Assert.Equal("manim_render_failed", exception.ErrorCode);
        Assert.Contains("synthetic manim failure", exception.Message);
    }

    [Fact]
    public async Task RenderAsync_MapsMissingToolToToolUnavailable()
    {
        var runner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(
                ProcessExecutionException.StartFailed,
                "tool not found"));
        var renderer = CreateRenderer(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => renderer.RenderAsync(
                SceneAnimationTemplate.ChatFlow,
                Parameters(),
                TestContext.Current.CancellationToken));

        Assert.Equal("manim_render_tool_unavailable", exception.ErrorCode);
    }

    [Fact]
    public void IsEnabled_ReflectsConfiguration()
    {
        Assert.True(CreateRenderer(new FakeProcessRunner(), enabled: true).IsEnabled);
        Assert.False(CreateRenderer(new FakeProcessRunner(), enabled: false).IsEnabled);
    }

    private ProcessManimSceneRenderer CreateRenderer(
        IProcessRunner runner,
        bool enabled) =>
        new(
            Options.Create(new ManimOptions
            {
                Enabled = enabled,
                PythonPath = "python3-test",
                ScriptPath = scriptPath,
                TimeoutSeconds = 30
            }),
            runner);

    private static SceneAnimationParameters Parameters() =>
        new("Video 1", "Heading", "Secondary", "Tertiary", SceneVisualPalette.Violet, 3.0);
}
