using System.Text.Json;
using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

/// <summary>
/// Proves the central principle: a new story format is DATA. An anime horror
/// pattern and a kids song pattern register and drive a story plan without a new
/// enum value, StoryDirector subclass, registry implementation change, or
/// production handler.
/// </summary>
public sealed class NewNarrativePatternWithoutNewCodeTests
{
    private const string AnimeHorrorJson = """
        {
          "id": "anime-horror-reveal",
          "version": 1,
          "displayName": "Anime horror reveal",
          "beatSlots": [
            { "role": "normality", "purpose": "Establish the ordinary before the break." },
            { "role": "disturbance", "purpose": "Introduce the first wrong detail." },
            { "role": "escalation", "purpose": "Raise the dread step by step." },
            { "role": "reveal", "purpose": "Show what is actually happening." },
            { "role": "sting", "purpose": "End on a final unsettling image." }
          ]
        }
        """;

    private const string KidsSongJson = """
        {
          "id": "kids-song-loop",
          "version": 1,
          "displayName": "Kids song loop",
          "beatSlots": [
            { "role": "intro", "purpose": "Greet the child and set the mood." },
            { "role": "verse", "purpose": "Introduce the first simple idea." },
            { "role": "chorus", "purpose": "Repeat the catchy core line." },
            { "role": "verse", "purpose": "Introduce the second simple idea." },
            { "role": "chorus", "purpose": "Repeat the catchy core line." },
            { "role": "celebration", "purpose": "End with a joyful finale." }
          ]
        }
        """;

    [Fact]
    public void AnimeHorrorPatternRegistersAndDirectsWithoutNewCode()
    {
        var pattern = JsonSerializer.Deserialize<NarrativePattern>(AnimeHorrorJson);

        Assert.NotNull(pattern);
        Assert.Equal("anime-horror-reveal", pattern!.Id.Value);
        Assert.Empty(pattern.Validate());

        var registry = new NarrativePatternRegistry(SeedNarrativePatterns.All);
        registry.Register(pattern);
        var director = new StoryDirector(registry);

        var plan = director
            .Direct(StoryTestSupport.Request(pattern: "anime-horror-reveal", targetDuration: 50))
            .Plan;

        Assert.Equal(
            ["normality", "disturbance", "escalation", "reveal", "sting"],
            plan.Beats.Select(beat => beat.Role.Value));
        Assert.Empty(plan.Validate(pattern));
        Assert.Equal(50, plan.Beats.Sum(beat => beat.TargetDurationSeconds));
    }

    [Fact]
    public void KidsSongPatternWithRepeatedRolesRegistersAndDirectsWithoutNewCode()
    {
        var pattern = JsonSerializer.Deserialize<NarrativePattern>(KidsSongJson);

        Assert.NotNull(pattern);
        Assert.Equal("kids-song-loop", pattern!.Id.Value);
        Assert.Empty(pattern.Validate());

        var registry = new NarrativePatternRegistry(SeedNarrativePatterns.All);
        registry.Register(pattern);
        var director = new StoryDirector(registry);

        var plan = director
            .Direct(StoryTestSupport.Request(pattern: "kids-song-loop", targetDuration: 60))
            .Plan;

        Assert.Equal(
            ["intro", "verse", "chorus", "verse", "chorus", "celebration"],
            plan.Beats.Select(beat => beat.Role.Value));
        Assert.Equal(6, plan.Beats.Select(beat => beat.Id.Value).Distinct().Count());
        Assert.Empty(plan.Validate(pattern));
    }

    [Fact]
    public void NewPatternsArePureDataWithNoExecutableFields()
    {
        var pattern = JsonSerializer.Deserialize<NarrativePattern>(AnimeHorrorJson)!;

        foreach (var slot in pattern.BeatSlots)
        {
            Assert.True(StoryIdentifier.IsValid(slot.Role.Value));
            Assert.False(string.IsNullOrWhiteSpace(slot.Purpose));
        }

        Assert.DoesNotContain("Type", AnimeHorrorJson);
        Assert.DoesNotContain("Assembly", AnimeHorrorJson);
        Assert.DoesNotContain("command", AnimeHorrorJson);
    }
}
