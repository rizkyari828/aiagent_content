using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Narration;
using AIStudio.Domain.Assets;
using Xunit;

namespace AIStudio.Tests.Narration;

public sealed class NarrationApiContractTests
{
    [Fact]
    public void NarrationResponse_ExposesCanonicalMetadata()
    {
        var snapshot = new NarrationSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "narration.wav",
            4096,
            new string('c', 64),
            AssetOrigin.Local,
            null,
            "Voiceover artist",
            null,
            null,
            new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

        var response = NarrationResponse.From(snapshot);
        var json = JsonSerializer.SerializeToElement(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("narration.wav", json.GetProperty("path").GetString());
        Assert.Equal(4096, json.GetProperty("byteSize").GetInt64());
        Assert.Equal("local", json.GetProperty("origin").GetString());
        Assert.Equal("Voiceover artist", json.GetProperty("creator").GetString());
    }

    [Fact]
    public void RegisterNarrationRequest_DeserializesFromWebJson()
    {
        var json = """
            {
              "path": "narration.wav",
              "origin": "local"
            }
            """;

        var request = JsonSerializer.Deserialize<RegisterNarrationRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal("narration.wav", request.Path);
        Assert.Equal("local", request.Origin);
    }
}
