using System.Text;
using AIStudio.Application.Content;

namespace AIStudio.Application.Jobs.GenerateIdea;

internal static class GenerateIdeaPrompt
{
    public const string SystemInstruction =
        "You are a content strategist. Return only one valid JSON object, " +
        "without markdown fences or commentary.";

    public static string Build(
        ContentProjectSnapshot project,
        GenerateIdeaJobPayload payload,
        IReadOnlyList<string>? recentProjectTitles = null,
        IReadOnlyList<string>? activeGuidelines = null)
    {
        var prompt = new StringBuilder()
            .AppendLine("Create one focused primary content idea AND 3 distinct angle variants (curiosity_gap, contrarian, problem_solution) using the following context.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Project brief: ").AppendLine(project.Brief ?? "Not provided")
            .Append("Topic: ").AppendLine(payload.Topic)
            .Append("Target audience: ").AppendLine(payload.TargetAudience ?? "General audience")
            .Append("Response language: ").AppendLine(payload.Language);

        if (activeGuidelines is { Count: > 0 })
        {
            prompt.AppendLine().AppendLine("Active channel guidelines (follow strictly):");
            foreach (var g in activeGuidelines.Take(5))
            {
                prompt.Append("- ").AppendLine(g);
            }
        }

        if (recentProjectTitles is { Count: > 0 })
        {
            prompt.AppendLine().AppendLine("Negative history (do NOT repeat these recent project topics/titles):");
            foreach (var t in recentProjectTitles.Take(12))
            {
                prompt.Append("- ").AppendLine(t);
            }
        }

        prompt.AppendLine()
            .AppendLine("Return one JSON object with these exact camelCase string fields:")
            .AppendLine("title, hook, summary, angle, targetAudience, suggestedFormat,")
            .AppendLine("and an optional 'variants' array of 3 objects (each with title, hook, summary, angle, targetAudience, suggestedFormat).")
            .AppendLine("Every field must be non-empty and written in the requested language.");

        return prompt.ToString();
    }
}
