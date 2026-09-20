using System.Text;
using System.Text.Json.Nodes;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class BlenderThreeDRenderingProviderTests : IDisposable
{
    private static readonly byte[] Mp4 = [0, 0, 0, 1, 102, 116, 121, 112, 0, 1, 2, 3];

    private readonly string templateDirectory;
    private readonly string missingDirectory;

    public BlenderThreeDRenderingProviderTests()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-blender-tests",
            Guid.NewGuid().ToString("N"));
        templateDirectory = Path.Combine(root, "templates");
        missingDirectory = Path.Combine(root, "missing");
        Directory.CreateDirectory(templateDirectory);
        Directory.CreateDirectory(missingDirectory);
        File.WriteAllText(
            Path.Combine(templateDirectory, "local_ai_laptop.py"),
            "# trusted template placeholder",
            Encoding.UTF8);
    }

    public void Dispose()
    {
        var root = Directory.GetParent(templateDirectory)!.FullName;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_ThrowsWhenDisabled()
    {
        var provider = CreateProvider(
            new FakeProcessRunner(),
            enabled: false);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_generation_disabled", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_RejectsUnknownTemplate()
    {
        var provider = CreateProvider(SuccessRunner(), enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(
                Request() with { Template = (SceneThreeDTemplate)999 },
                TestContext.Current.CancellationToken));

        Assert.Equal("threed_template_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_MapsMissingTemplateScript()
    {
        var provider = CreateProvider(
            SuccessRunner(),
            enabled: true,
            templateDirectory: missingDirectory);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_template_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_RejectsInvalidDuration()
    {
        var runner = SuccessRunner();
        var provider = CreateProvider(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(
                Request() with { DurationSeconds = 0 },
                TestContext.Current.CancellationToken));

        Assert.Equal("threed_request_invalid", exception.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task RenderAsync_SerializesOnlyStructuredParametersAndEncodes()
    {
        string? capturedParameters = null;
        var runner = new FakeProcessRunner(request =>
        {
            if (request.FileName == "blender-test")
            {
                Assert.Contains("--factory-startup", request.Arguments);
                Assert.DoesNotContain(
                    request.Arguments,
                    argument => argument.Contains(';') || argument.Contains("sh -c") || argument.Contains("bash"));
                var parametersPath = request.Arguments[request.Arguments.ToList().IndexOf("--params") + 1];
                capturedParameters = File.ReadAllText(parametersPath);
                var framesDirectory = request.Arguments[request.Arguments.ToList().IndexOf("--frames") + 1];
                Directory.CreateDirectory(framesDirectory);
                File.WriteAllBytes(Path.Combine(framesDirectory, "frame_0001.png"), [1]);
                File.WriteAllBytes(Path.Combine(framesDirectory, "frame_0002.png"), [2]);
                return new ProcessResult(0, string.Empty, string.Empty);
            }

            File.WriteAllBytes(request.Arguments[^1], Mp4);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var provider = CreateProvider(runner, enabled: true);

        var bytes = await provider.RenderAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(Mp4, bytes);

        Assert.NotNull(capturedParameters);
        var parameters = JsonNode.Parse(capturedParameters)!.AsObject();
        Assert.Equal(
            new HashSet<string>
            {
                "renderEngine", "computeBackend", "samples", "width", "height",
                "framesPerSecond", "durationSeconds", "seed", "accent", "background"
            },
            parameters.Select(pair => pair.Key).ToHashSet());
        Assert.Equal("CUDA", parameters["computeBackend"]!.GetValue<string>());
        Assert.Equal(768, parameters["width"]!.GetValue<int>());
        Assert.Equal(16, parameters["samples"]!.GetValue<int>());
        // Palette colors come from the shared SVG design source, not a second copy.
        Assert.Equal("#5fd3c4", parameters["accent"]!.GetValue<string>());
        // No source, script, or path fields can cross the boundary.
        Assert.DoesNotContain("python", capturedParameters, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bpy", capturedParameters, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderAsync_MapsBlenderStartFailure()
    {
        var runner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(ProcessExecutionException.StartFailed, "no blender"));
        var provider = CreateProvider(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_provider_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_MapsBlenderTimeout()
    {
        var runner = new FakeProcessRunner(
            _ => throw new ProcessExecutionException(ProcessExecutionException.TimedOut, "too slow"));
        var provider = CreateProvider(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_generation_timeout", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_MapsBlenderNonZeroExit()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessResult(2, string.Empty, "synthetic blender failure"));
        var provider = CreateProvider(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_generation_failed", exception.ErrorCode);
        Assert.Contains("synthetic blender failure", exception.Message);
    }

    [Fact]
    public async Task RenderAsync_MapsMissingFrames()
    {
        var runner = new FakeProcessRunner(
            _ => new ProcessResult(0, string.Empty, string.Empty));
        var provider = CreateProvider(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_output_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_MapsFfmpegFailure()
    {
        var runner = new FakeProcessRunner(request =>
        {
            if (request.FileName == "blender-test")
            {
                var framesDirectory = request.Arguments[request.Arguments.ToList().IndexOf("--frames") + 1];
                Directory.CreateDirectory(framesDirectory);
                File.WriteAllBytes(Path.Combine(framesDirectory, "frame_0001.png"), [1]);
                return new ProcessResult(0, string.Empty, string.Empty);
            }

            return new ProcessResult(1, string.Empty, "ffmpeg exploded");
        });
        var provider = CreateProvider(runner, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.RenderAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("threed_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task RenderAsync_HoldsGateOnlyForBlenderAndReleasesBeforeFfmpeg()
    {
        var gate = new RecordingGpuResourceGate();
        var runner = new FakeProcessRunner(request =>
        {
            if (request.FileName == "blender-test")
            {
                Assert.Equal(1, gate.ActiveLeases);
                var framesDirectory = request.Arguments[request.Arguments.ToList().IndexOf("--frames") + 1];
                Directory.CreateDirectory(framesDirectory);
                File.WriteAllBytes(Path.Combine(framesDirectory, "frame_0001.png"), [1]);
                return new ProcessResult(0, string.Empty, string.Empty);
            }

            // FFmpeg is CPU-only and must run after the GPU lease is released.
            Assert.Equal(0, gate.ActiveLeases);
            File.WriteAllBytes(request.Arguments[^1], Mp4);
            return new ProcessResult(0, string.Empty, string.Empty);
        });
        var provider = CreateProvider(runner, enabled: true, gpuResourceGate: gate);

        var bytes = await provider.RenderAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Equal(Mp4, bytes);
        Assert.Equal(0, gate.ActiveLeases);
        Assert.Equal(["acquire:blender", "release:blender"], gate.Events);
    }

    [Fact]
    public void IsEnabled_ReflectsConfiguration()
    {
        Assert.True(CreateProvider(new FakeProcessRunner(), enabled: true).IsEnabled);
        Assert.False(CreateProvider(new FakeProcessRunner(), enabled: false).IsEnabled);
    }

    private FakeProcessRunner SuccessRunner() =>
        new(request =>
        {
            if (request.FileName == "blender-test")
            {
                var framesDirectory = request.Arguments[request.Arguments.ToList().IndexOf("--frames") + 1];
                Directory.CreateDirectory(framesDirectory);
                File.WriteAllBytes(Path.Combine(framesDirectory, "frame_0001.png"), [1]);
                return new ProcessResult(0, string.Empty, string.Empty);
            }

            File.WriteAllBytes(request.Arguments[^1], Mp4);
            return new ProcessResult(0, string.Empty, string.Empty);
        });

    private BlenderThreeDRenderingProvider CreateProvider(
        IProcessRunner runner,
        bool enabled,
        string? templateDirectory = null,
        IGpuResourceGate? gpuResourceGate = null) =>
        new(
            Options.Create(new BlenderOptions
            {
                Enabled = enabled,
                ExecutablePath = "blender-test",
                TemplateDirectory = templateDirectory ?? this.templateDirectory,
                TimeoutSeconds = 30,
                RenderEngine = "CYCLES",
                ComputeBackend = "CUDA",
                Samples = 16,
                Width = 768,
                Height = 432,
                FramesPerSecond = 30
            }),
            Options.Create(new RenderingOptions
            {
                FfmpegPath = "ffmpeg-test",
                FfprobePath = "ffprobe-test",
                TimeoutSeconds = 30
            }),
            runner,
            gpuResourceGate ?? NoopGpuResourceGate.Instance);

    private static ThreeDRenderRequest Request() =>
        new(SceneThreeDTemplate.LocalAiLaptop, SceneVisualPalette.Ocean, 3.0, 12345);
}
