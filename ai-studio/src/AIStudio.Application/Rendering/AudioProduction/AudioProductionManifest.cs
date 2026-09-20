namespace AIStudio.Application.Rendering.AudioProduction;

/// <summary>
/// One resolved audio production stage recorded in the workspace manifest.
/// <see cref="Fingerprint"/> captures the effective generation inputs so a retry
/// reuses the artifact only when those inputs are unchanged.
/// </summary>
public sealed record AudioProductionStage(
    string Fingerprint,
    string Path,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int SampleRate,
    int Channels);

/// <summary>
/// One per-scene narration artifact plus its resolved position on the production
/// timeline. The measured narration duration drives the scene length; the reviewed
/// narration text is retained so subtitles and choreography use the same source.
/// </summary>
public sealed record AudioSceneStage(
    int SceneIndex,
    string NarrationPath,
    string ContentHash,
    long ByteSize,
    int SampleRate,
    int Channels,
    double NarrationDurationSeconds,
    double NarrationStartSeconds,
    double VisualStartSeconds,
    double VisualDurationSeconds,
    double LeadSeconds,
    double HoldSeconds,
    string NarrationText);

/// <summary>One narration clip placed at an absolute offset during assembly.</summary>
public sealed record NarrationSegment(string AbsolutePath, double StartSeconds);

/// <summary>
/// Milestone-specific durable manifest for one production's audio stages. It is a
/// single small JSON file under the approved asset root; it is deliberately not a
/// generic cache or content-addressed store.
/// </summary>
public sealed record AudioProductionManifest(
    string Version,
    IReadOnlyDictionary<string, AudioProductionStage> Stages,
    IReadOnlyList<AudioSceneStage>? Scenes = null,
    double TransitionSeconds = 0,
    double NarrationDurationSeconds = 0,
    double TotalDurationSeconds = 0);

/// <summary>A verified audio artifact resolved from the workspace.</summary>
public sealed record AudioProductionArtifact(
    string RelativePath,
    string AbsolutePath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int SampleRate,
    int Channels);
