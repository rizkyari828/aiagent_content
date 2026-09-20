using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;

namespace AIStudio.Infrastructure.Jobs;

public sealed class PlaceholderJobHandler : IJobHandler
{
    public bool CanHandle(JobType type) =>
        type is not JobType.GenerateIdea
            and not JobType.GenerateScript
            and not JobType.GenerateStoryboard
            and not JobType.RenderVideo
            and not JobType.FinalVideoQa
            and not JobType.GenerateSceneVisuals
            and not JobType.GenerateAudio
        && Enum.IsDefined(type);

    public Task<string> ExecuteAsync(
        ClaimedJob job,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = JsonSerializer.Serialize(new
        {
            status = "placeholder-complete",
            jobType = job.Type.ToString(),
            jobId = job.Id
        });

        return Task.FromResult(result);
    }
}
