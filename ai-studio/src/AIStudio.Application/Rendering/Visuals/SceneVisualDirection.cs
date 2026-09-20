namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Narrow, structured visual direction for one scene: what it communicates, the
/// preferred engine and its configured fallback, the trusted template to use, the
/// choreography beats, and the required on-screen elements. It is data only; no
/// executable content or template path ever crosses this boundary.
/// </summary>
public sealed record SceneVisualDirection(
    int SceneIndex,
    SceneVisualIntent Intent,
    SceneVisualComposition Composition,
    SceneVisualEngine PreferredEngine,
    SceneVisualEngine FallbackEngine,
    SceneAnimationTemplate ManimTemplate,
    SceneThreeDTemplate ThreeDTemplate,
    SceneChoreography Choreography,
    IReadOnlyList<string> RequiredElements,
    string MotionStyle,
    double DurationSeconds);

/// <summary>
/// Resolved routing decision for one scene: the engine the intent wanted, the
/// engine actually selected after availability, and whether a fallback was used.
/// </summary>
public sealed record SceneVisualRouteResult(
    SceneVisualEngine IntendedEngine,
    SceneVisualEngine SelectedEngine,
    bool IsFallback,
    string Reason,
    SceneAnimationTemplate ManimTemplate,
    SceneThreeDTemplate ThreeDTemplate);
