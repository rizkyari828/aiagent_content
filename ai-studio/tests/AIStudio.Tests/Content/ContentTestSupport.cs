using AIStudio.Application.Approvals;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Tests.Approvals;

namespace AIStudio.Tests.Content;

internal sealed class FakeContentProjectRepository : IContentProjectRepository
{
    public Dictionary<Guid, ContentProject> Projects { get; } = [];

    public int SaveCount { get; private set; }

    public Task<ContentProject?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Projects.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<ContentProjectSummary>> ListAsync(
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ContentProjectSummary> items = Projects.Values
            .OrderByDescending(project => project.UpdatedAt)
            .ThenBy(project => project.Id)
            .Take(take)
            .Select(project => new ContentProjectSummary(
                project.Id,
                project.Title,
                project.Status,
                project.UpdatedAt))
            .ToArray();
        return Task.FromResult(items);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        return Task.FromResult(1);
    }
}

/// <summary>
/// Single-project job history used by the content fixtures. Ordering mirrors the
/// PostgreSQL reader: newest <c>createdAt</c>, then id ordinal.
/// </summary>
internal sealed class FakeProjectJobReader : IProjectJobReader
{
    public List<ProjectJobSummary> Jobs { get; } = [];

    public Task<ProjectJobSummary?> FindLatestAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ordered().FirstOrDefault());
    }

    public Task<ProjectJobSummary?> FindLatestAsync(
        Guid contentProjectId,
        JobType type,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ordered().FirstOrDefault(job => job.Type == type));
    }

    public FakeProjectJobReader Add(ProjectJobSummary job)
    {
        Jobs.Add(job);
        return this;
    }

    private IEnumerable<ProjectJobSummary> Ordered() =>
        Jobs.OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id);
}

internal sealed class ContentProjectFixture
{
    public ContentProjectFixture(bool projectExists = true)
    {
        if (projectExists)
        {
            Projects.Projects[Project.Id] = Project;
        }

        ProjectReader = new ProjectReaderStub(
            projectExists
                ? new ContentProjectSnapshot(Project.Id, Project.Title, Project.Brief)
                : null);
        RenderJobs.Jobs[RenderJobId] = ApprovalTestData.RenderJob(Project.Id, RenderJobId);

        ProjectWorkflow = new ContentProjectWorkflow(Projects, JobSummaries, Time);
        ApprovalWorkflow = new FinalApprovalWorkflow(
            Approvals,
            ProjectReader,
            RenderJobs,
            Store,
            Time);
        ReadinessWorkflow = new ContentReadinessWorkflow(
            Projects,
            JobSummaries,
            ApprovalWorkflow,
            Publications,
            Time);
    }

    public static DateTimeOffset Now => ApprovalTestData.Now;

    public ContentProject Project { get; } =
        ContentProject.Create("Readiness fixture", "Disposable fixture row.", Now);

    public Guid ProjectId => Project.Id;

    public Guid RenderJobId { get; } = Guid.NewGuid();

    public Guid QaJobId { get; } = Guid.NewGuid();

    public FakeContentProjectRepository Projects { get; } = new();

    public FakeProjectJobReader JobSummaries { get; } = new();

    public FakeFinalApprovalRepository Approvals { get; } = new();

    public JobReaderStub RenderJobs { get; } = new();

    public FakeAssetFileStore Store { get; } = new();

    public FakePublicationRepository Publications { get; } = new();

    public ProjectReaderStub ProjectReader { get; }

    public SettableTimeProvider Time { get; } = new(Now);

    public ContentProjectWorkflow ProjectWorkflow { get; }

    public FinalApprovalWorkflow ApprovalWorkflow { get; }

    public ContentReadinessWorkflow ReadinessWorkflow { get; }

    public ContentProjectFixture SeedJob(
        JobType type,
        JobStatus status,
        DateTimeOffset? createdAt = null,
        Guid? id = null)
    {
        JobSummaries.Add(new ProjectJobSummary(
            id ?? Guid.NewGuid(),
            type,
            status,
            createdAt ?? Now,
            createdAt ?? Now,
            status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Cancelled
                ? createdAt ?? Now
                : null));
        return this;
    }

    /// <summary>Seeds a succeeded RenderVideo job summary matching the A3 evidence rows.</summary>
    public ContentProjectFixture SeedSucceededRender(DateTimeOffset? createdAt = null)
    {
        SeedJob(JobType.RenderVideo, JobStatus.Succeeded, createdAt, RenderJobId);
        RenderJobs.Jobs[RenderJobId] = ApprovalTestData.RenderJob(
            ProjectId,
            RenderJobId,
            createdAt);
        return this;
    }

    public ContentProjectFixture SeedPassingQa()
    {
        Approvals.QaEvidence = ApprovalTestData.QaEvidence(QaJobId);
        return this;
    }

    public ContentProjectFixture SeedQaFailure()
    {
        Approvals.QaEvidence = new FinalVideoQaEvidence(
            QaJobId,
            Result: null,
            Succeeded: false,
            CompletedAt: Now.AddMinutes(1));
        return this;
    }

    public ContentProjectFixture SeedApproval(string destination = "youtube")
    {
        Approvals.Approval = ApprovalTestData.CreateApproval(
            ProjectId,
            RenderJobId,
            destination);
        return this;
    }
}
