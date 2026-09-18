namespace AIStudio.Application.Rendering;

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
