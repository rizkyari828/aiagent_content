using System.Text.Json;
using AIStudio.Application.Rendering;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Shared fakes for the audio generation providers. They emulate the python
/// launcher contract: read the constrained request JSON, optionally write the
/// requested output WAV, and write a small success/failure result JSON.
/// </summary>
internal static class AudioGenerationTestSupport
{
    public static string? ArgumentAfter(
        IReadOnlyList<string> arguments,
        string flag)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index] == flag)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    public static FakeProcessRunner Runner(
        bool writeOutput = true,
        bool success = true,
        int exitCode = 0,
        byte[]? audioBytes = null,
        bool writeResult = true,
        Action<JsonElement>? onRequest = null) =>
        new(request =>
        {
            var requestPath = ArgumentAfter(request.Arguments, "--request");
            var resultPath = ArgumentAfter(request.Arguments, "--result");
            if (requestPath is null || resultPath is null)
            {
                throw new InvalidOperationException("Request/result arguments were not supplied.");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(requestPath));
            onRequest?.Invoke(document.RootElement);

            if (exitCode == 0 && success && writeOutput)
            {
                var outputPath = document.RootElement.GetProperty("outputPath").GetString()!;
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllBytes(outputPath, audioBytes ?? [1, 2, 3, 4]);
            }

            if (writeResult)
            {
                File.WriteAllText(
                    resultPath,
                    success ? """{"success":true}""" : """{"success":false,"error":"boom"}""");
            }

            return new ProcessResult(exitCode, string.Empty, string.Empty);
        });

    public static MediaInspection Audio(
        double durationSeconds = 1.5,
        int sampleRate = 48000,
        int channels = 1) =>
        new(durationSeconds, false, true, false, 0, 0, sampleRate, channels);
}
