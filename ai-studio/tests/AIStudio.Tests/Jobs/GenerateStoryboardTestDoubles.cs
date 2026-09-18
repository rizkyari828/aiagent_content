using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;

namespace AIStudio.Tests.Jobs;

internal static class GenerateStoryboardTestData
{
    public static readonly DateTimeOffset Created =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public const string ValidResult = """
        {
          "title": "Local AI Storyboard",
          "scenes": [
            {
              "heading": "Why local AI",
              "visual": "A laptop running a local model with the network cable unplugged."
            },
            {
              "heading": "Run the workflow",
              "visual": "A screen recording of the job queue and the structured result viewer."
            }
          ]
        }
        """;

    public static string ApprovedScriptContent =>
        GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult).Serialize();

    public static ReviewedScript Script(
        Guid contentProjectId,
        ScriptReviewStatus status,
        string? content = null)
    {
        var script = ReviewedScript.Create(
            contentProjectId,
            Guid.NewGuid(),
            content ?? ApprovedScriptContent,
            Created);

        if (status == ScriptReviewStatus.Approved)
        {
            script.Approve(Created.AddMinutes(1));
        }

        return script;
    }

    public static string ValidPayload(Guid contentProjectId) =>
        JsonSerializer.Serialize(
            new GenerateStoryboardJobPayload(contentProjectId),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static ClaimedJob CreateJob(Guid contentProjectId, string payload) =>
        new(
            Guid.NewGuid(),
            contentProjectId,
            JobType.GenerateStoryboard,
            "input-v1",
            payload,
            0,
            2,
            false);
}

internal sealed class StubScriptReviewRepository(ReviewedScript? script = null)
    : IScriptReviewRepository
{
    public ReviewedScript? Script { get; set; } = script;

    public int SaveCount { get; private set; }

    public Task<ReviewedScript?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            Script?.ContentProjectId == contentProjectId ? Script : null);
    }

    public void Add(ReviewedScript reviewedScript) => Script = reviewedScript;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}
