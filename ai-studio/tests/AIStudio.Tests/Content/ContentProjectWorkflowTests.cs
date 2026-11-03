using AIStudio.Application.Content;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Tests.Approvals;
using Xunit;

namespace AIStudio.Tests.Content;

public sealed class ContentProjectWorkflowTests
{
    [Fact]
    public async Task List_ReturnsNewestFirstAndHonorsTake()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = new FakeContentProjectRepository();
        var older = ContentProject.Create("Older project", null, ContentProjectFixture.Now);
        var newer = ContentProject.Create(
            "Newer project",
            "brief",
            ContentProjectFixture.Now.AddMinutes(1));
        repository.Projects[older.Id] = older;
        repository.Projects[newer.Id] = newer;
        var workflow = new ContentProjectWorkflow(
            repository,
            new FakeProjectJobReader(),
            new SettableTimeProvider(ContentProjectFixture.Now));

        var all = await workflow.ListAsync(20, cancellationToken);
        var limited = await workflow.ListAsync(1, cancellationToken);

        Assert.Equal(new[] { newer.Id, older.Id }, all.Select(item => item.Id));
        Assert.Equal("Newer project", all[0].Title);
        Assert.Equal(ContentStatus.Draft, all[0].Status);
        Assert.Equal(ContentProjectFixture.Now.AddMinutes(1), all[0].UpdatedAt);
        Assert.Equal(newer.Id, Assert.Single(limited).Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task List_RejectsTakeOutsideBounds(int take)
    {
        var workflow = new ContentProjectWorkflow(
            new FakeContentProjectRepository(),
            new FakeProjectJobReader(),
            new SettableTimeProvider(ContentProjectFixture.Now));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => workflow.ListAsync(take, TestContext.Current.CancellationToken));

        Assert.Equal("take", exception.ParamName);
    }

    [Fact]
    public async Task Find_ReturnsDetailWithLatestJobSummaryOnly()
    {
        var fixture = new ContentProjectFixture();
        fixture.SeedJob(JobType.GenerateIdea, JobStatus.Succeeded, Now());
        fixture.SeedJob(JobType.GenerateScript, JobStatus.Running, Now().AddMinutes(1));

        var details = await fixture.ProjectWorkflow.FindAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Equal(fixture.ProjectId, details.Id);
        Assert.Equal("Readiness fixture", details.Title);
        Assert.Equal("Disposable fixture row.", details.Brief);
        Assert.Equal(ContentStatus.Draft, details.Status);
        Assert.NotNull(details.LastJob);
        Assert.Equal(JobType.GenerateScript, details.LastJob.Type);
        Assert.Equal(JobStatus.Running, details.LastJob.Status);
    }

    [Fact]
    public async Task Find_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var details = await fixture.ProjectWorkflow.FindAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.Null(details);
    }

    [Fact]
    public async Task Update_ChangesEditableFieldsAndBumpsUpdatedAt()
    {
        var fixture = new ContentProjectFixture();
        fixture.Time.UtcNow = Now().AddMinutes(5);

        var details = await fixture.ProjectWorkflow.UpdateAsync(
            fixture.ProjectId,
            "  Renamed project  ",
            "  Updated brief  ",
            TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Equal("Renamed project", details.Title);
        Assert.Equal("Updated brief", details.Brief);
        Assert.Equal(Now().AddMinutes(5), details.UpdatedAt);
        Assert.Equal(1, fixture.Projects.SaveCount);
        Assert.Equal("Renamed project", fixture.Project.Title);
        Assert.Equal("Updated brief", fixture.Project.Brief);
    }

    [Fact]
    public async Task Update_NoOpKeepsUpdatedAtAndSkipsSave()
    {
        var fixture = new ContentProjectFixture();
        fixture.Time.UtcNow = Now().AddMinutes(5);

        var details = await fixture.ProjectWorkflow.UpdateAsync(
            fixture.ProjectId,
            "Readiness fixture",
            "Disposable fixture row.",
            TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Equal(Now(), details.UpdatedAt);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task Update_ClearsBriefWhenBlank()
    {
        var fixture = new ContentProjectFixture();
        fixture.Time.UtcNow = Now().AddMinutes(5);

        var details = await fixture.ProjectWorkflow.UpdateAsync(
            fixture.ProjectId,
            "Readiness fixture",
            "   ",
            TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Null(details.Brief);
        Assert.Equal(1, fixture.Projects.SaveCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_RejectsBlankTitle(string title)
    {
        var fixture = new ContentProjectFixture();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.ProjectWorkflow.UpdateAsync(
                fixture.ProjectId,
                title,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("title", exception.ParamName);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task Update_RejectsNullTitle()
    {
        var fixture = new ContentProjectFixture();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.ProjectWorkflow.UpdateAsync(
                fixture.ProjectId,
                null!,
                null,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Update_RejectsOverlongTitleAndBrief()
    {
        var fixture = new ContentProjectFixture();

        var titleException = await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.ProjectWorkflow.UpdateAsync(
                fixture.ProjectId,
                new string('a', ContentProject.MaxTitleLength + 1),
                null,
                TestContext.Current.CancellationToken));
        var briefException = await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.ProjectWorkflow.UpdateAsync(
                fixture.ProjectId,
                "Valid title",
                new string('a', ContentProject.MaxBriefLength + 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("title", titleException.ParamName);
        Assert.Equal("brief", briefException.ParamName);
    }

    [Fact]
    public async Task Update_NeverChangesStatus()
    {
        var fixture = new ContentProjectFixture();
        fixture.Project.TransitionTo(ContentStatus.Researching, Now().AddMinutes(1));
        fixture.Time.UtcNow = Now().AddMinutes(2);

        var details = await fixture.ProjectWorkflow.UpdateAsync(
            fixture.ProjectId,
            "Renamed project",
            null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Equal(ContentStatus.Researching, details.Status);
    }

    [Fact]
    public async Task Update_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var details = await fixture.ProjectWorkflow.UpdateAsync(
            fixture.ProjectId,
            "Renamed project",
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(details);
    }

    [Fact]
    public async Task Update_RejectsEmptyIdentifier()
    {
        var fixture = new ContentProjectFixture();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.ProjectWorkflow.UpdateAsync(
                Guid.Empty,
                "Renamed project",
                null,
                TestContext.Current.CancellationToken));
    }

    private static DateTimeOffset Now() => ContentProjectFixture.Now;
}
