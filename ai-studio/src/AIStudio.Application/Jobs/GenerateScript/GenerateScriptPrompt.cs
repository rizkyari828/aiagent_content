using System.Text;
using AIStudio.Application.Content;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Application.Jobs.GenerateScript;

internal static class GenerateScriptPrompt
{
    public const string SystemInstruction =
        "You are a script writer. Return only one valid JSON object, " +
        "without markdown fences or commentary.";

    public static string Build(
        ContentProjectSnapshot project,
        GenerateScriptJobPayload payload,
        StoryContextModel? storyContext = null)
    {
        var idea = payload.SelectedIdea;
        var builder = new StringBuilder()
            .AppendLine("Write a practical first-draft video script from the selected idea.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Project brief: ").AppendLine(project.Brief ?? "Not provided")
            .Append("Idea title: ").AppendLine(idea.Title)
            .Append("Idea hook: ").AppendLine(idea.Hook)
            .Append("Idea summary: ").AppendLine(idea.Summary)
            .Append("Idea angle: ").AppendLine(idea.Angle)
            .Append("Target audience: ").AppendLine(idea.TargetAudience)
            .Append("Suggested format: ").AppendLine(idea.SuggestedFormat)
            .Append("Response language: ").AppendLine(payload.Language);

        if (storyContext is not null)
        {
            AppendNarrativeContext(builder, storyContext);
        }

        builder
            .AppendLine()
            .AppendLine("Script fields are spoken content:")
            .AppendLine("- openingHook, every sections[].narration, and closing are the exact words intended to be spoken aloud; write natural spoken language there.")
            .AppendLine("- A sections[].heading is a short title or label, not necessarily spoken.")
            .AppendLine("- Never put camera, shot, lens, editing, acting, sound-effect, visual-effect, or scene-direction instructions inside openingHook, narration, or closing.")
            .AppendLine("- Creative guidance and beat purposes describe intent; translate them into spoken words instead of copying their production directions.")
            .AppendLine()
            .AppendLine("Return exactly these camelCase fields:")
            .AppendLine("title, openingHook, sections, closing.")
            .AppendLine("sections must be a non-empty ordered array of objects with exactly:")
            .AppendLine("heading, narration.")
            .AppendLine("All text must be non-empty and written in the requested language.");

        return builder.ToString();
    }

    /// <summary>
    /// Renders the compact, already-filtered <see cref="StoryContext"/> the Script
    /// layer must express as spoken content. It reuses the projected context instead
    /// of reconstructing upstream narrative data, and carries no camera/shot,
    /// provider, path, or executable detail.
    /// </summary>
    private static void AppendNarrativeContext(StringBuilder builder, StoryContextModel context)
    {
        var concept = context.Concept;
        var story = context.Story;

        builder.AppendLine()
            .AppendLine("Narrative planning context (authoritative; preserve this progression):")
            .Append("Concept: ").AppendLine(concept.Title)
            .Append("Audience: ").AppendLine(concept.Audience)
            .Append("Format: ").Append(concept.Format).Append("; style: ").AppendLine(concept.Style)
            .Append("Story plan: ").Append(story.Id.Value)
            .Append(" v").Append(story.Version)
            .Append("; pattern ").Append(story.Pattern.Value)
            .Append(" v").Append(story.PatternVersion.Value)
            .Append("; target duration ").Append(story.TargetDurationSeconds).AppendLine("s")
            .AppendLine("Beats (in order; write one section per beat, in this order):");

        foreach (var beat in context.Beats)
        {
            builder.Append("- ").Append(beat.Id.Value)
                .Append(" (").Append(beat.Role.Value).Append(", ").Append(beat.DurationSeconds).Append("s")
                .Append(", importance ").Append(beat.Importance).Append("): ").Append(beat.Purpose);

            if (beat.CharacterRefs.Count > 0)
            {
                builder.Append(" [characters: ").Append(string.Join(", ", beat.CharacterRefs)).Append(']');
            }

            if (beat.WorldRefs.Count > 0)
            {
                builder.Append(" [worlds: ").Append(string.Join(", ", beat.WorldRefs)).Append(']');
            }

            builder.AppendLine();
        }

        if (context.Characters.Count > 0)
        {
            builder.AppendLine("Characters (identity is authoritative; do not change it):");

            foreach (var character in context.Characters)
            {
                builder.Append("- ").Append(character.Id.Value)
                    .Append(" (").Append(character.DisplayName).Append("): role=")
                    .Append(character.Identity.Role);

                if (!string.IsNullOrWhiteSpace(character.Identity.Species))
                {
                    builder.Append(", species=").Append(character.Identity.Species);
                }

                if (character.PersonalityTraits.Count > 0)
                {
                    builder.Append("; personality=").Append(string.Join(", ", character.PersonalityTraits));
                }

                builder.AppendLine();
            }
        }

        if (context.Worlds.Count > 0)
        {
            builder.AppendLine("Worlds (identity is authoritative; do not change it):");

            foreach (var world in context.Worlds)
            {
                builder.Append("- ").Append(world.Id.Value)
                    .Append(" (").Append(world.DisplayName).Append("): environment=")
                    .Append(world.Identity.EnvironmentType);

                if (world.RecurringProps.Count > 0)
                {
                    builder.Append("; recurringProps=").Append(string.Join(", ", world.RecurringProps));
                }

                builder.AppendLine();
            }
        }

        if (context.States is { } states
            && (states.Characters.Count > 0 || states.Worlds.Count > 0))
        {
            builder.AppendLine("Current state (may change per beat; never treat it as identity):");

            foreach (var state in states.Characters)
            {
                builder.Append("- character ").Append(state.CharacterRef.Value)
                    .Append(": variant=").Append(state.Variant);

                AppendOptional(builder, "emotion", state.Emotion);
                AppendOptional(builder, "pose", state.Pose);
                AppendOptional(builder, "action", state.Action);
                builder.AppendLine();
            }

            foreach (var state in states.Worlds)
            {
                builder.Append("- world ").Append(state.WorldRef.Value);
                AppendOptional(builder, "timeOfDay", state.TimeOfDay);
                AppendOptional(builder, "weather", state.Weather);
                AppendOptional(builder, "lighting", state.Lighting);
                builder.AppendLine();
            }
        }

        builder.AppendLine("Script ownership and boundaries:")
            .AppendLine("- Script owns the exact spoken words: narration, dialogue, spoken hooks, spoken transitions, and a call to action where appropriate.")
            .AppendLine("- Preserve the concept, audience, known character identity, beat order, and each beat's purpose.")
            .AppendLine("- Do not add, remove, or reorder narrative beats, and do not replace the story plan with a different story.")
            .AppendLine("- Do not emit camera or shot instructions, lens details, image/video/audio generation commands, provider or model names, or filesystem paths.");
    }

    private static void AppendOptional(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("; ").Append(name).Append('=').Append(value);
        }
    }
}
