using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Subtitles;
using AIStudio.Domain.Assets;
using Xunit;

namespace AIStudio.Tests.Subtitles;

public sealed class SubtitleApiContractTests
{
    [Fact]
    public void SubtitleResponse_ExposesCanonicalMetadata()
    {
        var snapshot = new SubtitleSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subtitle.srt",
            512,
            new string('c', 64),
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero));

        var response = SubtitleResponse.From(snapshot);
        var json = JsonSerializer.SerializeToElement(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("subtitle.srt", json.GetProperty("path").GetString());
        Assert.Equal(512, json.GetProperty("byteSize").GetInt64());
        Assert.Equal("local", json.GetProperty("origin").GetString());
    }

    [Fact]
    public void RegisterSubtitleRequest_DeserializesFromWebJson()
    {
        var json = """
            {
              "path": "subtitle.srt",
              "origin": "local"
            }
            """;

        var request = JsonSerializer.Deserialize<RegisterSubtitleRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal("subtitle.srt", request.Path);
        Assert.Equal("local", request.Origin);
    }
}
