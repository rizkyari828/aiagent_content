using System.Text;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;

namespace AIStudio.Application.Jobs.GenerateStoryboard;

internal static class GenerateStoryboardPrompt
{
    public const string SystemInstruction =
        "You are a storyboard planner. Return only one valid JSON object, " +
        "without markdown fences or commentary.";

    public static string Build(
        ContentProjectSnapshot project,
        GenerateScriptResult script)
    {
        var builder = new StringBuilder()
            .AppendLine("Turn the approved script into a simple, ordered visual storyboard.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Script title: ").AppendLine(script.Title)
            .Append("Opening hook: ").AppendLine(script.OpeningHook);

        for (var index = 0; index < script.Sections.Count; index++)
        {
            var section = script.Sections[index];
            builder
                .Append("Section ").Append(index + 1).Append(" heading: ").AppendLine(section.Heading)
                .Append("Section ").Append(index + 1).Append(" narration: ").AppendLine(section.Narration);
        }

        return builder
            .Append("Closing: ").AppendLine(script.Closing)
            .AppendLine()
            .AppendLine("Return exactly these camelCase fields:")
            .AppendLine("title, scenes.")
            .AppendLine("scenes must be a non-empty ordered array of objects with exactly:")
            .AppendLine("heading, visual.")
            .AppendLine("Follow the script order. All text must be non-empty.")
            .AppendLine("Describe only what should be shown; do not add camera, duration, or rendering metadata.")
            .ToString();
    }
}
