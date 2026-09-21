namespace AIStudio.Application.Capabilities;

/// <summary>
/// How far a registered provider has progressed toward being usable. v1 computes
/// only <see cref="Registered"/> and <see cref="Enabled"/>. Live external-runtime
/// health is deliberately a later, separate layer: resolving a capability must not
/// require loading FLUX, contacting ComfyUI, launching Blender, or starting
/// VoxCPM/ACE-Step/Manim.
/// </summary>
public enum CapabilityAvailability
{
    /// <summary>The implementation exists in AI Studio (compile-time registration).</summary>
    Registered = 0,

    /// <summary>Configuration allows the provider to be used.</summary>
    Enabled = 1
}
