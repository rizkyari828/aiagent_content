using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class WorldBibleValidatorTests
{
    [Fact]
    public void ValidWorldHasNoIssues()
    {
        var world = BibleTestSupport.World(
            spatialTraits: ["single-window", "cluttered desk"],
            recurringProps: ["desk", "gaming-pc", "window"],
            continuityRules: ["desk remains beside window"],
            locations: [BibleTestSupport.Location("nook"), BibleTestSupport.Location("desk-area")]);

        Assert.Empty(world.Validate());
    }

    [Fact]
    public void InvalidIdentityIsReported()
    {
        var world = new WorldBible
        {
            DisplayName = string.Empty,
            Identity = new WorldIdentity { EnvironmentType = "Not Valid", VisualDescription = "" }
        };

        var codes = world.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.WorldBibleIdInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldBibleVersionInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldDisplayNameEmpty, codes);
        Assert.Contains(BibleIssueCodes.WorldEnvironmentTypeInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldVisualDescriptionInvalid, codes);
    }

    [Fact]
    public void InvalidAndDuplicateRecurringPropsAreReported()
    {
        var world = BibleTestSupport.World(recurringProps: ["desk", "desk", "Not Valid"]);

        var codes = world.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.WorldRecurringPropDuplicate, codes);
        Assert.Contains(BibleIssueCodes.WorldRecurringPropInvalid, codes);
    }

    [Fact]
    public void EmptyContinuityRuleAndSpatialTraitAreReported()
    {
        var world = BibleTestSupport.World(
            spatialTraits: [""],
            continuityRules: [" "]);

        var codes = world.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.WorldSpatialTraitInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldContinuityRuleInvalid, codes);
    }

    [Fact]
    public void InvalidAndDuplicateLocationsAreReported()
    {
        var world = BibleTestSupport.World(
            locations:
            [
                BibleTestSupport.Location("classroom"),
                BibleTestSupport.Location("classroom"),
                BibleTestSupport.Location("Not Valid")
            ]);

        var codes = world.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.WorldLocationDuplicate, codes);
        Assert.Contains(BibleIssueCodes.WorldLocationIdInvalid, codes);
    }

    [Fact]
    public void AssetReferencesAreValidated()
    {
        var world = BibleTestSupport.World(
            assetReferences: [BibleTestSupport.Asset("environment-bedroom-reference", "environment-reference")]);

        Assert.Empty(world.Validate());
    }

    [Fact]
    public void ValidWorldStateHasNoIssues()
    {
        var state = BibleTestSupport.WorldState(
            timeOfDay: "morning",
            weather: "rain",
            lighting: "on",
            temporaryProps: ["umbrella"],
            notes: "messy desk from the previous night");

        Assert.Empty(state.Validate());
    }

    [Fact]
    public void InvalidWorldStateIsReported()
    {
        var state = new WorldState
        {
            WorldRef = default,
            TimeOfDay = "Not Valid",
            TemporaryProps = ["Not Valid"],
            Notes = new string('x', CharacterBibleValidator.MaximumTextLength + 1)
        };

        var codes = state.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.WorldStateWorldRefInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldStateTokenInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldStateTemporaryPropInvalid, codes);
        Assert.Contains(BibleIssueCodes.WorldStateNotesInvalid, codes);
    }

    [Fact]
    public void HasLocationFindsDeclaredSubLocationsOnly()
    {
        var world = BibleTestSupport.World(locations: [BibleTestSupport.Location("classroom")]);

        Assert.True(world.HasLocation("classroom"));
        Assert.False(world.HasLocation("playground"));
    }
}
