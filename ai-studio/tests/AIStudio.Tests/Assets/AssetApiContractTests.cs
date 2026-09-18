using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Assets;
using AIStudio.Domain.Assets;
using Xunit;

namespace AIStudio.Tests.Assets;

public sealed class AssetApiContractTests
{
    [Fact]
    public void SceneAssetResponse_ExposesCanonicalMetadata()
    {
        var snapshot = new SceneAssetSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            AssetType.Image,
            "scene-1.png",
            2048,
            new string('b', SceneAsset.ContentHashLength),
            AssetOrigin.External,
            "https://example.test/asset",
            "Photographer",
            "CC-BY-4.0",
            AssetTestData.Now.AddDays(-1),
            AssetTestData.Now);

        var response = SceneAssetResponse.From(snapshot);
        var json = JsonSerializer.SerializeToElement(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(1, json.GetProperty("sceneIndex").GetInt32());
        Assert.Equal("image", json.GetProperty("type").GetString());
        Assert.Equal("external", json.GetProperty("origin").GetString());
        Assert.Equal("scene-1.png", json.GetProperty("path").GetString());
        Assert.Equal(2048, json.GetProperty("byteSize").GetInt64());
        Assert.Equal("CC-BY-4.0", json.GetProperty("license").GetString());
    }

    [Fact]
    public void RegisterSceneAssetRequest_DeserializesFromWebJson()
    {
        var json = """
            {
              "sceneIndex": 0,
              "path": "scene-0.png",
              "type": "image",
              "origin": "local"
            }
            """;

        var request = JsonSerializer.Deserialize<RegisterSceneAssetRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal(0, request.SceneIndex);
        Assert.Equal("scene-0.png", request.Path);
        Assert.Equal("image", request.Type);
        Assert.Equal("local", request.Origin);
    }
}
