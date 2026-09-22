using AIStudio.Application.Bibles;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Tests.Bibles;
using AIStudio.Tests.Stories;
using Xunit;

namespace AIStudio.Tests.StoryContext;

public sealed class StoryContextFilteringTests
{
    [Fact]
    public void IncludesOnlyReferencedCharacters()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        Assert.Equal(["hana", "rio"], context.Characters.Select(character => character.Id.Value));
    }

    [Fact]
    public void IncludesOnlyReferencedWorlds()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        Assert.Equal(["bedroom", "office"], context.Worlds.Select(world => world.Id.Value));
    }

    [Fact]
    public void RelationshipTargetsOutsideTheStoryAreExcluded()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        var rio = context.Characters.Single(character => character.Id.Value == "rio");

        Assert.Equal(["hana"], rio.Relationships.Select(relationship => relationship.Target.Value));
        Assert.Equal("sibling", rio.Relationships.Single().Type);
    }

    [Fact]
    public void TokenEfficiencyExcludesUnrelatedRegistryEntities()
    {
        var characters = new CharacterBibleRegistry(
            Enumerable.Range(1, 10).Select(index => BibleTestSupport.Character($"c{index:00}")));
        var worlds = new WorldBibleRegistry(
            Enumerable.Range(1, 8).Select(index => BibleTestSupport.World($"w{index:00}", environmentType: "office")));

        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60,
                    characterRefs: ["c03", "c07"], worldRefs: ["w02"])
            ],
            targetDuration: 60);

        var result = new StoryContextBuilder(characters, worlds)
            .Build(StoryContextTestSupport.Request(plan: plan));

        Assert.True(result.IsValid);
        Assert.Equal(["c03", "c07"], result.Context.Characters.Select(character => character.Id.Value));
        Assert.Equal(["w02"], result.Context.Worlds.Select(world => world.Id.Value));
        Assert.Equal(3, result.Context.Characters.Count + result.Context.Worlds.Count);
    }

    [Fact]
    public void UnresolvedCharacterReferenceIsReportedAndOmitted()
    {
        var characters = new CharacterBibleRegistry([BibleTestSupport.Character("hana")]);
        var worlds = new WorldBibleRegistry([BibleTestSupport.World("bedroom")]);

        var result = new StoryContextBuilder(characters, worlds).Build(StoryContextTestSupport.Request());

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == BibleIssueCodes.StoryCharacterReferenceUnknown);
        Assert.DoesNotContain("rio", result.Context.Characters.Select(character => character.Id.Value));
    }

    [Fact]
    public void UnresolvedWorldReferenceIsReportedAndOmitted()
    {
        var characters = new CharacterBibleRegistry([BibleTestSupport.Character("rio"), BibleTestSupport.Character("hana")]);
        var worlds = new WorldBibleRegistry([BibleTestSupport.World("bedroom")]);

        var result = new StoryContextBuilder(characters, worlds).Build(StoryContextTestSupport.Request());

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == BibleIssueCodes.StoryWorldReferenceUnknown);
        Assert.DoesNotContain("office", result.Context.Worlds.Select(world => world.Id.Value));
    }

    [Fact]
    public void LatestBibleVersionIsResolvedDeterministically()
    {
        var characters = new CharacterBibleRegistry(
        [
            BibleTestSupport.Character("rio", version: 1, hair: "short-black"),
            BibleTestSupport.Character("rio", version: 2, hair: "short-black-undercut")
        ]);
        var worlds = new WorldBibleRegistry([BibleTestSupport.World("bedroom")]);

        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60, characterRefs: ["rio"])],
            targetDuration: 60);

        var context = new StoryContextBuilder(characters, worlds)
            .Build(StoryContextTestSupport.Request(plan: plan))
            .Context;

        var rio = Assert.Single(context.Characters);
        Assert.Equal(2, rio.Version);
        Assert.Equal("short-black-undercut", rio.Identity.Hair);
    }

    [Fact]
    public void AssetReferencesAreExcludedByDefault()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        Assert.All(context.Characters, character => Assert.Empty(character.AssetReferences));
        Assert.All(context.Worlds, world => Assert.Empty(world.AssetReferences));
    }

    [Fact]
    public void AssetReferencesExposeSafeMetadataWhenRequested()
    {
        var context = StoryContextTestSupport.Builder()
            .Build(StoryContextTestSupport.Request(includeAssetReferences: true))
            .Context;

        var rio = context.Characters.Single(character => character.Id.Value == "rio");
        var bedroom = context.Worlds.Single(world => world.Id.Value == "bedroom");

        Assert.Equal("character-rio-front-v1", rio.AssetReferences.Single().AssetId.Value);
        Assert.Equal("visual-reference", rio.AssetReferences.Single().Purpose);
        Assert.Equal(2, rio.AssetReferences.Single().Version!.Value.Value);
        Assert.Equal("environment-bedroom-reference", bedroom.AssetReferences.Single().AssetId.Value);
    }
}
