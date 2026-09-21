using System.Text;

namespace AIStudio.Application.Creative;

/// <summary>
/// Deterministic, token-efficient prompt builder for the future LLM Creative
/// Director. It carries only the approved idea, a safe recipe/capability summary,
/// the JSON contract, and creative boundaries — never provider implementation
/// details, executable paths, or secrets. Producing the same inputs always yields
/// the same prompt.
/// </summary>
public static class CreativeDirectorPrompt
{
    public const string SystemInstruction =
        "You are a video creative director. Decide how to express an approved idea. " +
        "Return only one valid JSON object, without markdown fences or commentary.";

    public const string OutputContract =
        """{"ideaReference":"...","concept":{"id":"...","version":1,"title":"...","description":"...","audience":"...","format":"...","style":"...","recipeId":"...","recipeVersion":1,"duration":45,"tags":["..."]},"treatment":{"storyApproach":"...","hookTreatment":"...","pacing":"...","visualStrategy":"...","endingTreatment":"...","tone":"...","transitionStrategy":"..."}}""";

    public static string Build(
        ApprovedIdea idea,
        CreativePlanningContext context,
        CreativeDirectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(idea);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var prompt = new StringBuilder()
            .AppendLine("Create one production-ready creative direction for the approved idea below.")
            .AppendLine("Do not invent a new topic or idea. Keep the approved topic, angle, audience and objective.")
            .AppendLine()
            .AppendLine("Approved idea:")
            .Append("Topic: ").AppendLine(idea.Topic)
            .Append("Angle: ").AppendLine(idea.Angle)
            .Append("Audience: ").AppendLine(idea.Audience);

        if (!string.IsNullOrWhiteSpace(idea.Objective))
        {
            prompt.Append("Objective: ").AppendLine(idea.Objective);
        }

        if (!string.IsNullOrWhiteSpace(idea.HookPremise))
        {
            prompt.Append("Hook premise: ").AppendLine(idea.HookPremise);
        }

        if (!string.IsNullOrWhiteSpace(idea.SourceSummary))
        {
            prompt.Append("Source summary: ").AppendLine(idea.SourceSummary);
        }

        if (!string.IsNullOrWhiteSpace(idea.Language))
        {
            prompt.Append("Language: ").AppendLine(idea.Language);
        }

        var constraints = idea.Constraints ?? new CreativeConstraints();
        if (!string.IsNullOrWhiteSpace(constraints.PreferredFormat)
            || !string.IsNullOrWhiteSpace(constraints.PreferredStyle)
            || constraints.TargetDurationSeconds is not null)
        {
            prompt.Append("User constraints: ")
                .Append("format=").Append(constraints.PreferredFormat ?? "any")
                .Append(", style=").Append(constraints.PreferredStyle ?? "any")
                .Append(", durationSeconds=").AppendLine(
                    constraints.TargetDurationSeconds?.ToString() ?? "any");
        }

        prompt.AppendLine()
            .AppendLine("Registered production recipes (select recipeId from this list when possible):");

        if (context.Recipes.Count == 0)
        {
            prompt.AppendLine("- none registered");
        }
        else
        {
            foreach (var recipe in context.Recipes)
            {
                prompt.Append("- recipeId=")
                    .Append(recipe.Id.Value)
                    .Append(" recipeVersion=").Append(recipe.Version.Value)
                    .Append(" (").Append(recipe.DisplayName).Append("): requires ")
                    .Append(recipe.Capabilities.Count == 0 ? "none" : string.Join(", ", recipe.Capabilities))
                    .Append("; resolvable=").Append(recipe.Resolvable ? "true" : "false")
                    .Append("; usesFallbacks=").AppendLine(recipe.UsesFallbacks ? "true" : "false");
            }
        }

        if (context.AvailableCapabilities.Count > 0 || context.UnavailableCapabilities.Count > 0)
        {
            prompt.AppendLine()
                .AppendLine("Production capability awareness (planning context only):");
            prompt.Append("Available: ")
                .AppendLine(context.AvailableCapabilities.Count == 0
                    ? "none"
                    : string.Join(", ", context.AvailableCapabilities));
            prompt.Append("Unavailable: ")
                .AppendLine(context.UnavailableCapabilities.Count == 0
                    ? "none"
                    : string.Join(", ", context.UnavailableCapabilities));
        }

        prompt.AppendLine()
            .AppendLine("Return only one JSON object shaped exactly like:")
            .AppendLine(OutputContract)
            .AppendLine()
            .AppendLine("Rules:")
            .AppendLine("- Decide HOW to express the approved idea; never change WHAT the idea is.")
            .AppendLine("- Return exactly one creative direction.")
            .AppendLine("- format and style are lowercase hyphenated data tokens, for example anime-short or retro-game-documentary.")
            .AppendLine("- concept.id must be a lowercase hyphenated identifier; version is 1; duration is seconds.")
            .AppendLine("- Preserve the approved audience in concept.audience.")
            .AppendLine("- Keep the approved idea reference in ideaReference.")
            .AppendLine("- concept.recipeId must be copied exactly from a recipeId above, with no version suffix and no spaces (for example tech-explainer, never \"tech-explainer v1\"); put the number only in concept.recipeVersion.")
            .AppendLine("- Return only the properties shown in the contract above; do not add extra properties.")
            .AppendLine("- Do not wrap the object in another object or array, do not use markdown fences, and do not add commentary.")
            .AppendLine("- Do not emit code, shell commands, file paths, URLs, model names, or provider details.")
            .Append("- Do not claim an unavailable capability is implemented");

        prompt.AppendLine(options.AllowBeyondCurrentCapabilities
            ? "; a currently unsupported recipe may be chosen, but resolution will report it."
            : "; select a recipe that is currently resolvable.");

        return prompt.ToString();
    }
}
