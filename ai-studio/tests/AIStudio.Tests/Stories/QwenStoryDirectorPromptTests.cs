using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class QwenStoryDirectorPromptTests
{
    [Fact]
    public void RepresentsTheSuppliedPatternSlotsInOrder()
    {
        var prompt = Build();

        Assert.Contains("problem-solution-short", prompt);
        Assert.Contains("Pattern version: 1", prompt);
        Assert.Contains("Ordered beat slots", prompt);
        Assert.Contains("- 1. role=hook", prompt);
        Assert.Contains("- 2. role=problem", prompt);
        Assert.Contains("- 3. role=discovery", prompt);
        Assert.Contains("- 4. role=solution", prompt);
        Assert.Contains("- 5. role=payoff", prompt);
    }

    [Fact]
    public void CopiesTheExactAuthoritativeHeaderValues()
    {
        var prompt = Build();

        Assert.Contains("id = \"run-ai-locally-anime-short-story\"", prompt);
        Assert.Contains("sourceConceptId = \"run-ai-locally-anime-short\"", prompt);
        Assert.Contains("narrativePattern = \"problem-solution-short\"", prompt);
        Assert.Contains("targetDurationSeconds = 60", prompt);
        Assert.Contains("Target duration: 60", prompt);
    }

    [Fact]
    public void StatesTheCompleteJsonContract()
    {
        var prompt = Build();

        foreach (var property in new[]
        {
            "\"id\"", "\"version\"", "\"sourceConceptId\"", "\"narrativePattern\"",
            "\"narrativePatternVersion\"", "\"targetDurationSeconds\"", "\"beats\"",
            "\"order\"", "\"role\"", "\"purpose\"", "\"importance\"",
            "\"characterRefs\"", "\"worldRefs\"", "\"continuityFrom\""
        })
        {
            Assert.Contains(property, prompt);
        }
    }

    [Fact]
    public void StatesIdentifierTokenRules()
    {
        var prompt = Build();

        Assert.Contains("lowercase token", prompt);
        Assert.Contains("beat-01", prompt);
        Assert.Contains("no spaces", prompt);
        Assert.Contains("no version suffixes", prompt);
    }

    [Fact]
    public void StatesContinuityAndDurationRules()
    {
        var prompt = Build();

        Assert.Contains("continuityFrom may list only earlier beat ids", prompt);
        Assert.Contains("never a cycle", prompt);
        Assert.Contains("within 10% of", prompt);
    }

    [Fact]
    public void StatesStoryDirectorBoundaries()
    {
        var prompt = Build();

        Assert.Contains("dialogue", prompt);
        Assert.Contains("shot lists", prompt);
        Assert.Contains("camera/lens/blocking", prompt);
        Assert.Contains("provider details", prompt);
        Assert.Contains("model names", prompt);
        Assert.Contains("file paths", prompt);
        Assert.Contains("commands", prompt);
        Assert.Contains("do not add extra properties", prompt);
        Assert.Contains("markdown fences", prompt);
    }

    [Fact]
    public void DoesNotLeakProviderOrModelDetails()
    {
        var prompt = Build();

        Assert.DoesNotContain("qwen", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ollama", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromptIsDeterministic()
    {
        Assert.Equal(Build(), Build());
    }

    private static string Build() =>
        QwenStoryDirectorPrompt.Build(
            StoryTestSupport.Direction(),
            SeedNarrativePatterns.All.Single(pattern => pattern.Id.Value == "problem-solution-short"),
            new StoryPlanId("run-ai-locally-anime-short-story"),
            new StoryPlanVersion(1),
            60);
}
