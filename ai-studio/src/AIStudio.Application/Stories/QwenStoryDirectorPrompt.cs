using System.Globalization;
using System.Text;
using AIStudio.Application.Creative;

namespace AIStudio.Application.Stories;

/// <summary>
/// Deterministic, token-efficient prompt builder for the Qwen-backed Story
/// Director. It carries only the approved <see cref="CreativeDirection"/>, the
/// caller-supplied <see cref="NarrativePattern"/> (ordered slots), the exact
/// StoryPlan JSON contract, and the narrative boundaries — never a provider,
/// model, path, or executable detail. Producing the same inputs always yields the
/// same prompt.
/// </summary>
public static class QwenStoryDirectorPrompt
{
    public const string SystemInstruction =
        "You are a story director. Turn an approved creative direction into a narrative " +
        "progression as one valid JSON story plan. Return only one valid JSON object, " +
        "without markdown fences or commentary.";

    /// <summary>Compact canonical response shape; also documents required nesting.</summary>
    public const string OutputContract =
        """{"id":"...","version":1,"sourceConceptId":"...","narrativePattern":"...","narrativePatternVersion":1,"targetDurationSeconds":60,"beats":[{"id":"beat-01","order":1,"role":"hook","purpose":"...","importance":"normal","targetDurationSeconds":9,"characterRefs":[],"worldRefs":[],"continuityFrom":[]}]}""";

    public static string Build(
        CreativeDirection direction,
        NarrativePattern pattern,
        StoryPlanId planId,
        StoryPlanVersion planVersion,
        int targetDurationSeconds)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(pattern);

        var concept = direction.Concept;
        var treatment = direction.Treatment;
        var durations = SuggestedDurations(pattern, targetDurationSeconds);

        var prompt = new StringBuilder()
            .AppendLine("Create one production-ready story plan for the approved creative direction below.")
            .AppendLine("Continue this exact story; never replace its topic, concept, or ending.")
            .AppendLine()
            .AppendLine("Approved creative direction (creative intent context):")
            .Append("Concept id: ").AppendLine(concept.Id.Value)
            .Append("Title: ").AppendLine(concept.Title)
            .Append("Description: ").AppendLine(concept.Description)
            .Append("Audience: ").AppendLine(concept.Audience)
            .Append("Format: ").AppendLine(concept.Format)
            .Append("Style: ").AppendLine(concept.Style)
            .Append("Story approach: ").AppendLine(treatment.StoryApproach)
            .Append("Hook treatment: ").AppendLine(treatment.HookTreatment)
            .Append("Pacing: ").AppendLine(treatment.Pacing)
            .Append("Visual strategy: ").AppendLine(treatment.VisualStrategy)
            .Append("Ending treatment: ").AppendLine(treatment.EndingTreatment);

        if (!string.IsNullOrWhiteSpace(treatment.Tone))
        {
            prompt.Append("Tone: ").AppendLine(treatment.Tone);
        }

        if (!string.IsNullOrWhiteSpace(treatment.TransitionStrategy))
        {
            prompt.Append("Transition strategy: ").AppendLine(treatment.TransitionStrategy);
        }

        prompt.AppendLine()
            .AppendLine("The creative-direction fields above are CONTEXT, not output instructions: translate the visual, hook, ending, and transition guidance into narrative events, state, and purpose, and never copy their shot, camera, or editing instructions.")
            .AppendLine()
            .AppendLine("Authoritative narrative pattern (fill it; never replace it):")
            .Append("Pattern id: ").AppendLine(pattern.Id.Value)
            .Append("Pattern version: ").AppendLine(Format(pattern.Version.Value))
            .Append("Display name: ").AppendLine(pattern.DisplayName)
            .AppendLine("Ordered beat slots (create exactly one beat per slot, in this order):");

        for (var index = 0; index < pattern.BeatSlots.Count; index++)
        {
            var slot = pattern.BeatSlots[index];
            prompt.Append("- ").Append(Format(index + 1)).Append(". role=").Append(slot.Role.Value)
                .Append(" required=").Append(slot.IsRequired ? "true" : "false")
                .Append(" suggestedTargetDurationSeconds=").Append(Format(durations[index]))
                .Append(" guidance=\"").Append(slot.Purpose).AppendLine("\"");
        }

