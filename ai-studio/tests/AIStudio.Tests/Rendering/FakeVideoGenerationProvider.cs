using AIStudio.Application.Rendering.Visuals;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Deterministic I2V provider double for future routing/wiring tests. It records
/// every request and returns a canned result; it never touches HTTP, the filesystem,
/// a GPU, or a model.
/// </summary>
internal sealed class FakeVideoGenerationProvider : IVideoGenerationProvider
{
    public bool IsEnabled { get; set; } = true;

    public List<VideoGenerationRequest> Requests { get; } = [];

    public VideoGenerationResult Result { get; set; } = new(
        "renders/i2v/fake.mp4",
        new string('a', 64),
        1024,
        3,
        1280,
        720,
        24);

    public Exception? Failure { get; set; }

    public Task<VideoGenerationResult> GenerateAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException<VideoGenerationResult>(Failure);
        }

        Requests.Add(request);
        return Task.FromResult(Result);
    }
}
