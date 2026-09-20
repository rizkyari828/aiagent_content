namespace AIStudio.Application.Rendering.AudioGeneration;

public sealed record SpeechSynthesisResult(
    string RelativePath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int SampleRate,
    SpeechVoiceProfile Voice);
