namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Resolved visual state at one instant of a scene. It is produced by
/// <see cref="ChoreographyEvaluator"/> and consumed by
/// <see cref="SceneVisualSvg.ComposeFrame"/>; it carries only which logical
/// elements are revealed and a few bounded scalar states.
/// </summary>
public sealed record SceneVisualFrameState(
    IReadOnlySet<string> Revealed,
    IReadOnlySet<string> Controlled,
    double Progress,
    string? CommandText,
    bool Typing,
    bool Completion,
    bool NoteVisible,
    double Pulse,
    string? PulseElement)
{
    public static readonly SceneVisualFrameState Full = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        Progress: -1,
        CommandText: null,
        Typing: false,
        Completion: false,
        NoteVisible: true,
        Pulse: 0,
        PulseElement: null);

    public bool IsRevealed(string element) => Revealed.Contains(element);

    /// <summary>
    /// Number of logically revealed indexed elements (for example <c>card</c>) when
    /// the choreography controls them, or <paramref name="total"/> otherwise.
    /// </summary>
    public int RevealedCount(string prefix, int total)
    {
        if (!Controlled.Any(element => element.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return total;
        }

        var count = 0;
        for (var index = 0; index < total; index++)
        {
            if (Revealed.Contains($"{prefix}{index}"))
            {
                count = index + 1;
            }
        }

        return Math.Clamp(count, 0, total);
    }
}

/// <summary>
/// Deterministic evaluator for a scene's choreography. It converts beats into a
/// bounded frame state using approved primitive semantics only. No code is
/// generated; the output is plain data.
/// </summary>
public static class ChoreographyEvaluator
{
    public static SceneVisualFrameState Evaluate(
        SceneChoreography choreography,
        double timeSeconds,
        double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(choreography);

        if (!choreography.IsValid())
        {
            return SceneVisualFrameState.Full;
        }

        var revealed = new HashSet<string>(StringComparer.Ordinal);
        var controlled = new HashSet<string>(
            choreography.Beats.Select(beat => beat.Element),
            StringComparer.Ordinal);
        var progress = -1d;
        string? command = null;
        var typing = false;
        var completion = false;
        var pulse = 0d;
        string? pulseElement = null;
        var bounded = Math.Max(0.001, durationSeconds);

        foreach (var beat in choreography.Beats)
        {
            if (beat.StartTime >= bounded)
            {
                continue;
            }

            var p = Progress(beat, timeSeconds);

            switch (beat.Primitive)
            {
                case AnimationPrimitive.Progress:
                    progress = p;
                    revealed.Add(beat.Element);
                    break;

                case AnimationPrimitive.TypeText:
                    revealed.Add(beat.Element);
                    if (!string.IsNullOrEmpty(beat.Text))
                    {
                        var length = (int)Math.Round(Math.Clamp(p, 0, 1) * beat.Text!.Length);
                        command = beat.Text[..Math.Clamp(length, 0, beat.Text.Length)];
                    }

                    break;

                case AnimationPrimitive.Checkmark:
                    if (p >= 0.5 || timeSeconds >= beat.StartTime + beat.Duration)
                    {
                        completion = true;
                        revealed.Add(beat.Element);
                    }

                    break;

                case AnimationPrimitive.ScalePulse:
                    if (string.Equals(beat.Element, "typing", StringComparison.Ordinal))
                    {
                        typing = timeSeconds >= beat.StartTime
                            && timeSeconds <= beat.StartTime + beat.Duration;
                    }
                    else if (timeSeconds >= beat.StartTime
                        && timeSeconds <= beat.StartTime + beat.Duration)
                    {
                        pulse = Math.Max(pulse, Math.Sin(p * Math.PI));
                        pulseElement = beat.Element;
                    }

                    break;

                case AnimationPrimitive.FadeOut:
                case AnimationPrimitive.SlideOut:
                    // Elements start revealed; outgoing beats are intentionally
                    // minimal in v1 and do not remove already revealed content.
                    break;

                default:
                    if (timeSeconds >= beat.StartTime)
                    {
                        revealed.Add(beat.Element);
                    }

                    break;
            }
        }

        return new SceneVisualFrameState(
            revealed,
            controlled,
            progress,
            command,
            typing,
            completion,
            NoteVisible: !HasNoteBeat(choreography) || revealed.Contains("note"),
            pulse,
            pulseElement);
    }

    private static bool HasNoteBeat(SceneChoreography choreography) =>
        choreography.Beats.Any(beat => string.Equals(beat.Element, "note", StringComparison.Ordinal));

    private static double Progress(AnimationBeat beat, double timeSeconds)
    {
        if (timeSeconds <= beat.StartTime)
        {
            return 0;
        }

        if (timeSeconds >= beat.StartTime + beat.Duration)
        {
            return 1;
        }

        var linear = (timeSeconds - beat.StartTime) / beat.Duration;
        return 1 - Math.Pow(1 - linear, 3);
    }
}
