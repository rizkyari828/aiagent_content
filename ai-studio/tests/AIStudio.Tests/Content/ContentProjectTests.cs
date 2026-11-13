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

    [Fact]
    public void UpdateDetails_NormalizesTextAndBumpsUpdatedAt()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);

        var changed = project.UpdateDetails(
            "  Updated title  ",
            "  New brief  ",
            Now.AddMinutes(1));

        Assert.True(changed);
        Assert.Equal("Updated title", project.Title);
        Assert.Equal("New brief", project.Brief);
        Assert.Equal(Now.AddMinutes(1), project.UpdatedAt);
        Assert.Equal(ContentStatus.Draft, project.Status);
    }

    [Fact]
    public void UpdateDetails_NoOpKeepsUpdatedAt()
    {
        var project = ContentProject.Create("Local AI pilot", "Brief", Now);

        var changed = project.UpdateDetails("Local AI pilot", "Brief", Now.AddHours(1));

        Assert.False(changed);
        Assert.Equal(Now, project.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_ClearsBlankBrief()
    {
        var project = ContentProject.Create("Local AI pilot", "Brief", Now);

        var changed = project.UpdateDetails("Local AI pilot", "   ", Now.AddMinutes(1));

        Assert.True(changed);
        Assert.Null(project.Brief);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDetails_RejectsBlankTitle(string title)
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);

        Assert.Throws<ArgumentException>(
            () => project.UpdateDetails(title, null, Now.AddMinutes(1)));
    }

    [Fact]
    public void MarkReadyToPublish_SetsStatusAndTimestampFromAnyLiveStatus()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);

        project.MarkReadyToPublish(Now.AddMinutes(1));

        Assert.Equal(ContentStatus.ReadyToPublish, project.Status);
        Assert.Equal(Now.AddMinutes(1), project.UpdatedAt);
    }

    [Fact]
    public void MarkReadyToPublish_IsIdempotent()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);
        project.MarkReadyToPublish(Now.AddMinutes(1));

        project.MarkReadyToPublish(Now.AddMinutes(2));

        Assert.Equal(ContentStatus.ReadyToPublish, project.Status);
        Assert.Equal(Now.AddMinutes(1), project.UpdatedAt);
    }

    [Theory]
    [InlineData(ContentStatus.Published)]
    [InlineData(ContentStatus.Archived)]
    public void MarkReadyToPublish_RejectsTerminalStatuses(ContentStatus terminal)
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);
        project.MarkReadyToPublish(Now.AddMinutes(1));
        project.TransitionTo(terminal, Now.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(
            () => project.MarkReadyToPublish(Now.AddMinutes(3)));
    }

    [Fact]
    public void MarkPublished_SetsStatusAndTimestampFromReadyToPublish()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);
        project.MarkReadyToPublish(Now.AddMinutes(1));

        project.MarkPublished(Now.AddMinutes(2));

        Assert.Equal(ContentStatus.Published, project.Status);
        Assert.Equal(Now.AddMinutes(2), project.UpdatedAt);
    }

    [Fact]
    public void MarkPublished_IsIdempotent()
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);
        project.MarkReadyToPublish(Now.AddMinutes(1));
        project.MarkPublished(Now.AddMinutes(2));

        project.MarkPublished(Now.AddMinutes(3));

        Assert.Equal(ContentStatus.Published, project.Status);
        Assert.Equal(Now.AddMinutes(2), project.UpdatedAt);
    }

    [Theory]
    [InlineData(ContentStatus.Draft)]
    [InlineData(ContentStatus.Archived)]
    public void MarkPublished_RejectsInvalidStatuses(ContentStatus invalid)
    {
        var project = ContentProject.Create("Local AI pilot", null, Now);
        if (invalid == ContentStatus.Archived)
        {
            project.TransitionTo(ContentStatus.Archived, Now.AddMinutes(1));
        }

        Assert.Throws<InvalidOperationException>(
            () => project.MarkPublished(Now.AddMinutes(2)));
    }
}
