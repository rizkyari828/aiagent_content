using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.Rendering;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ReportsStartFailureForMissingExecutable()
    {
        var runner = new SystemProcessRunner();

        var exception = await Assert.ThrowsAsync<ProcessExecutionException>(
            () => runner.RunAsync(
                new ProcessRunRequest(
                    "/nonexistent/aistudio-tool",
                    [],
                    TimeSpan.FromSeconds(5)),
                TestContext.Current.CancellationToken));

        Assert.Equal(ProcessExecutionException.StartFailed, exception.ErrorCode);
    }
}
