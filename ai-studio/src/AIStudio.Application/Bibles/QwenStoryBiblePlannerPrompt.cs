using System.Text;
using AIStudio.Application.Creative;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Deterministic, token-efficient prompt builder for the Qwen Story Bible Planner.
/// It carries only the approved creative direction and the existing story plan —
/// never Script, Storyboard, media, asset paths, or provider detail — and states the
/// exact JSON contract and stable-identity rules. Same inputs always yield the same
/// prompt.
/// </summary>
public static class QwenStoryBiblePlannerPrompt
{
    public const string SystemInstruction =
        "You are a story bible planner. Identify the stable characters and worlds a story " +
        "needs, then say which beats use them. Return only one valid JSON object, without " +
        "markdown fences or commentary.";

    /// <summary>Compact canonical response shape; also documents required nesting.</summary>
    public const string OutputContract =
        """{"characterBibles":[{"id":"...","version":1,"displayName":"...","identity":{"role":"...","species":"...","agePresentation":"...","bodyStyle":"...","hair":"...","eyes":"...","visualDescription":"...","distinguishingTraits":["..."]},"personalityTraits":["..."],"baselineVariant":"default","variants":[],"relationships":[],"assetReferences":[]}],"worldBibles":[{"id":"...","version":1,"displayName":"...","identity":{"environmentType":"...","visualDescription":"...","spatialTraits":["..."]},"recurringProps":["..."],"continuityRules":["..."],"locations":[],"assetReferences":[]}],"beatGroundings":[{"beatId":"...","characterRefs":["..."],"worldRefs":["..."]}]}""";

    public static string Build(CreativeDirection direction, StoryPlan storyPlan)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(storyPlan);

        var concept = direction.Concept;
        var treatment = direction.Treatment;

        var prompt = new StringBuilder()
            .AppendLine("Propose the stable story bible for the story plan below.")
            .AppendLine("Decide WHO recurs and WHERE the story recurs, then which beats use them. Do not invent a different story.")
            .AppendLine()
            .AppendLine("Approved creative direction (context only):")
            .Append("Concept title: ").AppendLine(concept.Title)
            .Append("Concept description: ").AppendLine(concept.Description)
            .Append("Audience: ").AppendLine(concept.Audience)
            .Append("Format: ").Append(concept.Format).Append("; style: ").AppendLine(concept.Style)
            .Append("Story approach: ").AppendLine(treatment.StoryApproach)
            .AppendLine()
            .Append("Story plan: ").Append(storyPlan.Id.Value)
            .Append(" v").Append(storyPlan.Version.Value)
            .Append("; pattern ").Append(storyPlan.NarrativePattern.Value)
            .Append(" v").Append(storyPlan.NarrativePatternVersion.Value)
            .Append("; target duration ").Append(storyPlan.TargetDurationSeconds).AppendLine("s")
            .AppendLine("Beats (do not change their ids, order, role, purpose, or duration):");

        foreach (var beat in storyPlan.Beats.OrderBy(beat => beat.Order))
        {
            prompt.Append("- ").Append(beat.Id.Value)
                .Append(" (order ").Append(beat.Order)
                .Append(", role ").Append(beat.Role.Value)
                .Append(", ").Append(beat.TargetDurationSeconds).AppendLine("s): ").Append(beat.Purpose);
        }

        prompt.AppendLine()
            .AppendLine("Return only one JSON object shaped exactly like:")
            .AppendLine(OutputContract)
            .AppendLine()
            .AppendLine("Rules:")
            .AppendLine("- Identify only the stable characters and stable worlds/locations the story needs. Include a character or world only when it is genuinely relevant.")
            .AppendLine("- Character and world ids are lowercase tokens of a-z, 0-9, '.', '_' or '-' only, starting with a letter or digit, for example student-01 or student-bedroom. Copy the exact id token: never include a version suffix, label, or space (write \"student-01\" with version 1, never \"student-01 v1\").")
            .AppendLine("- version is a separate positive integer; baselineVariant defaults to \"default\".")
            .AppendLine("- Once an id is defined, every beatGrounding must reuse that exact same id token. Never create several ids for the same recurring character or world.")
            .AppendLine("- characterBibles hold stable identity only (role, species, age presentation, body style, hair, eyes, visual description, distinguishing traits, personality, stable relationships, approved variants). Never put temporary scene state (emotion, pose, action, current location, held props) into a bible.")
            .AppendLine("- worldBibles hold stable environment identity only (environment type, visual description, spatial traits, recurring props, continuity rules, locations). Never put temporary state (current lighting, weather, time of day, scattered props) into a bible.")
            .AppendLine("- variants are approved appearance variants only; never encode emotion, pose, or action as a variant. relationships must target another character proposed in this same response, never itself.")
            .AppendLine("- assetReferences must always be an empty array: do not invent asset ids, file paths, URLs, model names, or provider workflows.")
            .AppendLine("- beatGroundings is an explicit list: every beat you decide to ground once, with beatId, characterRefs, and worldRefs. Empty arrays are allowed when a beat needs no character or world. Never apply one assignment to every beat implicitly.")
            .AppendLine("- Do not write dialogue, narration, or script lines. Do not give camera, shot, or storyboard directions. Do not rewrite the story plan.")
            .AppendLine("- Never include provider, model, engine, filesystem path, URL, or command detail.")
            .AppendLine("- Return only the properties shown in the contract above; do not add extra properties.")
            .AppendLine("- Do not wrap the object in another object or array, do not use markdown fences, and do not add commentary.");

        return prompt.ToString();
    }
}
