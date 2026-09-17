namespace AIStudio.Application.Jobs;

public interface IJobReader
{
    Task<JobSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken);
}
