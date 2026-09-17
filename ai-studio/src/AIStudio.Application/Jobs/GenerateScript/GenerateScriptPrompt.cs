using System.Text;
using AIStudio.Application.Content;

namespace AIStudio.Application.Jobs.GenerateScript;

internal static class GenerateScriptPrompt
{
    public const string SystemInstruction =
        "You are a script writer. Return only one valid JSON object, " +
        "without markdown fences or commentary.";

    public static string Build(
        ContentProjectSnapshot project,
        GenerateScriptJobPayload payload)
    {
        var idea = payload.SelectedIdea;
        return new StringBuilder()
            .AppendLine("Write a practical first-draft video script from the selected idea.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Project brief: ").AppendLine(project.Brief ?? "Not provided")
            .Append("Idea title: ").AppendLine(idea.Title)
            .Append("Idea hook: ").AppendLine(idea.Hook)
            .Append("Idea summary: ").AppendLine(idea.Summary)
            .Append("Idea angle: ").AppendLine(idea.Angle)
            .Append("Target audience: ").AppendLine(idea.TargetAudience)
            .Append("Suggested format: ").AppendLine(idea.SuggestedFormat)
            .Append("Response language: ").AppendLine(payload.Language)
            .AppendLine()
            .AppendLine("Return exactly these camelCase fields:")
            .AppendLine("title, openingHook, sections, closing.")
            .AppendLine("sections must be a non-empty ordered array of objects with exactly:")
            .AppendLine("heading, narration.")
            .AppendLine("All text must be non-empty and written in the requested language.")
            .ToString();
    }
}
