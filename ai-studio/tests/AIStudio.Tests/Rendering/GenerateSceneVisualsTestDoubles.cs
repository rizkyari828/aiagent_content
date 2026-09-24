using System.Text.Json;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Rendering;

internal sealed class StubSceneVisualRenderer(
    Func<SceneVisualBrief, byte[]>? render = null) : ISceneVisualRenderer
{
    public List<SceneVisualBrief> Briefs { get; } = [];

    public List<SceneVisualBrief> AnimatedBriefs { get; } = [];

    public List<double> AnimationDurations { get; } = [];

    public List<string> MotionLabels { get; } = [];

    public Task<byte[]> RenderPngAsync(
        SceneVisualBrief brief,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Briefs.Add(brief);
        return Task.FromResult(render?.Invoke(brief) ?? new byte[] { 137, 80, 78, 71 });
    }

    public Task<byte[]> RenderAnimationAsync(
        SceneVisualBrief brief,
        SceneChoreography choreography,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnimatedBriefs.Add(brief);
        AnimationDurations.Add(durationSeconds);
        return Task.FromResult(
            render?.Invoke(brief)
            ?? new byte[] { 0, 0, 0, 1, 102, 116, 121, 112 });
    }

    public Task<byte[]> RenderImageMotionAsync(
        byte[] backgroundPng,
        string overlayLabel,
        SceneVisualPalette palette,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MotionLabels.Add(overlayLabel);
        return Task.FromResult(new byte[] { 0, 0, 0, 1, 102, 116, 121, 112 });
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

    public const string OpeningStoryboard = """
        {
          "title": "Opening storyboard",
          "scenes": [
            {
              "heading": "Opening Hook",
              "visual": "A laptop appears and local AI runs offline while the cloud connection is disabled."
            }
          ]
        }
        """;

    public const string ClosingStoryboard = """
        {
          "title": "Closing storyboard",
          "scenes": [
            {
              "heading": "Closing",
              "visual": "A confident person opens a laptop and chats with local AI; a final callout appears."
            }
          ]
        }
        """;

    /// <summary>
    /// Deterministic narrative fixture. Every heading and visual avoids the tech
    /// keyword vocabulary, so each scene genuinely classifies as
    /// <c>SceneVisualIntent.Generic</c> under the existing director. No anime keyword
    /// is present anywhere.
    /// </summary>
    public const string NarrativeStoryboard = """
        {
          "title": "Narrative motion comic",
          "scenes": [
            {
              "heading": "Student enters a quiet study room",
              "visual": "The student steps into a calm room with a wooden desk and a warm lamp."
            },
            {
              "heading": "Student notices a mysterious interface",
              "visual": "A glowing panel awakens beside the student and pulses softly."
            },
            {
              "heading": "A shadowy figure appears behind the student",
              "visual": "A tall silhouette rises slowly in the dim background."
            },
            {
              "heading": "Close emotional reaction and payoff",
              "visual": "The student turns with wide eyes as the room brightens into hope."
            }
          ]
        }
        """;

    public static string Payload(
        Guid contentProjectId,
        Guid storyboardJobId,
        bool force = false,
        IReadOnlyList<PinnedIdentityAsset>? identityReferences = null,
        ProductionRecipeReference? productionRecipe = null,
        string? artDirection = null) =>
        JsonSerializer.Serialize(
            new GenerateSceneVisualsJobPayload(contentProjectId, storyboardJobId, force)
            {
                ProductionRecipe = productionRecipe,
                ArtDirection = artDirection,
                IdentityReferences = identityReferences
            },
            JsonOptions);

    public static ClaimedJob Job(
        Guid contentProjectId,
        Guid storyboardJobId,
        JobType type = JobType.GenerateSceneVisuals,
        bool force = false,
        IReadOnlyList<PinnedIdentityAsset>? identityReferences = null,
        ProductionRecipeReference? productionRecipe = null,
        string? artDirection = null) =>
        new(
            Guid.NewGuid(),
            contentProjectId,
            type,
            "input-v1",
            Payload(contentProjectId, storyboardJobId, force, identityReferences, productionRecipe, artDirection),
            0,
            2,
            false);
}
