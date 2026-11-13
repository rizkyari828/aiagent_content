using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Rendering;
using AIStudio.Application.Research;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.AI.Cli;

public sealed class CliTextGenerator(
    IProcessRunner processRunner,
    IOptions<CliAiOptions> options,
    TimeProvider timeProvider) : IAiTextGenerator
{
    private readonly SemaphoreSlim concurrencyLock = new(1, 1);
    private DateTimeOffset? cooldownUntil;

    public async Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var settings = options.Value;
        if (!settings.Enabled)
        {
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "Experimental CLI AI provider is disabled (CliAi:Enabled=false).");
        }

        if (string.IsNullOrWhiteSpace(settings.Executable))
        {
            throw new AiGenerationException(
                AiErrorCode.ProviderUnavailable,
                "CliAi:Executable is not configured.");
        }

        var now = timeProvider.GetUtcNow();
        if (cooldownUntil.HasValue && cooldownUntil.Value > now)
        {
            throw new AiGenerationException(
                AiErrorCode.RateLimited,
                $"CLI AI provider is in cooldown until {cooldownUntil.Value:O}.");
        }

        await concurrencyLock.WaitAsync(cancellationToken);
        string? tempDir = null;
        try
        {
            now = timeProvider.GetUtcNow();
            if (cooldownUntil.HasValue && cooldownUntil.Value > now)
            {
                throw new AiGenerationException(
                    AiErrorCode.RateLimited,
                    $"CLI AI provider is in cooldown until {cooldownUntil.Value:O}.");
            }

            tempDir = Directory.CreateTempSubdirectory("aistudio-cli-").FullName;

            var prompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
                ? request.Prompt
                : $"{request.SystemPrompt.Trim()}\n\n{request.Prompt}";

            var model = string.IsNullOrWhiteSpace(request.Model)
                ? settings.DefaultModel
                : request.Model.Trim();

            var args = new List<string>
            {
                "-p",
                prompt,
                "--output-format",
                "json"
            };

            if (!string.IsNullOrWhiteSpace(model))
            {
                args.Add("--model");
                args.Add(model);
            }

            ProcessResult result;
            try
            {
                result = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        settings.Executable.Trim(),
                        args,
                        TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 300)),
                        WorkingDirectory: tempDir,
                        CloseStandardInput: true),
                    cancellationToken);
            }
            catch (ProcessExecutionException ex) when (ex.ErrorCode == ProcessExecutionException.TimedOut)
            {
                ActivateCooldown(settings);
                throw new AiGenerationException(AiErrorCode.Timeout, "CLI AI provider timed out.", innerException: ex);
            }
            catch (ProcessExecutionException ex)
            {
                throw new AiGenerationException(AiErrorCode.ProviderUnavailable, ex.Message, innerException: ex);
            }

            var combinedError = $"{result.StandardError}\n{result.StandardOutput}";
            if (IsRateLimitSignal(combinedError))
            {
                ActivateCooldown(settings);
                throw new AiGenerationException(
                    AiErrorCode.RateLimited,
                    "CLI AI provider hit rate limit / quota (429 RESOURCE_EXHAUSTED).");
            }

            if (result.ExitCode != 0)
            {
                throw new AiGenerationException(
                    AiErrorCode.ProviderError,
                    $"CLI AI provider exited with code {result.ExitCode}.");
            }

            var extracted = ExtractJsonResponse(result.StandardOutput);
            return new AiTextResponse(
                extracted,
                string.IsNullOrWhiteSpace(model) ? "gemini-cli" : model,
                null,
                null,
                null);
        }
        finally
        {
            concurrencyLock.Release();
            if (tempDir is not null)
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best-effort temp dir cleanup
                }
            }
        }
    }

    private void ActivateCooldown(CliAiOptions settings)
    {
        var seconds = Math.Clamp(settings.CooldownSeconds, 30, 3_600);
        cooldownUntil = timeProvider.GetUtcNow().AddSeconds(seconds);
    }

    private static bool IsRateLimitSignal(string text) =>
        text.Contains("429", StringComparison.OrdinalIgnoreCase)
        || text.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase)
        || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

    private static string ExtractJsonResponse(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new AiGenerationException(
                AiErrorCode.MalformedResponse,
                "CLI AI provider returned empty output.");
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("error", out var errorEl)
                && errorEl.ValueKind != JsonValueKind.Null)
            {
                var errText = errorEl.ToString();
                if (IsRateLimitSignal(errText))
                {
                    throw new AiGenerationException(AiErrorCode.RateLimited, errText);
                }

                throw new AiGenerationException(AiErrorCode.ProviderError, $"CLI AI error: {errText}");
            }

            if (doc.RootElement.TryGetProperty("response", out var respEl)
                && respEl.ValueKind == JsonValueKind.String)
            {
                return ResearchPackageParser.StripMarkdownFence(respEl.GetString() ?? string.Empty);
            }
        }
        catch (JsonException ex)
        {
            throw new AiGenerationException(
                AiErrorCode.MalformedResponse,
                "CLI AI output was not valid --output-format json.",
                innerException: ex);
        }

        throw new AiGenerationException(
            AiErrorCode.MalformedResponse,
            "CLI AI JSON output did not contain a 'response' string.");
    }
}
