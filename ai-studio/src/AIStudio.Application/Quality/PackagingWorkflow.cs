using System.Text;
using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Research;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Quality;

public sealed class PackagingWorkflow(
    IContentProjectReader contentProjects,
    IScriptReviewRepository scripts,
    IAiTextGenerator textGenerator,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PackagingMetadata?> GetAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var script = await scripts.FindByProjectIdAsync(contentProjectId, cancellationToken);
        if (script is null || string.IsNullOrWhiteSpace(script.PackagingJson))
        {
            return null;
        }

        return JsonSerializer.Deserialize<PackagingMetadata>(script.PackagingJson, JsonOptions);
    }

    public async Task<PackagingMetadata?> GenerateAsync(
        Guid contentProjectId,
        string? provider,
        string? model,
        CancellationToken cancellationToken)
    {
        var project = await contentProjects.FindByIdAsync(contentProjectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var script = await scripts.FindByProjectIdAsync(contentProjectId, cancellationToken);
        if (script is null)
        {
            throw new ResearchException("packaging_script_not_found", "Project does not have a reviewed script.");
        }

        if (script.Status != ScriptReviewStatus.Approved)
        {
            throw new ResearchException("packaging_script_not_approved", "Script must be approved before generating packaging metadata.");
        }

        var parsedScript = GenerateScriptResult.Deserialize(script.Content);
        var prompt = BuildPrompt(project, parsedScript);

        var response = await textGenerator.GenerateAsync(
            new AiTextRequest(
                prompt,
                "You are a YouTube & Short-Form packaging strategist. Return only one valid JSON object without markdown fences.",
                Model: model,
                ResponseFormat: AiResponseFormat.JsonObject,
                Temperature: 0.4,
                MaxTokens: 600,
                Provider: provider),
            cancellationToken);

        var cleaned = ResearchPackageParser.StripMarkdownFence(response.Text);
        var metadata = JsonSerializer.Deserialize<PackagingMetadata>(cleaned, JsonOptions)
            ?? throw new ResearchException("packaging_malformed_response", "AI returned empty packaging metadata.");

        if (metadata.TitleVariants is not { Count: > 0 })
        {
            throw new ResearchException("packaging_malformed_response", "Packaging metadata must include titleVariants.");
        }

        var canonicalJson = JsonSerializer.Serialize(metadata, JsonOptions);
        script.SetPackagingMetadata(canonicalJson, timeProvider.GetUtcNow());
        await scripts.SaveChangesAsync(cancellationToken);

        return metadata;
    }

    private static string BuildPrompt(ContentProjectSnapshot project, GenerateScriptResult script)
    {
        var sb = new StringBuilder()
            .AppendLine("Generate high-CTR packaging metadata for this approved video script.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Script title: ").AppendLine(script.Title)
            .Append("Opening hook: ").AppendLine(script.OpeningHook)
            .Append("Closing: ").AppendLine(script.Closing)
            .AppendLine()
            .AppendLine("Return one JSON object with these exact camelCase fields:")
            .AppendLine("- titleVariants: array of 3 high-CTR titles")
            .AppendLine("- seoDescription: concise 2-3 sentence video description")
            .AppendLine("- hashtags: array of 5-8 relevant hashtags (e.g. #LocalAI)")
            .AppendLine("- thumbnailConcept: { textOverlay (2-4 punchy words), visualComposition (English description) }");

        return sb.ToString();
    }
}

public sealed record ThumbnailConcept(
    string TextOverlay,
    string VisualComposition);

public sealed record PackagingMetadata(
    IReadOnlyList<string> TitleVariants,
    string SeoDescription,
    IReadOnlyList<string> Hashtags,
    ThumbnailConcept ThumbnailConcept);
