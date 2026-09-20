using System.Net;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class ComfyUiImageGenerationProviderTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private readonly string workflowPath =
        Path.Combine(Path.GetTempPath(), $"aistudio-comfy-workflow-{Guid.NewGuid():N}.json");

    public ComfyUiImageGenerationProviderTests() =>
        File.WriteAllText(workflowPath, Template, Encoding.UTF8);

    public void Dispose()
    {
        if (File.Exists(workflowPath))
        {
            File.Delete(workflowPath);
        }
    }

    [Fact]
    public async Task GenerateAsync_InjectsPromptIntoApprovedWorkflowAndReturnsPng()
    {
        string? submitted = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                submitted = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}");
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, History);
            }

            if (path.StartsWith("/view?", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Png)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var provider = CreateProvider(handler, enabled: true);

        var bytes = await provider.GenerateAsync(
            new ImageGenerationRequest("a calm local server room", 42),
            TestContext.Current.CancellationToken);

        Assert.Equal(Png, bytes);
        Assert.NotNull(submitted);
        // Prompt and configured dimensions are injected into the fixed graph.
        Assert.Contains("a calm local server room", submitted);
        Assert.Contains("\"width\":1280", submitted);
        Assert.Contains("\"height\":720", submitted);
        Assert.Contains("\"noise_seed\":42", submitted);
        // The submitted body is the approved template with placeholders replaced.
        Assert.DoesNotContain("__PROMPT__", submitted);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsWhenDisabled()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("should not be called")),
            enabled: false);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.GenerateAsync(
                new ImageGenerationRequest("prompt", 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_disabled", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsMissingWorkflow()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new InvalidOperationException("should not be called")),
            enabled: true,
            workflow: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.GenerateAsync(
                new ImageGenerationRequest("prompt", 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("image_workflow_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsUnreachableServerToUnavailable()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) =>
                throw new HttpRequestException("connection refused")),
            enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.GenerateAsync(
                new ImageGenerationRequest("prompt", 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("image_provider_unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsCompletedWithoutImageToFailure()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}"));
            }

            return Task.FromResult(Json(
                HttpStatusCode.OK,
                "{\"abc\":{\"status\":{\"completed\":true,\"status_str\":\"success\"},\"outputs\":{}}}"));
        });
        var provider = CreateProvider(handler, enabled: true);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.GenerateAsync(
                new ImageGenerationRequest("prompt", 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_failed", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_MapsPollTimeout()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}"));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, "{}"));
        });
        var provider = CreateProvider(handler, enabled: true, timeoutSeconds: 1);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => provider.GenerateAsync(
                new ImageGenerationRequest("prompt", 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("image_generation_timeout", exception.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_HoldsGateUntilGenerationCompletes()
    {
        var gate = new RecordingGpuResourceGate();
        var generationStarted = new TaskCompletionSource();
        var releaseGeneration = new TaskCompletionSource();

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/prompt")
            {
                return Json(HttpStatusCode.OK, "{\"prompt_id\":\"abc\"}");
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                generationStarted.TrySetResult();
                await releaseGeneration.Task.WaitAsync(TestContext.Current.CancellationToken);
                return Json(HttpStatusCode.OK, History);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Png)
            };
        });
        var provider = CreateProvider(handler, enabled: true, gpuResourceGate: gate);

        var run = provider.GenerateAsync(
            new ImageGenerationRequest("prompt", 1),
            TestContext.Current.CancellationToken);

        await generationStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        // The lease is still held while ComfyUI is generating, not merely after submit.
        Assert.Equal(1, gate.ActiveLeases);
        Assert.Equal(1, gate.AcquireCount);
        Assert.Contains("acquire:comfyui", gate.Events);

        releaseGeneration.TrySetResult();
        await run;

        Assert.Equal(0, gate.ActiveLeases);
        Assert.Contains("release:comfyui", gate.Events);
    }

    [Fact]
    public void IsEnabled_ReflectsConfiguration()
    {
        Assert.True(CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: true).IsEnabled);
        Assert.False(CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: false).IsEnabled);
    }

    private ComfyUiImageGenerationProvider CreateProvider(
        HttpMessageHandler handler,
        bool enabled,
        string? workflow = null,
        int timeoutSeconds = 600,
        IGpuResourceGate? gpuResourceGate = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8188/") },
            Options.Create(new ComfyUiOptions
            {
                Enabled = enabled,
                BaseUrl = "http://127.0.0.1:8188",
                WorkflowPath = workflow ?? workflowPath,
                TimeoutSeconds = timeoutSeconds,
                Width = 1280,
                Height = 720
            }),
            gpuResourceGate ?? NoopGpuResourceGate.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private const string Template = """
        {
          "1": { "class_type": "CLIPTextEncode", "inputs": { "text": "__PROMPT__" } },
          "2": { "class_type": "Flux2Scheduler", "inputs": { "width": "__WIDTH__", "height": "__HEIGHT__" } },
          "3": { "class_type": "RandomNoise", "inputs": { "noise_seed": "__SEED__" } }
        }
        """;

    private const string History = """
        {
          "abc": {
            "status": { "completed": true, "status_str": "success" },
            "outputs": {
              "9": {
                "images": [
                  { "filename": "abc_00001_.png", "subfolder": "aistudio", "type": "output" }
                ]
              }
            }
          }
        }
        """;

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
