using System.Text;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Application.Jobs.GenerateStoryboard;

internal static class GenerateStoryboardPrompt
{
    public const string SystemInstruction =
        "You are a storyboard planner. Return only one valid JSON object, " +
        "without markdown fences or commentary.";

    public static string Build(
        ContentProjectSnapshot project,
        GenerateScriptResult script,
        StoryContextModel? storyContext = null)
    {
        var builder = new StringBuilder()
            .AppendLine("Turn the approved script into a simple, ordered visual storyboard.")
            .Append("Project title: ").AppendLine(project.Title)
            .Append("Script title: ").AppendLine(script.Title)
            .Append("Opening hook: ").AppendLine(script.OpeningHook);

        for (var index = 0; index < script.Sections.Count; index++)
        {
            var section = script.Sections[index];
            builder
                .Append("Section ").Append(index + 1).Append(" heading: ").AppendLine(section.Heading)
                .Append("Section ").Append(index + 1).Append(" narration: ").AppendLine(section.Narration);
        }

        builder.Append("Closing: ").AppendLine(script.Closing);

        if (storyContext is not null)
        {
            AppendNarrativeContext(builder, storyContext);
        }

        return builder
            .AppendLine()
            .AppendLine("Return exactly these camelCase fields:")
            .AppendLine("title, scenes.")
            .AppendLine("scenes must be a non-empty ordered array of objects with exactly:")
            .AppendLine("heading, visual.")
            .AppendLine("Follow the script order. All text must be non-empty.")
            .AppendLine("visual describes what is shown: subject, action, environment, framing, composition, camera movement, and transition intent are all allowed when useful.")
            .AppendLine("Stay engine-neutral and implementation-free: never name providers, models, node graphs, render scripts, executables, file paths, URLs, or commands.")
            .AppendLine("Use the approved script as the spoken reference; do not rewrite, replace, or add dialogue or narration.")
            .ToString();
    }

    /// <summary>
    /// Renders the compact, already-filtered <see cref="StoryContextModel"/> so the
    /// storyboard can translate the narrative and spoken script into visual intent.
    /// It reuses the projected context instead of rebuilding upstream data, and never
    /// carries provider, path, or executable detail.
    /// </summary>
    private static void AppendNarrativeContext(StringBuilder builder, StoryContextModel context)
    {
        var concept = context.Concept;
        var story = context.Story;

        builder.AppendLine()
            .AppendLine("Narrative planning context (authoritative visual intent source):")
            .Append("Concept: ").AppendLine(concept.Title)
            .Append("Audience: ").AppendLine(concept.Audience)
            .Append("Format: ").Append(concept.Format).Append("; style: ").AppendLine(concept.Style)
            .Append("Story plan: ").Append(story.Id.Value)
            .Append(" v").Append(story.Version)
            .Append("; pattern ").Append(story.Pattern.Value)
            .Append(" v").Append(story.PatternVersion.Value)
            .Append("; target duration ").Append(story.TargetDurationSeconds).AppendLine("s")
            .AppendLine("Story beats (ordered). The approved script has one section per beat in the same order; keep storyboard scenes aligned to that order and add more than one scene for a beat only when it needs it:");

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
            builder.AppendLine("Characters (stable identity; keep it consistent across scenes):");

            foreach (var character in context.Characters)
            {
                builder.Append("- ").Append(character.Id.Value)
                    .Append(" (").Append(character.DisplayName).Append("): role=")
                    .Append(character.Identity.Role);

                if (!string.IsNullOrWhiteSpace(character.Identity.Species))
                {
                    builder.Append(", species=").Append(character.Identity.Species);
                }

                if (!string.IsNullOrWhiteSpace(character.Identity.VisualDescription))
                {
                    builder.Append("; appearance=").Append(character.Identity.VisualDescription);
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
            builder.AppendLine("Worlds (stable identity; keep them consistent across scenes):");

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
            builder.AppendLine("Current state (may change per scene; never treat it as identity):");

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
    }

    private static void AppendOptional(StringBuilder builder, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("; ").Append(name).Append('=').Append(value);
        }
    }
}
