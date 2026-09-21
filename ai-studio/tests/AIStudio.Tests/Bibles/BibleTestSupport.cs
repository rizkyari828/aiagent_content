using AIStudio.Application.Bibles;

namespace AIStudio.Tests.Bibles;

internal static class BibleTestSupport
{
    public static CharacterBible Character(
        string id = "rio",
        int version = 1,
        string? displayName = null,
        string role = "protagonist",
        string? species = "human",
        string? hair = "short-black",
        string? eyes = "brown",
        IReadOnlyList<string>? distinguishingTraits = null,
        IReadOnlyList<string>? personalityTraits = null,
        string baselineVariant = CharacterBible.DefaultVariant,
        IReadOnlyList<CharacterVariant>? variants = null,
        IReadOnlyList<CharacterRelationship>? relationships = null,
        IReadOnlyList<AssetReference>? assetReferences = null) =>
        new()
        {
            Id = new CharacterBibleId(id),
            Version = new CharacterBibleVersion(version),
            DisplayName = displayName ?? id,
            Identity = new CharacterIdentity
            {
                Role = role,
                Species = species,
                Hair = hair,
                Eyes = eyes,
                DistinguishingTraits = distinguishingTraits ?? []
            },
            PersonalityTraits = personalityTraits ?? [],
            BaselineVariant = baselineVariant,
            Variants = variants ?? [],
            Relationships = relationships ?? [],
            AssetReferences = assetReferences ?? []
        };

    public static CharacterVariant Variant(string id, string? displayName = null) =>
        new() { Id = id, DisplayName = displayName };

    public static CharacterRelationship Relationship(string target, string type) =>
        new() { Target = new CharacterBibleId(target), Type = type };

    public static AssetReference Asset(
        string assetId,
        string purpose = "visual-reference",
        string? variant = null) =>
        new() { AssetId = new AssetReferenceId(assetId), Purpose = purpose, Variant = variant };

    public static CharacterState CharacterState(
        string characterRef = "rio",
        string variant = CharacterBible.DefaultVariant,
        string? emotion = null,
        string? pose = null,
        string? action = null,
        string? worldRef = null,
        IReadOnlyList<string>? heldProps = null) =>
        new()
        {
            CharacterRef = new CharacterBibleId(characterRef),
            Variant = variant,
            Emotion = emotion,
            Pose = pose,
            Action = action,
            WorldRef = worldRef is null ? null : new WorldBibleId(worldRef),
            HeldProps = heldProps ?? []
        };

    public static WorldBible World(
        string id = "rio-bedroom",
        int version = 1,
        string? displayName = null,
        string environmentType = "bedroom",
        string visualDescription = "a small bedroom with a desk beside the window",
        IReadOnlyList<string>? spatialTraits = null,
        IReadOnlyList<string>? recurringProps = null,
        IReadOnlyList<string>? continuityRules = null,
        IReadOnlyList<WorldLocation>? locations = null,
        IReadOnlyList<AssetReference>? assetReferences = null) =>
        new()
        {
            Id = new WorldBibleId(id),
            Version = new WorldBibleVersion(version),
            DisplayName = displayName ?? id,
            Identity = new WorldIdentity
            {
                EnvironmentType = environmentType,
                VisualDescription = visualDescription,
                SpatialTraits = spatialTraits ?? []
            },
            RecurringProps = recurringProps ?? [],
            ContinuityRules = continuityRules ?? [],
            Locations = locations ?? [],
            AssetReferences = assetReferences ?? []
        };

    public static WorldLocation Location(string id, string? displayName = null) =>
        new() { Id = id, DisplayName = displayName };

    public static WorldState WorldState(
        string worldRef = "rio-bedroom",
        string? timeOfDay = null,
        string? weather = null,
        string? lighting = null,
        IReadOnlyList<string>? temporaryProps = null,
        string? notes = null) =>
        new()
        {
            WorldRef = new WorldBibleId(worldRef),
            TimeOfDay = timeOfDay,
            Weather = weather,
            Lighting = lighting,
            TemporaryProps = temporaryProps ?? [],
            Notes = notes
        };
}
