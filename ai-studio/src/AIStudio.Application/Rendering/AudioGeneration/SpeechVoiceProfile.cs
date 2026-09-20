namespace AIStudio.Application.Rendering.AudioGeneration;

/// <summary>
/// Curated narration voice presets. Free-form voice-control text is intentionally
/// not part of the public contract; each preset maps to one approved internal
/// Indonesian voice description inside the provider.
/// </summary>
public enum SpeechVoiceProfile
{
    Formal = 0,
    Playful = 1,
    Energetic = 2,
    Documentary = 3
}
