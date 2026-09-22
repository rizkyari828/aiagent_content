namespace AIStudio.Application.Bibles;

/// <summary>
/// One deterministic structural problem found in a character/world bible, a scene
/// state, or an asset reference. A bible with no issues is valid declarative data;
/// nothing here scores visual similarity, calls a model, or executes anything.
/// </summary>
public sealed record BibleIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable bible validation codes, safe to surface to future data loaders.</summary>
public static class BibleIssueCodes
{
    public const string CharacterBibleIdInvalid = "character_bible_id_invalid";
    public const string CharacterBibleVersionInvalid = "character_bible_version_invalid";
    public const string CharacterDisplayNameEmpty = "character_display_name_empty";
    public const string CharacterRoleInvalid = "character_identity_role_invalid";
    public const string CharacterIdentityTokenInvalid = "character_identity_token_invalid";
    public const string CharacterVisualDescriptionInvalid = "character_identity_visual_description_invalid";
    public const string CharacterTraitInvalid = "character_trait_invalid";
    public const string CharacterTraitDuplicate = "character_trait_duplicate";
    public const string CharacterBaselineVariantInvalid = "character_baseline_variant_invalid";
    public const string CharacterVariantIdInvalid = "character_variant_id_invalid";
    public const string CharacterVariantDuplicate = "character_variant_duplicate";
    public const string CharacterRelationshipTargetInvalid = "character_relationship_target_invalid";
    public const string CharacterRelationshipTypeInvalid = "character_relationship_type_invalid";
    public const string CharacterRelationshipSelfReference = "character_relationship_self_reference";
    public const string CharacterStateCharacterRefInvalid = "character_state_character_ref_invalid";
    public const string CharacterStateVariantInvalid = "character_state_variant_invalid";
    public const string CharacterStateVariantUnknown = "character_state_variant_unknown";
    public const string CharacterStateTokenInvalid = "character_state_token_invalid";
    public const string CharacterStateWorldRefInvalid = "character_state_world_ref_invalid";

    public const string WorldBibleIdInvalid = "world_bible_id_invalid";
    public const string WorldBibleVersionInvalid = "world_bible_version_invalid";
    public const string WorldDisplayNameEmpty = "world_display_name_empty";
    public const string WorldEnvironmentTypeInvalid = "world_identity_environment_type_invalid";
    public const string WorldVisualDescriptionInvalid = "world_identity_visual_description_invalid";
    public const string WorldSpatialTraitInvalid = "world_spatial_trait_invalid";
    public const string WorldRecurringPropInvalid = "world_recurring_prop_invalid";
    public const string WorldRecurringPropDuplicate = "world_recurring_prop_duplicate";
    public const string WorldContinuityRuleInvalid = "world_continuity_rule_invalid";
    public const string WorldLocationIdInvalid = "world_location_id_invalid";
    public const string WorldLocationDuplicate = "world_location_duplicate";
    public const string WorldStateWorldRefInvalid = "world_state_world_ref_invalid";
    public const string WorldStateTokenInvalid = "world_state_token_invalid";
    public const string WorldStateTemporaryPropInvalid = "world_state_temporary_prop_invalid";
    public const string WorldStateNotesInvalid = "world_state_notes_invalid";

    public const string AssetReferenceIdInvalid = "asset_reference_id_invalid";
    public const string AssetReferenceVersionInvalid = "asset_reference_version_invalid";
    public const string AssetReferencePurposeInvalid = "asset_reference_purpose_invalid";
    public const string AssetReferenceVariantInvalid = "asset_reference_variant_invalid";
    public const string AssetReferenceDuplicate = "asset_reference_duplicate";

    public const string StoryCharacterReferenceUnknown = "story_character_reference_unknown";
    public const string StoryWorldReferenceUnknown = "story_world_reference_unknown";
}
