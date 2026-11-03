using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Real-tool smoke test for the I2V artifact probe: a genuine H.264 clip is produced
/// with FFmpeg, served through the fake ComfyUI HTTP shape, and verified by the real
/// <see cref="FfprobeMediaInspector"/>. Skips when ffmpeg/ffprobe are not on PATH, so
/// the default suite stays dependency-free.
/// </summary>
public sealed class ComfyUiVideoGenerationProviderIntegrationTests : IDisposable
{
    private readonly string root;
    private readonly string workflowPath;

    public ComfyUiVideoGenerationProviderIntegrationTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-i2v-integration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        workflowPath = Path.Combine(root, "workflow.json");
        File.WriteAllText(workflowPath, Template, Encoding.UTF8);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_WithRealClip_ProbesAndAcceptsArtifact()
    {
        Assert.SkipUnless(
            FfmpegAvailable(),
            "ffmpeg/ffprobe are not available on PATH.");

        var runner = new SystemProcessRunner();
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var fileStore = new LocalAssetFileStore(storage);
        var fixturePath = Path.Combine(root, "fixture.mp4");
        await GenerateClipAsync(runner, fixturePath);
        var clip = await File.ReadAllBytesAsync(
            fixturePath,
            TestContext.Current.CancellationToken);

        var provider = new ComfyUiVideoGenerationProvider(
            new HttpClient(new FixedVideoHandler(clip))
            {
                BaseAddress = new Uri("http://127.0.0.1:8188/")
            },
            Options.Create(new ComfyUiVideoOptions
            {
                Enabled = true,
                WorkflowPath = workflowPath,
                FirstFrameWorkflowPath = workflowPath,
                OutputDirectory = "renders/i2v",
                TimeoutSeconds = 120,
                MaxSeconds = 5,
                Width = 1280,
                Height = 720,
                Fps = 24
            }),
            NoopGpuResourceGate.Instance,
            new IdentityAssetRegistry(),
            new LocalIdentityAssetStore(fileStore),
            fileStore,
            new FfprobeMediaInspector(
                Options.Create(new RenderingOptions()),
                runner));

        var result = await provider.GenerateAsync(
            new VideoGenerationRequest(
                "a paper goose unfolding its wings",
                DurationSeconds: 2,
                FramesPerSecond: 24,
                Width: 320,
                Height: 180,
                Seed: 7),
            TestContext.Current.CancellationToken);

        Assert.Equal(320, result.Width);
        Assert.Equal(180, result.Height);
        Assert.InRange(result.DurationSeconds, 1.75, 2.25);
        Assert.Equal(24, result.FramesPerSecond);
        Assert.Equal(clip.Length, result.ByteSize);
        Assert.True(File.Exists(Path.Combine(root, result.RelativeOutputPath)));
    }

    private static async Task GenerateClipAsync(
        SystemProcessRunner runner,
        string path)
    {
        var execution = await runner.RunAsync(
            new ProcessRunRequest(
                "ffmpeg",
                [
                    "-y", "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi",
                    "-i", "color=c=black:s=320x180:r=24:d=2",
                    "-c:v", "libx264",
                    "-pix_fmt", "yuv420p",
                    path
                ],
                TimeSpan.FromSeconds(60)),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, execution.ExitCode);
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            if (process.ExitCode != 0)
            {
                return false;
            }
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or InvalidOperationException
                or PlatformNotSupportedException)
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception)
            when (exception is Win32Exception
                or InvalidOperationException
                or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed class FixedVideoHandler(byte[] clip) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Task.FromResult(Json("{\"prompt_id\":\"abc\"}"));
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(History));
            }

            if (path.StartsWith("/view?", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(clip)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }

    private const string Template = """
        {
          "1": { "class_type": "TrustedTestPromptNode", "inputs": { "text": "__PROMPT__", "seed": "__SEED__" } },
          "2": {
            "class_type": "TrustedTestVideoNode",
            "inputs": {
              "width": "__WIDTH__",
              "height": "__HEIGHT__",
              "fps": "__FPS__",
              "duration": "__DURATION_SECONDS__"
            }
          }
        }
        """;

    private const string History = """
        {
          "abc": {
            "status": { "completed": true, "status_str": "success" },
            "outputs": {
              "9": {
                "items": [
                  { "filename": "abc_00001_.mp4", "subfolder": "aistudio", "type": "output" }
                ]
              }
            }
          }
        }
        """;
}
