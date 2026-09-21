using AIStudio.Application.Bibles;
using AIStudio.Tests.Bibles;
using AIStudio.Tests.Stories;
using Xunit;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Tests.StoryContext;

/// <summary>
/// Proves the context builder is format-agnostic: a tech explainer, an anime horror
/// story, and a preschool 3D story all use the same builder with no subclass, enum,
/// or genre-conditional branch.
/// </summary>
public sealed class StoryContextGeneralityTests
{
    [Fact]
    public void TechExplainerStoryBuilds()
    {
        var context = Build(
            conceptId: "local-ai-tech-explainer",
            format: "youtube-longform",
            style: "clean-tech",
            characterId: "rio",
            worldId: "rio-bedroom",
            environmentType: "bedroom",
            species: "human");

        Assert.Equal("youtube-longform", context.Concept.Format);
        Assert.Equal(["rio"], context.Characters.Select(character => character.Id.Value));
        Assert.Equal(["rio-bedroom"], context.Worlds.Select(world => world.Id.Value));
    }

    [Fact]
    public void AnimeHorrorStoryBuilds()
    {
        var context = Build(
            conceptId: "anime-horror-story",
            format: "anime-short",
            style: "anime-cinematic",
            characterId: "yuki",
            worldId: "cyberpunk-alley",
            environmentType: "urban-alley",
            species: "human");

        Assert.Equal("anime-short", context.Concept.Format);
        Assert.Equal("anime-cinematic", context.Concept.Style);
        Assert.Equal(["yuki"], context.Characters.Select(character => character.Id.Value));
        Assert.Equal(["cyberpunk-alley"], context.Worlds.Select(world => world.Id.Value));
    }

    [Fact]
    public void Preschool3DStoryBuilds()
    {
        var context = Build(
            conceptId: "preschool-3d-story",
            format: "youtube-short",
            style: "3d-preschool",
            characterId: "milo",
            worldId: "preschool-playroom",
            environmentType: "playroom",
            species: "stylized-child");

        Assert.Equal("3d-preschool", context.Concept.Style);
        Assert.Equal(["milo"], context.Characters.Select(character => character.Id.Value));
        Assert.Equal(["preschool-playroom"], context.Worlds.Select(world => world.Id.Value));
    }

    [Fact]
    public void OneBuilderHandlesEveryFormatIdentically()
    {
        var cases = new[]
        {
            ("tech", "youtube-longform", "clean-tech", "rio", "bedroom", "bedroom", "human"),
            ("anime", "anime-short", "anime-cinematic", "yuki", "alley", "urban-alley", "human"),
            ("preschool", "youtube-short", "3d-preschool", "milo", "playroom", "playroom", "stylized-child")
        };

        foreach (var (conceptId, format, style, characterId, worldId, environmentType, species) in cases)
        {
            var context = Build(conceptId, format, style, characterId, worldId, environmentType, species);

            Assert.Equal(format, context.Concept.Format);
            Assert.Single(context.Characters);
            Assert.Single(context.Worlds);
            Assert.Single(context.Beats);
        }
    }

    private static StoryContextModel Build(
        string conceptId,
        string format,
        string style,
        string characterId,
        string worldId,
        string environmentType,
        string species)
    {
        var direction = StoryContextTestSupport.Direction(
            conceptId: conceptId,
            format: format,
            style: style);

        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60,
                    characterRefs: [characterId], worldRefs: [worldId])
            ],
            sourceConceptId: conceptId,
            targetDuration: 60);

        var characters = new CharacterBibleRegistry(
            [BibleTestSupport.Character(characterId, role: "protagonist", species: species)]);
        var worlds = new WorldBibleRegistry(
            [BibleTestSupport.World(worldId, environmentType: environmentType)]);

        var result = StoryContextTestSupport.Builder(characters, worlds)
            .Build(StoryContextTestSupport.Request(direction, plan));

        Assert.True(result.IsValid);
        return result.Context;
    }
}
