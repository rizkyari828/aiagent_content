using AIStudio.Application.Capabilities;
using Xunit;

namespace AIStudio.Tests.Capabilities;

public sealed class CapabilityIdTests
{
    [Theory]
    [InlineData("speech.narration")]
    [InlineData("visual.ui_motion")]
    [InlineData("visual.image_to_video")]
    [InlineData("audio.sound_effect")]
    [InlineData("subtitle.burned")]
    public void AcceptsStableDottedIdentifiers(string value)
    {
        var id = new CapabilityId(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("visual")]
    [InlineData("Visual.Burned")]
    [InlineData("visual.")]
    [InlineData(".burned")]
    [InlineData("visual..burned")]
    [InlineData("visual.burned-subtitle")]
    [InlineData("1visual.burned")]
    public void RejectsMalformedIdentifiers(string value)
    {
        Assert.False(CapabilityId.IsValid(value));
        Assert.Throws<ArgumentException>(() => new CapabilityId(value));
    }

    [Fact]
    public void TryParseReturnsFalseForMalformedValue()
    {
        Assert.False(CapabilityId.TryParse("Visual.Burned", out var parsed));
        Assert.Equal(default, parsed);
    }

    [Fact]
    public void KnownIdentifiersAreStable()
    {
        Assert.Equal("speech.narration", CapabilityIds.SpeechNarration.Value);
        Assert.Equal("music.instrumental", CapabilityIds.MusicInstrumental.Value);
        Assert.Equal("visual.still", CapabilityIds.VisualStill.Value);
        Assert.Equal("visual.diagram", CapabilityIds.VisualDiagram.Value);
        Assert.Equal("visual.ui_motion", CapabilityIds.VisualUiMotion.Value);
        Assert.Equal("visual.ai_image", CapabilityIds.VisualAiImage.Value);
        Assert.Equal("visual.three_d", CapabilityIds.VisualThreeD.Value);
        Assert.Equal("media.compose", CapabilityIds.MediaCompose.Value);
        Assert.Equal("subtitle.burned", CapabilityIds.SubtitleBurned.Value);
    }

    [Fact]
    public void EqualValuesShareEqualityAndHashCode()
    {
        var first = CapabilityId.Parse("visual.diagram");
        var second = CapabilityIds.VisualDiagram;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
