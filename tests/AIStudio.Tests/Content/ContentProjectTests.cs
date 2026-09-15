using AIStudio.Domain.Content;
using Xunit;

namespace AIStudio.Tests.Content;

public sealed class ContentProjectTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_UsesDraftAndUtcTimestamps()
    {
        var localTime = Now.ToOffset(TimeSpan.FromHours(7));

        var project = ContentProject.Create("  Local AI pilot  ", "  Initial brief  ", localTime);

        Assert.NotEqual(Guid.Empty, project.Id);
        Assert.Equal("Local AI pilot", project.Title);
        Assert.Equal("Initial brief", project.Brief);
        Assert.Equal(ContentStatus.Draft, project.Status);
        Assert.Equal(Now, project.CreatedAt);
        Assert.Equal(Now, project.UpdatedAt);
    }

    [Fact]
    public void TransitionTo_AllowsReviewRework()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);
        project.TransitionTo(ContentStatus.Researching, Now.AddMinutes(1));
        project.TransitionTo(ContentStatus.IdeaReview, Now.AddMinutes(2));

        project.TransitionTo(ContentStatus.Researching, Now.AddMinutes(3));

        Assert.Equal(ContentStatus.Researching, project.Status);
    }

    [Fact]
    public void TransitionTo_RejectsSkippedStage()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);

        Assert.Throws<InvalidOperationException>(
            () => project.TransitionTo(ContentStatus.Published, Now.AddMinutes(1)));
    }
}
