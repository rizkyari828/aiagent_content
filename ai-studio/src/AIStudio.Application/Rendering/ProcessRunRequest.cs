namespace AIStudio.Application.Rendering;

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string? WorkingDirectory = null,
    bool CloseStandardInput = false);
