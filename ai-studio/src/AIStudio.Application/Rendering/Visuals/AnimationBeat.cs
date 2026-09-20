namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// One choreography beat: what becomes visible/moves/progresses, when, and for how
/// long, expressed only with approved primitives. Element names are logical slots
/// the trusted composer knows (for example <c>card0</c>, <c>progress</c>,
/// <c>command</c>, <c>check</c>, <c>bubble1</c>).
/// </summary>
public sealed record AnimationBeat(
    double StartTime,
    double Duration,
    AnimationPrimitive Primitive,
    string Element,
    string? Text = null,
    double Value = 0);

/// <summary>
/// Ordered beats for one scene, bounded to the scene duration. Small by design: a
/// scene carries roughly two to four beats, not a full animation timeline.
/// </summary>
public sealed record SceneChoreography(IReadOnlyList<AnimationBeat> Beats)
{
    public const int MaxBeats = 12;

    public static readonly SceneChoreography Empty = new([]);

    public bool IsValid() =>
        Beats.Count <= MaxBeats
        && Beats.All(beat =>
            beat is not null
            && beat.StartTime >= 0
            && beat.Duration > 0
            && !string.IsNullOrWhiteSpace(beat.Element)
            && beat.Element.Length <= 64
            && Enum.IsDefined(beat.Primitive));

    public double EndTime => Beats.Count == 0 ? 0 : Beats.Max(beat => beat.StartTime + beat.Duration);
}
