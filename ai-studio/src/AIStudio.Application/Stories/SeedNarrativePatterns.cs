namespace AIStudio.Application.Stories;

/// <summary>
/// A deliberately small seed catalog that proves a narrative pattern is DATA. Only
/// two general patterns are seeded on purpose; more formats (anime, kids, mystery,
/// tutorial, ...) belong in tests or later data, not in new C# types. Nothing here
/// is a director subclass or an enum.
/// </summary>
public static class SeedNarrativePatterns
{
    public static IReadOnlyList<NarrativePattern> All { get; } =
    [
        new()
        {
            Id = new NarrativePatternId("problem-solution-short"),
            Version = new NarrativePatternVersion(1),
            DisplayName = "Problem, solution, short",
            BeatSlots =
            [
                new() { Role = new StoryBeatRole("hook"), Purpose = "Grab attention with the core tension.", DurationWeight = 0.15 },
                new() { Role = new StoryBeatRole("problem"), Purpose = "Show the concrete problem the audience feels.", DurationWeight = 0.20 },
                new() { Role = new StoryBeatRole("discovery"), Purpose = "Reveal the approach or insight that changes things.", DurationWeight = 0.25 },
                new() { Role = new StoryBeatRole("solution"), Purpose = "Demonstrate the solution working.", DurationWeight = 0.25 },
                new() { Role = new StoryBeatRole("payoff"), Purpose = "Land the benefit and the takeaway.", DurationWeight = 0.15 }
            ]
        },
        new()
        {
            Id = new NarrativePatternId("explanatory-flow"),
            Version = new NarrativePatternVersion(1),
            DisplayName = "Explanatory flow",
            BeatSlots =
            [
                new() { Role = new StoryBeatRole("hook"), Purpose = "Open with the question or claim.", DurationWeight = 0.15 },
                new() { Role = new StoryBeatRole("context"), Purpose = "Establish why it matters.", DurationWeight = 0.20 },
                new() { Role = new StoryBeatRole("explanation"), Purpose = "Explain the core idea clearly.", DurationWeight = 0.30 },
                new() { Role = new StoryBeatRole("example"), Purpose = "Ground the idea in one concrete example.", DurationWeight = 0.20 },
                new() { Role = new StoryBeatRole("takeaway"), Purpose = "Summarize what the audience should remember.", DurationWeight = 0.15 }
            ]
        }
    ];
}
