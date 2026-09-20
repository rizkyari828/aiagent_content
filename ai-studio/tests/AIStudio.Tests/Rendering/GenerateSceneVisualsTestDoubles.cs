using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Rendering;

internal sealed class StubSceneVisualRenderer(
    Func<SceneVisualBrief, byte[]>? render = null) : ISceneVisualRenderer
{
    public List<SceneVisualBrief> Briefs { get; } = [];

    public Task<byte[]> RenderPngAsync(
        SceneVisualBrief brief,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Briefs.Add(brief);
        return Task.FromResult(render?.Invoke(brief) ?? new byte[] { 137, 80, 78, 71 });
    }
}

internal sealed class StubManimSceneRenderer(
    bool isEnabled = true,
    Func<SceneAnimationTemplate, SceneAnimationParameters, byte[]>? render = null)
    : IManimSceneRenderer
{
    public bool IsEnabled { get; } = isEnabled;

    public List<(SceneAnimationTemplate Template, SceneAnimationParameters Parameters)> Calls
    { get; } = [];

    public Task<byte[]> RenderAsync(
        SceneAnimationTemplate template,
        SceneAnimationParameters parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((template, parameters));
        return Task.FromResult(
            render?.Invoke(template, parameters)
            ?? new byte[] { 0, 0, 0, 1, 102, 116, 121, 112 });
    }
}

internal sealed class StubImageGenerationProvider(
    bool isEnabled = true,
    Func<ImageGenerationRequest, byte[]>? generate = null)
    : IImageGenerationProvider
{
    public bool IsEnabled { get; } = isEnabled;

    public List<ImageGenerationRequest> Requests { get; } = [];

    public Task<byte[]> GenerateAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(
            generate?.Invoke(request) ?? new byte[] { 137, 80, 78, 71 });
    }
}

internal sealed class StubThreeDRenderingProvider(
    bool isEnabled = true,
    Func<ThreeDRenderRequest, byte[]>? render = null)
    : IThreeDRenderingProvider
{
    public bool IsEnabled { get; } = isEnabled;

    public List<ThreeDRenderRequest> Requests { get; } = [];

    public Task<byte[]> RenderAsync(
        ThreeDRenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(
            render?.Invoke(request)
            ?? new byte[] { 0, 0, 0, 1, 102, 116, 121, 112 });
    }
}

internal static class GenerateSceneVisualsTestData
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public const string AnimatedStoryboard = """
        {
          "title": "Animated storyboard",
          "scenes": [
            {
              "heading": "Local AI flow",
              "visual": "A laptop sends data to a cloud that is crossed out, then it stays local."
            },
            {
              "heading": "Chat offline",
              "visual": "A chat bubble asks a question and the assistant answers while offline."
            }
          ]
        }
        """;

    public static string Payload(Guid contentProjectId, Guid storyboardJobId) =>
        JsonSerializer.Serialize(
            new GenerateSceneVisualsJobPayload(contentProjectId, storyboardJobId),
            JsonOptions);

    public static ClaimedJob Job(
        Guid contentProjectId,
        Guid storyboardJobId,
        JobType type = JobType.GenerateSceneVisuals) =>
        new(
            Guid.NewGuid(),
            contentProjectId,
            type,
            "input-v1",
            Payload(contentProjectId, storyboardJobId),
            0,
            2,
            false);
}
