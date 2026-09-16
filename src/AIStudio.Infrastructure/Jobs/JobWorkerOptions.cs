namespace AIStudio.Infrastructure.Jobs;

public sealed class JobWorkerOptions
{
    public const string SectionName = "JobWorker";

    public bool Enabled { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
