using AIStudio.Application.AI;
using AIStudio.Application.Capabilities;
using AIStudio.Application.Rendering;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.AI.Cli;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.AI;

public sealed class CliTextGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WhenDisabled_ThrowsProviderUnavailableWithoutRunningProcess()
    {
        var runner = new RecordingProcessRunner(_ => new ProcessResult(0, "{}", string.Empty));
        var gen = new CliTextGenerator(
            runner,
            Options.Create(new CliAiOptions { Enabled = false }),
            TimeProvider.System);

        var ex = await Assert.ThrowsAsync<AiGenerationException>(() =>
            gen.GenerateAsync(new AiTextRequest("hello"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.ProviderUnavailable, ex.ErrorCode);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task GenerateAsync_UsesIsolatedEmptyWorkingDir_ClosedStdin_AndStripsJsonFence()
    {
        var runner = new RecordingProcessRunner(_ => new ProcessResult(
            0,
            """{"response":"```json\n{\"ok\":true}\n```","stats":{}}""",
            string.Empty));

        var gen = new CliTextGenerator(
            runner,
            Options.Create(new CliAiOptions { Enabled = true, Executable = "gemini" }),
            TimeProvider.System);

        var res = await gen.GenerateAsync(
            new AiTextRequest("Make idea", "System", Model: "gemini-2.5-pro"),
            TestContext.Current.CancellationToken);

        Assert.Equal("""{"ok":true}""", res.Text);
        Assert.Single(runner.Requests);
        var req = runner.Requests[0];
        Assert.Equal("gemini", req.FileName);
        Assert.True(req.CloseStandardInput);
        Assert.False(string.IsNullOrWhiteSpace(req.WorkingDirectory));
        Assert.DoesNotContain("--dangerously-skip-permissions", req.Arguments);
        Assert.Contains("--output-format", req.Arguments);
        Assert.Contains("json", req.Arguments);
    }

    [Fact]
    public async Task GenerateAsync_On429_ActivatesCooldownAndThrowsRateLimited()
    {
        var runner = new RecordingProcessRunner(_ => new ProcessResult(
            1,
            string.Empty,
            "Error 429 RESOURCE_EXHAUSTED"));

        var gen = new CliTextGenerator(
            runner,
            Options.Create(new CliAiOptions { Enabled = true, Executable = "gemini", CooldownSeconds = 300 }),
            TimeProvider.System);

        var first = await Assert.ThrowsAsync<AiGenerationException>(() =>
            gen.GenerateAsync(new AiTextRequest("p1"), TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<AiGenerationException>(() =>
            gen.GenerateAsync(new AiTextRequest("p2"), TestContext.Current.CancellationToken));

        Assert.Equal(AiErrorCode.RateLimited, first.ErrorCode);
        Assert.Equal(AiErrorCode.RateLimited, second.ErrorCode);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public void ProductionCapabilityCatalog_RegistersGeminiCliAtLowestPriorityDisabledByDefault()
    {
        var providers = ProductionCapabilityCatalog.CreateProviders(
            speechEnabled: false,
            musicEnabled: false,
            manimEnabled: false,
            imageEnabled: false,
            threeDEnabled: false,
            videoEnabled: false);

        var cli = Assert.Single(providers, p => p.ProviderId == "gemini-cli");
        Assert.Equal(CapabilityIds.TextGeneration, cli.CapabilityId);
        Assert.Equal(20, cli.Priority);
        Assert.False(cli.Enabled);
        Assert.Equal("local_cli_experimental", cli.RuntimeCategory);
    }

    private sealed class RecordingProcessRunner(Func<ProcessRunRequest, ProcessResult> handler) : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(handler(request));
        }
    }
}
