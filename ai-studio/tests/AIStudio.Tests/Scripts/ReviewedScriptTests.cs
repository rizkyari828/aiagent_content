using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Scripts;
using AIStudio.Tests.Jobs;
using Xunit;

namespace AIStudio.Tests.Scripts;

public sealed class ReviewedScriptTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ProducesDraftBoundToSourceJob()
    {
        var projectId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        var content = GenerateScriptResult
            .Deserialize(GenerateScriptTestData.ValidResult)
            .Serialize();

        var script = ReviewedScript.Create(projectId, sourceJobId, content, Now);

        Assert.Equal(projectId, script.ContentProjectId);
        Assert.Equal(sourceJobId, script.SourceJobId);
        Assert.Equal(ScriptReviewStatus.Draft, script.Status);
        Assert.Equal(1, script.Revision);
        Assert.Null(script.ApprovedAt);
    }

    [Fact]
    public void Edit_IncrementsRevisionOnlyWhenCanonicalContentChanges()
    {
        var original = GenerateScriptResult
            .Deserialize(GenerateScriptTestData.ValidResult)
            .Serialize();
        var script = ReviewedScript.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            original,
            Now);

        Assert.False(script.Edit(original, Now.AddMinutes(1)));
        Assert.Equal(1, script.Revision);
        Assert.Equal(Now, script.UpdatedAt);

        var edited = GenerateScriptResult.Deserialize(original) with
        {
            Closing = "A reviewed closing."
        };

        Assert.True(script.Edit(edited.Serialize(), Now.AddMinutes(2)));
        Assert.Equal(2, script.Revision);
        Assert.Equal("A reviewed closing.",
            GenerateScriptResult.Deserialize(script.Content).Closing);
    }

    [Fact]
    public void Approve_IsIdempotentAndPreventsFurtherEditing()
    {
        var content = GenerateScriptResult
            .Deserialize(GenerateScriptTestData.ValidResult)
            .Serialize();
        var script = ReviewedScript.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            content,
            Now);

        Assert.True(script.Approve(Now.AddMinutes(1)));
        Assert.False(script.Approve(Now.AddMinutes(2)));
        Assert.Equal(ScriptReviewStatus.Approved, script.Status);
        Assert.Equal(Now.AddMinutes(1), script.ApprovedAt);

        var exception = Assert.Throws<InvalidOperationException>(
            () => script.Edit(content, Now.AddMinutes(3)));
        Assert.Contains("cannot be edited", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void Create_RejectsInvalidCanonicalContent(string content)
    {
        Assert.Throws<ArgumentException>(() => ReviewedScript.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            content,
            Now));
    }
}
