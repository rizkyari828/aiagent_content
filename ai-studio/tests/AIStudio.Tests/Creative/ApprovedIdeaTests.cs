using AIStudio.Application.Creative;
using AIStudio.Application.Jobs.GenerateIdea;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class ApprovedIdeaTests
{
    [Fact]
    public void ValidIdeaHasNoIssues()
    {
        Assert.Empty(ApprovedIdeaValidator.Validate(CreativeTestSupport.Idea()));
    }

    [Fact]
    public void MissingCoreIdeaFieldsAreRejected()
    {
        var idea = new ApprovedIdea();

        var codes = Codes(ApprovedIdeaValidator.Validate(idea));

        Assert.Contains(CreativeIssueCodes.IdeaTopicEmpty, codes);
        Assert.Contains(CreativeIssueCodes.IdeaAngleEmpty, codes);
        Assert.Contains(CreativeIssueCodes.IdeaAudienceEmpty, codes);
        Assert.Contains(CreativeIssueCodes.IdeaHookEmpty, codes);
    }

    [Fact]
    public void InvalidConstraintHintsAreRejected()
    {
        var idea = CreativeTestSupport.Idea() with
        {
            Constraints = new CreativeConstraints
            {
                PreferredFormat = "YouTube Longform",
                PreferredStyle = "Clean Tech",
                TargetDurationSeconds = 0
            }
        };

        var codes = Codes(ApprovedIdeaValidator.Validate(idea));

        Assert.Contains(CreativeIssueCodes.IdeaPreferredFormatInvalid, codes);
        Assert.Contains(CreativeIssueCodes.IdeaPreferredStyleInvalid, codes);
        Assert.Contains(CreativeIssueCodes.IdeaDurationInvalid, codes);
    }

    [Fact]
    public void MapsFromExistingGenerateIdeaResultWithoutDuplicatingIdeaLogic()
    {
        var result = new GenerateIdeaResult(
            "Run AI locally",
            "Your existing PC may already be enough",
            "A practical look at running local models.",
            "Stop paying API fees",
            "developers",
            "youtube-longform");

        var idea = ApprovedIdea.FromGenerateIdeaResult(result, "job-42", "educational");

        Assert.Equal("job-42", idea.IdeaReference);
        Assert.Equal("Run AI locally", idea.Topic);
        Assert.Equal("Stop paying API fees", idea.Angle);
        Assert.Equal("developers", idea.Audience);
        Assert.Equal("educational", idea.Objective);
        Assert.Equal("Your existing PC may already be enough", idea.HookPremise);
        Assert.Equal("A practical look at running local models.", idea.SourceSummary);
        Assert.Equal("youtube-longform", idea.Constraints.PreferredFormat);
        Assert.Empty(ApprovedIdeaValidator.Validate(idea));
    }

    private static IReadOnlyList<string> Codes(IReadOnlyList<CreativeIssue> issues) =>
        issues.Select(issue => issue.Code).ToList();
}
