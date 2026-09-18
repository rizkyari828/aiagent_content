using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Domain.Jobs;

namespace AIStudio.Tests.Rendering;

internal static class FinalVideoQaTestData
{
    public static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static RenderVideoResult RenderResult(
        string outputPath,
        string contentHash,
        long byteSize = 2_048,
        double durationSeconds = 12.5,
        int width = 640,
        int height = 480,
        Guid? subtitleTrackId = null,
        string? subtitleContentHash = null) =>
        new(
            outputPath,
            contentHash,
            byteSize,
            durationSeconds,
            width,
            height,
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            new string('a', RenderVideoResult.ContentHashLength),
            subtitleTrackId,
            subtitleContentHash);

    public static JobSnapshot RenderJob(
        Guid contentProjectId,
        RenderVideoResult result,
        JobStatus status = JobStatus.Succeeded) =>
        new(
            Guid.NewGuid(),
            contentProjectId,
            JobType.RenderVideo,
            status,
            0,
            2,
            result.Serialize(),
            null,
            null,
            Now,
            Now,
            Now,
            Now);

    public static ClaimedJob ClaimedQaJob(Guid contentProjectId, Guid renderJobId) =>
        new(
            Guid.NewGuid(),
            contentProjectId,
            JobType.FinalVideoQa,
            "input-v1",
            JsonSerializer.Serialize(
                new FinalVideoQaJobPayload(contentProjectId, renderJobId),
                JsonOptions),
            0,
            2,
            false);
}

internal sealed class FakeMediaInspector(Func<string, MediaInspection> handler)
    : IMediaInspector
{
    public List<string> Paths { get; } = [];

    public Task<MediaInspection> InspectAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Paths.Add(absolutePath);
        return Task.FromResult(handler(absolutePath));
    }

    public static FakeMediaInspector Returning(MediaInspection inspection) =>
        new(_ => inspection);

    public static FakeMediaInspector Failing(string errorCode) =>
        new(_ => throw new ProcessExecutionException(errorCode, "synthetic probe failure"));
}
