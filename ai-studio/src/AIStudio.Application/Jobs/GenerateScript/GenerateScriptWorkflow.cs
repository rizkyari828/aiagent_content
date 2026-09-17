using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateScript;

public sealed class GenerateScriptWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        GenerateIdeaResult selectedIdea,
        string? language,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "Content project ID cannot be empty.",
                nameof(contentProjectId));
        }

        var rawPayload = JsonSerializer.Serialize(
            new GenerateScriptJobPayload(contentProjectId, selectedIdea, language),
            JsonOptions);
        var normalizedPayload = GenerateScriptJobPayload.Deserialize(rawPayload);
        var payload = JsonSerializer.Serialize(normalizedPayload, JsonOptions);
        var inputVersionHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var job = Job.Create(
            contentProjectId,
            JobType.GenerateScript,
            inputVersionHash,
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }
}
