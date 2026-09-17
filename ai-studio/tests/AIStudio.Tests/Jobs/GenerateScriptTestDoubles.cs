using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Jobs;

internal static class GenerateScriptTestData
{
    public const string ValidResult = """
        {
          "title": "Local AI Tutorial",
          "openingHook": "Build privately on your own machine.",
          "sections": [
            {
              "heading": "Why local AI",
              "narration": "Local inference keeps the workflow under your control."
            },
            {
              "heading": "Run the workflow",
              "narration": "Configure the model, execute the task, and review the result."
            }
          ],
          "closing": "Try the workflow and verify its limits."
        }
        """;

    public static GenerateIdeaResult SelectedIdea => new(
        "Local AI Content",
        "Create privately.",
        "A local workflow.",
        "Privacy",
        "Creators",
        "Tutorial");

    public static string ValidPayload(Guid projectId) =>
        JsonSerializer.Serialize(new GenerateScriptJobPayload(
            projectId,
            SelectedIdea,
            "Indonesian"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static ClaimedJob CreateJob(Guid projectId, string payload) =>
        new(
            Guid.NewGuid(),
            projectId,
            JobType.GenerateScript,
            "input-v1",
            payload,
            0,
            2,
            false);
}
