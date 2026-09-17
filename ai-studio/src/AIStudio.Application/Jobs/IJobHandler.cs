using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs;

public interface IJobHandler
{
    bool CanHandle(JobType type);

    Task<string> ExecuteAsync(ClaimedJob job, CancellationToken cancellationToken);
}
