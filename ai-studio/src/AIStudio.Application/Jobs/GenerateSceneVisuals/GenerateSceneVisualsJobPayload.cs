using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

public sealed record GenerateSceneVisualsJobPayload(
    Guid ContentProjectId,
    Guid StoryboardJobId,
    bool Force = false)
{
    /// <summary>
    /// Concrete, already-materialized identity pins for the AI image path. A
    /// materialized job never carries a floating version: the resolution happened
    /// exactly once before persistence. The list is omitted entirely for the
    /// unchanged zero-reference path and is limited to one reference in v1.
    /// </summary>
    [JsonPropertyName("identityReferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PinnedIdentityAsset>? IdentityReferences { get; init; }

    /// <summary>
    /// The explicitly selected production recipe for this visual job, as stable
    /// identity only (id + concrete version). Omitted entirely when the caller did
    /// not select one, so a legacy payload keeps the exact previous routing. A job
    /// never re-resolves "latest recipe".
    /// </summary>
    [JsonPropertyName("productionRecipe")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductionRecipeReference? ProductionRecipe { get; init; }

    /// <summary>
    /// Optional caller-supplied art direction for narrative AI-image scenes, e.g.
    /// "original cinematic anime, soft cel shading, deep blue night tones with warm
    /// amber highlights". It is creative data selected per job (never a hardcoded
    /// genre) and is applied only to narrative AiImage prompts; technical SVG/Manim
    /// prompts are unaffected. Omitted entirely when not supplied.
    /// </summary>
    [JsonPropertyName("artDirection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtDirection { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateSceneVisualsJobPayload Deserialize(string json)
    {
        GenerateSceneVisualsJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<GenerateSceneVisualsJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload(
                "GenerateSceneVisuals payload must be a valid JSON object.",
                exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("GenerateSceneVisuals payload requires a contentProjectId.");
        }

        if (payload.StoryboardJobId == Guid.Empty)
        {
            throw InvalidPayload("GenerateSceneVisuals payload requires a storyboardJobId.");
        }

        return payload with { IdentityReferences = payload.IdentityReferences ?? [] };
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("visual_job_invalid_payload", message, innerException);
}
