namespace AIStudio.Application.Rendering.AudioGeneration;

/// <summary>
/// A request to synthesize one narration clip. The text is data: providers never
/// accept Python code, model paths, executable paths, or filter strings. The
/// output destination is a relative asset path resolved by the provider through
/// the approved asset store.
/// </summary>
public sealed record SpeechSynthesisRequest(
    string Text,
    SpeechVoiceProfile Voice,
    string RelativeOutputPath,
    string? Locale = null);
