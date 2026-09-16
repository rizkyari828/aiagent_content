using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;

namespace AIStudio.Infrastructure.Jobs;

public sealed class PlaceholderJobHandler : IJobHandler
{
    public bool CanHandle(JobType type) =>
        type != JobType.GenerateIdea && Enum.IsDefined(type);

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
