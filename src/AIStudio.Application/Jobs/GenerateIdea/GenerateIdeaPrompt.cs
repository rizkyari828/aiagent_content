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
        GenerateIdeaJobPayload payload)
    {
        var prompt = new StringBuilder()
            .AppendLine("Create one focused content idea using the following context.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Project brief: ").AppendLine(project.Brief ?? "Not provided")
            .Append("Topic: ").AppendLine(payload.Topic)
            .Append("Target audience: ").AppendLine(payload.TargetAudience ?? "General audience")
            .Append("Response language: ").AppendLine(payload.Language)
            .AppendLine()
            .AppendLine("Return exactly these camelCase string fields:")
            .AppendLine("title, hook, summary, angle, targetAudience, suggestedFormat.")
            .AppendLine("Every field must be non-empty and written in the requested language.");

        return prompt.ToString();
    }
}
