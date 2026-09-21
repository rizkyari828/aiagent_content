using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class CharacterBibleValidatorTests
{
    [Fact]
    public void ValidCharacterHasNoIssues()
    {
        var character = BibleTestSupport.Character(
            distinguishingTraits: ["small scar above left eyebrow"],
            personalityTraits: ["curious", "persistent"]);

        Assert.Empty(character.Validate());
    }

    [Fact]
    public void InvalidIdentityIsReported()
    {
        var character = new CharacterBible
        {
            DisplayName = string.Empty,
            Identity = new CharacterIdentity { Role = "Not A Token" }
        };

        var codes = character.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.CharacterBibleIdInvalid, codes);
        Assert.Contains(BibleIssueCodes.CharacterBibleVersionInvalid, codes);
        Assert.Contains(BibleIssueCodes.CharacterDisplayNameEmpty, codes);
        Assert.Contains(BibleIssueCodes.CharacterRoleInvalid, codes);
    }

    [Fact]
    public void InvalidIdentityTokensAreReported()
    {
        var character = BibleTestSupport.Character(species: "Not Valid", hair: "short black");

        var codes = character.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.CharacterIdentityTokenInvalid, codes);
    }

    [Fact]
    public void EmptyAndDuplicateTraitsAreReported()
    {
        var character = BibleTestSupport.Character(
            personalityTraits: ["curious", "curious"],
            distinguishingTraits: [""]);

        var codes = character.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.CharacterTraitInvalid, codes);
        Assert.Contains(BibleIssueCodes.CharacterTraitDuplicate, codes);
    }

    [Fact]
    public void DeclaredBaselineVariantAndVariantsAreAccepted()
    {
        var character = BibleTestSupport.Character(
            baselineVariant: "winter-jacket",
            variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("winter-jacket")]);

        Assert.Empty(character.Validate());
    }

    [Fact]
    public void BaselineVariantMustBeDeclared()
    {
        var character = BibleTestSupport.Character(
            baselineVariant: "winter-jacket",
            variants: [BibleTestSupport.Variant("default")]);

        Assert.Contains(
            BibleIssueCodes.CharacterBaselineVariantInvalid,
            character.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void DuplicateVariantsAreReported()
    {
        var character = BibleTestSupport.Character(
            variants: [BibleTestSupport.Variant("school-uniform"), BibleTestSupport.Variant("school-uniform")]);

        Assert.Contains(
            BibleIssueCodes.CharacterVariantDuplicate,
            character.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void InvalidVariantIdIsReported()
    {
        var character = BibleTestSupport.Character(variants: [BibleTestSupport.Variant("Not Valid")]);

        Assert.Contains(
            BibleIssueCodes.CharacterVariantIdInvalid,
            character.Validate().Select(issue => issue.Code));
    }

    [Fact]
    public void RelationshipSelfReferenceAndBadTypeAreReported()
    {
        var character = BibleTestSupport.Character(
            relationships: [BibleTestSupport.Relationship("rio", "Not Valid")]);

        var codes = character.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.CharacterRelationshipSelfReference, codes);
        Assert.Contains(BibleIssueCodes.CharacterRelationshipTypeInvalid, codes);
    }

    [Fact]
    public void DataDrivenRelationshipTypeIsAccepted()
    {
        var character = BibleTestSupport.Character(
            relationships: [BibleTestSupport.Relationship("hana", "long-lost-sibling")]);

        Assert.Empty(character.Validate());
    }

    [Fact]
    public void AssetReferencesAreValidatedAndDeduplicated()
    {
        var character = BibleTestSupport.Character(
            assetReferences:
            [
                BibleTestSupport.Asset("character-rio-front-v1", "visual-reference"),
                BibleTestSupport.Asset("character-rio-front-v1", "visual-reference"),
                BibleTestSupport.Asset("character-rio-front-v1", "Not Valid")
            ]);

        var codes = character.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.AssetReferenceDuplicate, codes);
        Assert.Contains(BibleIssueCodes.AssetReferencePurposeInvalid, codes);
    }

    [Fact]
    public void ValidCharacterStateHasNoIssues()
    {
        var state = BibleTestSupport.CharacterState(
            emotion: "tired",
            pose: "standing",
            action: "pointing",
            worldRef: "rio-bedroom",
            heldProps: ["laptop"]);

        Assert.Empty(state.Validate());
    }

    [Fact]
    public void InvalidCharacterStateIsReported()
    {
        var state = new CharacterState
        {
            CharacterRef = default,
            Variant = "Not Valid",
            Emotion = "Very Tired",
            WorldRef = new WorldBibleId(),
            HeldProps = ["Not Valid"]
        };

        var codes = state.Validate().Select(issue => issue.Code).ToList();

        Assert.Contains(BibleIssueCodes.CharacterStateCharacterRefInvalid, codes);
        Assert.Contains(BibleIssueCodes.CharacterStateVariantInvalid, codes);
        Assert.Contains(BibleIssueCodes.CharacterStateTokenInvalid, codes);
        Assert.Contains(BibleIssueCodes.CharacterStateWorldRefInvalid, codes);
    }

    [Fact]
    public void UnknownVariantIsRejectedAgainstItsCharacter()
    {
        var character = BibleTestSupport.Character(
            variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("winter-jacket")]);

        var unknown = BibleTestSupport.CharacterState(variant: "space-suit");
        var approved = BibleTestSupport.CharacterState(variant: "winter-jacket");

        Assert.Contains(
            BibleIssueCodes.CharacterStateVariantUnknown,
            unknown.Validate(character).Select(issue => issue.Code));
        Assert.Empty(approved.Validate(character));
    }
}
