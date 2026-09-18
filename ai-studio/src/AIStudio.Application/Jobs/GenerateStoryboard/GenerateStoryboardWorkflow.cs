using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Jobs.GenerateStoryboard;

public sealed class GenerateStoryboardWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    IScriptReviewRepository scripts,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "Content project ID cannot be empty.",
                nameof(contentProjectId));
        }

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var script = await scripts.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (script is null)
        {
            throw Error(
                "storyboard_script_not_found",
                $"Content project '{contentProjectId}' does not have a reviewed script.");
        }

        if (script.Status != ScriptReviewStatus.Approved)
        {
            throw Error(
                "storyboard_script_not_approved",
                "The canonical storyboard input must be an approved reviewed script.");
        }

        var canonicalScript = CanonicalizeScript(script.Content);
        var normalizedPayload = GenerateStoryboardJobPayload.Deserialize(
            JsonSerializer.Serialize(
                new GenerateStoryboardJobPayload(contentProjectId),
                JsonOptions));
        var payload = JsonSerializer.Serialize(normalizedPayload, JsonOptions);
        var inputVersionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{payload}\n{canonicalScript}")))
            .ToLowerInvariant();

        var job = Job.Create(
            contentProjectId,
            JobType.GenerateStoryboard,
            inputVersionHash,
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    private static string CanonicalizeScript(string content)
    {
        try
        {
            return GenerateScriptResult.Deserialize(content).Serialize();
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "storyboard_script_invalid",
                "The reviewed script content is not a valid structured script.",
                exception);
        }
    }

    private static StoryboardGenerationException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