        prompt.AppendLine()
            .Append("Target duration: ").Append(Format(targetDurationSeconds))
            .AppendLine(" seconds. The sum of every beat targetDurationSeconds must stay within 10% of that target.")
            .AppendLine()
            .AppendLine("Copy these exact header values into the JSON:")
            .Append("id = \"").Append(planId.Value).AppendLine("\"")
            .Append("version = ").AppendLine(Format(planVersion.Value))
            .Append("sourceConceptId = \"").Append(concept.Id.Value).AppendLine("\"")
            .Append("narrativePattern = \"").Append(pattern.Id.Value).AppendLine("\"")
            .Append("narrativePatternVersion = ").AppendLine(Format(pattern.Version.Value))
            .Append("targetDurationSeconds = ").AppendLine(Format(targetDurationSeconds))
            .AppendLine()
            .AppendLine("Return only one JSON object shaped exactly like:")
            .AppendLine(OutputContract)
            .AppendLine()
            .AppendLine("Rules:")
            .AppendLine("- Return exactly one story plan for the supplied narrative pattern.")
            .AppendLine("- Create one beat per pattern slot, in slot order; each beat role must equal its slot role.")
            .AppendLine("- Every id, role, and importance is a lowercase token of a-z, 0-9, '.', '_' or '-' only, starting with a letter or digit; no spaces, no version suffixes, no labels (for example beat-01, never 'beat 01' or 'beat-01 v1').")
            .AppendLine("- Beat order is 1-based and strictly increasing; beat ids are unique within the plan.")
            .AppendLine("- purpose is narrative intent (what happens in the beat and why it matters), never final spoken words.")
            .AppendLine("- importance is a lowercase token such as normal, major, or minor.")
            .AppendLine("- Every beat targetDurationSeconds is a positive integer; the sum must stay within 10% of the target duration.")
            .AppendLine("- continuityFrom may list only earlier beat ids defined in this same plan, never the beat itself and never a cycle; use [] for the first beat.")
            .AppendLine("- characterRefs and worldRefs are optional lowercase identifier lists; use [] when no established character or world is referenced. Never invent ids.")
            .AppendLine("- Do not write dialogue, narration scripts, shot lists, camera/lens/blocking directions, engine names, provider details, model names, file paths, URLs, or commands.")
            .AppendLine("- Every beat describes narrative events, state, and purpose only; translate the creative-context visual fields into what happens, never copy their shot or editing instructions.")
            .AppendLine("- Never use cinematography or editing execution language such as close-up, medium shot, wide shot, camera, camera angle, lens, mm, pan, tilt, dolly, rack focus, cut to, hard cut, cross-dissolve, zoom, frame number, shot number, or camera coordinates.")
            .AppendLine("- Narrative visual state is allowed and encouraged when it states what happens, for example the room becomes dark, the student notices the anomaly, the product appears, or the cat approaches the bowl.")
            .AppendLine("- Return only the properties shown in the contract above; do not add extra properties.")
            .AppendLine("- Do not wrap the object in another object or array, do not use markdown fences, and do not add commentary.");

        return prompt.ToString();
    }

    /// <summary>
    /// Suggests per-slot durations by slot weight, so the beats can satisfy the
    /// existing duration tolerance. The StoryPlanValidator remains authoritative
    /// after generation; this only guides the model.
    /// </summary>
    private static IReadOnlyList<int> SuggestedDurations(
        NarrativePattern pattern,
        int targetDurationSeconds)
    {
        var slots = pattern.BeatSlots;
        var durations = new int[slots.Count];
        var totalWeight = slots.Sum(slot => slot.DurationWeight);
        var assigned = 0;

        for (var index = 0; index < slots.Count; index++)
        {
            var isLast = index == slots.Count - 1;
            var duration = isLast
                ? Math.Max(StoryPlanValidator.MinimumDurationSeconds, targetDurationSeconds - assigned)
                : Math.Max(
                    StoryPlanValidator.MinimumDurationSeconds,
                    (int)Math.Round(
                        targetDurationSeconds * (slots[index].DurationWeight / totalWeight),
                        MidpointRounding.AwayFromZero));

            durations[index] = duration;
            assigned += duration;
        }

        return durations;
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
