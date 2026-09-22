using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class ImageGenerationRequestTests
{
    [Fact]
    public void Constructor_AllowsNoIdentityReference()
    {
        var request = new ImageGenerationRequest("prompt", 42);

        Assert.Equal("prompt", request.Prompt);
        Assert.Equal(42, request.Seed);
        Assert.Empty(request.IdentityReferences);
    }

    [Fact]
    public void Constructor_AllowsOneConcretePinnedIdentityReference()
    {
        var reference = new PinnedIdentityAsset(
            new AssetReferenceId("student-reference"),
            new IdentityAssetVersion(2));

        var request = new ImageGenerationRequest("prompt", 42, [reference]);

        Assert.Same(reference, request.IdentityReferences.Single());
        Assert.Equal(2, request.IdentityReferences.Single().Version.Value);
    }

    [Fact]
    public void Constructor_RejectsMoreThanOneIdentityReference()
    {
        var first = new PinnedIdentityAsset(
            new AssetReferenceId("student-reference"),
            new IdentityAssetVersion(1));
        var second = new PinnedIdentityAsset(
            new AssetReferenceId("teacher-reference"),
            new IdentityAssetVersion(1));

        var exception = Assert.Throws<NotSupportedException>(() =>
            new ImageGenerationRequest("prompt", 42, [first, second]));

        Assert.Contains("at most one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedIdentityAsset_RejectsDefaultFloatingVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PinnedIdentityAsset(
                new AssetReferenceId("student-reference"),
                default));
    }
}
