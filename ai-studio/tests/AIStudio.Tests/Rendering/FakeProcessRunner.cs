using System.Globalization;
using AIStudio.Application.Rendering;

namespace AIStudio.Tests.Rendering;

internal sealed class FakeProcessRunner(
    Func<ProcessRunRequest, ProcessResult>? handler = null) : IProcessRunner
{
    public List<ProcessRunRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    public Task<ProcessResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        if (handler is null)
        {
            throw new InvalidOperationException(
                "The fake process runner was called without a configured handler.");
        }

        return Task.FromResult(handler(request));
    }
}

internal static class FakeFfmpeg
{
    public static FakeProcessRunner WritingOutput(
        byte[] outputBytes,
        double durationSeconds = 12,
        bool outputHasAudio = true) =>
        new(request =>
        {
            if (request.FileName.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                var target = request.Arguments[^1];
                return new ProcessResult(
                    0,
                    ProbeJson(durationSeconds, File.Exists(target) && outputHasAudio),
                    string.Empty);
            }

            if (request.FileName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                var output = request.Arguments[^1];
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllBytes(output, outputBytes);
                return new ProcessResult(0, string.Empty, string.Empty);
            }

            throw new InvalidOperationException($"Unexpected tool '{request.FileName}'.");
        });

    public static FakeProcessRunner FailingRender(string standardError) =>
        new(request =>
        {
            if (request.FileName.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessResult(0, ProbeJson(12, hasVideo: false), string.Empty);
            }

            return new ProcessResult(1, string.Empty, standardError);
        });

    private static string ProbeJson(double duration, bool hasVideo)
    {
        var streams = hasVideo
            ? """[{"codec_type":"video"},{"codec_type":"audio"}]"""
            : """[{"codec_type":"audio"}]""";
        var seconds = duration.ToString(CultureInfo.InvariantCulture);
        return $$"""{"format":{"duration":"{{seconds}}"},"streams":{{streams}}}""";
    }
}
