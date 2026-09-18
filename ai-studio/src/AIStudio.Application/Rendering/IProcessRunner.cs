namespace AIStudio.Application.Rendering;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken);
}
