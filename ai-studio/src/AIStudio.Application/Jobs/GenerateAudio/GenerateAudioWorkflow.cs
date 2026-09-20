using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs.GenerateAudio;

/// <summary>
/// Enqueue side of the durable audio production step. It gates on a completed
/// storyboard exactly like the visual and render workflows so the enqueue and the
/// handler share the same guard.
/// </summary>
public sealed class GenerateAudioWorkflow(
    IApplicationDbContext dbContext,
    IContentProjectReader contentProjects,
    IJobReader jobs,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<Guid?> EnqueueAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(storyboardJobId, nameof(storyboardJobId));

        var project = await contentProjects.FindByIdAsync(
            contentProjectId,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var storyboard = await RequireStoryboardAsync(
            contentProjectId,
            storyboardJobId,
            jobs,
            cancellationToken);

        var payload = JsonSerializer.Serialize(
            new GenerateAudioJobPayload(contentProjectId, storyboardJobId),
            JsonOptions);

        var job = Job.Create(
            contentProjectId,
            JobType.GenerateAudio,
            ComputeInputVersionHash(payload, storyboard),
            payload,
            timeProvider.GetUtcNow());

        dbContext.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return job.Id;
    }

    /// <summary>
    /// Shared by the enqueue workflow and the handler so both gate on the same
    /// completed storyboard.
    /// </summary>
    internal static async Task<GenerateStoryboardResult> RequireStoryboardAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        IJobReader jobs,
        CancellationToken cancellationToken)
    {
        var job = await jobs.FindByIdAsync(storyboardJobId, cancellationToken);
        if (job is null || job.ContentProjectId != contentProjectId)
        {
            throw Error(
                "audio_storyboard_not_found",
                $"GenerateStoryboard job '{storyboardJobId}' does not exist for this content project.");
        }

        if (job.Type != JobType.GenerateStoryboard
            || job.Status != JobStatus.Succeeded
            || job.Result is null)
        {
            throw Error(
                "audio_storyboard_invalid",
                "Audio production requires a completed GenerateStoryboard job result.");
        }

        try
        {
            return GenerateStoryboardResult.Deserialize(job.Result);
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "audio_storyboard_invalid",
                "The storyboard result is not a valid structured storyboard.",
                exception);
        }
    }

    private static string ComputeInputVersionHash(
        string payload,
        GenerateStoryboardResult storyboard)
    {
        var builder = new StringBuilder(payload)
            .Append('\n')
            .Append(storyboard.Serialize())
            .Append('\n')
            .Append(AudioProductionWorkspace.Version);

        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static AudioProductionException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
