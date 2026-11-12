using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering.Gemini;
using AIStudio.Tests.IdentityAssets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Deterministic fake-HTTP unit tests for <see cref="GeminiVideoGenerationProvider"/>.
/// Verifies Veo asynchronous REST predictLongRunning flow, polling, download, identity
/// asset validation, media inspection bounds, error handling, and security guarantees.
/// </summary>
public sealed class GeminiVideoGenerationProviderTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static readonly byte[] Mp4 =
    [
        0x00, 0x00, 0x00, 0x18,
        0x66, 0x74, 0x79, 0x70,
        0x69, 0x73, 0x6F, 0x6D,
        0x00, 0x00, 0x02, 0x00
    ];

    private const string DefaultBaseAddress = "https://generativelanguage.googleapis.com/";
    private const string TestApiKey = "secret-veo-key-xyz-987";

    private readonly string root =
        Path.Combine(Path.GetTempPath(), $"aistudio-gemini-video-{Guid.NewGuid():N}");

    public GeminiVideoGenerationProviderTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenDisabled()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: false,
            apiKey: TestApiKey);

        Assert.False(provider.IsEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsEnabled_ReturnsFalse_WhenApiKeyMissing(string apiKey)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: true,
            apiKey: apiKey);

        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_ReturnsTrue_WhenEnabledAndApiKeyConfigured()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException()),
            enabled: true,
            apiKey: TestApiKey);

        Assert.True(provider.IsEnabled);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsNotConfigured_WhenDisabled()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: false,
            apiKey: TestApiKey);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.NotConfigured, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsNotConfigured_WhenApiKeyMissing()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: string.Empty);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.NotConfigured, ex.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/absolute/path")]
    [InlineData("../parent/path")]
    [InlineData("sub/../../escape")]
    [InlineData("C:/rooted/path")]
    public async Task GenerateAsync_ThrowsNotConfigured_WhenOutputDirectoryIsUnsafe(string outputDirectory)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: TestApiKey,
            outputDirectory: outputDirectory);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.NotConfigured, ex.ErrorCode);
    }

    [Theory]
    [InlineData("", 5, 24, 1280, 720, 0, "video_request_invalid")]
    [InlineData("   ", 5, 24, 1280, 720, 0, "video_request_invalid")]
    [InlineData("prompt\0with_nul", 5, 24, 1280, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 0, 24, 1280, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 9, 24, 1280, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 0, 1280, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 31, 1280, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 14, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 1282, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 1279, 720, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 1280, 14, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 1280, 722, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 1280, 721, 0, "video_request_invalid")]
    [InlineData("prompt", 5, 24, 1280, 720, -1, "video_request_invalid")]
    public async Task GenerateAsync_RejectsInvalidRequests(
        string prompt,
        int durationSeconds,
        int framesPerSecond,
        int width,
        int height,
        long seed,
        string expectedCode)
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: TestApiKey);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(
                new VideoGenerationRequest(prompt, durationSeconds, framesPerSecond, width, height, seed),
                TestContext.Current.CancellationToken));

        Assert.Equal(expectedCode, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_TextOnly_SubmitsExpectedJsonAndPersistsResult()
    {
        string? submittedJson = null;
        string? submittedAuthHeader = null;
        string? pollAuthHeader = null;
        string? downloadAuthHeader = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var apiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.FirstOrDefault()
                : null;

            if (request.Method == HttpMethod.Post && path == "/v1beta/models/veo-3.1-generate-preview:predictLongRunning")
            {
                submittedAuthHeader = apiKey;
                submittedJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-12345"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/v1beta/models/veo-3.1-generate-preview/operations/op-12345")
            {
                pollAuthHeader = apiKey;
                return Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-12345",
                      "done": true,
                      "response": {
                        "generateVideoResponse": {
                          "generatedSamples": [
                            {
                              "video": {
                                "uri": "https://generativelanguage.googleapis.com/v1beta/files/video-xyz"
                              }
                            }
                          ]
                        }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/v1beta/files/video-xyz")
            {
                downloadAuthHeader = apiKey;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Mp4)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 5.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var result = await provider.GenerateAsync(
            Request(prompt: "a golden retriever running across an alpine meadow", durationSeconds: 5, seed: 777),
            TestContext.Current.CancellationToken);

        Assert.Equal(TestApiKey, submittedAuthHeader);
        Assert.Equal(TestApiKey, pollAuthHeader);
        Assert.Equal(TestApiKey, downloadAuthHeader);

        Assert.NotNull(submittedJson);
        var parsed = JsonNode.Parse(submittedJson) as JsonObject;
        Assert.NotNull(parsed);

        var instances = parsed["instances"] as JsonArray;
        Assert.NotNull(instances);
        Assert.Single(instances);
        var instance = instances[0] as JsonObject;
        Assert.NotNull(instance);
        Assert.Equal("a golden retriever running across an alpine meadow", instance["prompt"]?.GetValue<string>());
        Assert.Null(instance["image"]);

        var parameters = parsed["parameters"] as JsonObject;
        Assert.NotNull(parameters);
        Assert.Equal("16:9", parameters["aspectRatio"]?.GetValue<string>());
        Assert.Equal("720p", parameters["resolution"]?.GetValue<string>());
        Assert.Equal(5, parameters["durationSeconds"]?.GetValue<int>());
        Assert.Equal(1, parameters["sampleCount"]?.GetValue<int>());
        Assert.Equal(777L, parameters["seed"]?.GetValue<long>());

        Assert.StartsWith("generated-video/gemini-veo/scene-", result.RelativeOutputPath);
        Assert.EndsWith(".mp4", result.RelativeOutputPath);
        Assert.Equal(Mp4.Length, result.ByteSize);
        Assert.Equal(Hash(Mp4), result.ContentHash);
        Assert.Equal(5.0, result.DurationSeconds);
        Assert.Equal(1280, result.Width);
        Assert.Equal(720, result.Height);
        Assert.Equal(24, result.FramesPerSecond);

        Assert.True(File.Exists(Path.Combine(root, result.RelativeOutputPath)));
    }

    [Fact]
    public async Task GenerateAsync_AlternativeVideoUri_SupportsDirectVideosArray()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-alt"
                    }
                    """));
            }

            if (request.Method == HttpMethod.Get && path == "/v1beta/models/veo-3.1-generate-preview/operations/op-alt")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-alt",
                      "done": true,
                      "response": {
                        "videos": [
                          {
                            "uri": "https://generativelanguage.googleapis.com/v1beta/files/video-alt"
                          }
                        ]
                      }
                    }
                    """));
            }

            if (request.Method == HttpMethod.Get && path == "/v1beta/files/video-alt")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Mp4)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 4.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var result = await provider.GenerateAsync(
            Request(durationSeconds: 4),
            TestContext.Current.CancellationToken);

        Assert.Equal(4.0, result.DurationSeconds);
        Assert.StartsWith("generated-video/gemini-veo/scene-", result.RelativeOutputPath);
    }

    [Fact]
    public async Task GenerateAsync_WithApprovedPinnedFirstFrame_EncodesBase64AndSendsImage()
    {
        var fixture = ApprovedReference();
        string? submittedJson = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post)
            {
                submittedJson = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-frame"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/v1beta/models/veo-3.1-generate-preview/operations/op-frame")
            {
                return Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-frame",
                      "done": true,
                      "response": {
                        "generateVideoResponse": {
                          "generatedSamples": [
                            {
                              "video": {
                                "uri": "https://generativelanguage.googleapis.com/v1beta/files/video-frame"
                              }
                            }
                          ]
                        }
                      }
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/v1beta/files/video-frame")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Mp4)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var provider = CreateProvider(
            handler,
            enabled: true,
            apiKey: TestApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var result = await provider.GenerateAsync(
            Request(firstFrame: fixture.Reference),
            TestContext.Current.CancellationToken);

        Assert.NotNull(submittedJson);
        var parsed = JsonNode.Parse(submittedJson) as JsonObject;
        var instance = (parsed?["instances"] as JsonArray)?[0] as JsonObject;
        Assert.NotNull(instance);

        var imageObj = instance["image"] as JsonObject;
        Assert.NotNull(imageObj);
        Assert.Equal("image/png", imageObj["mimeType"]?.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Png), imageObj["bytesBase64Encoded"]?.GetValue<string>());

        Assert.Equal([(fixture.Reference.AssetId, fixture.Reference.Version)], fixture.Store.Reads);
        Assert.EndsWith(".mp4", result.RelativeOutputPath);
    }

    [Fact]
    public async Task GenerateAsync_WithUnapprovedFirstFrame_RejectsBeforeHttp()
    {
        var fixture = ApprovedReference(approve: false);
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: TestApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(
                Request(firstFrame: fixture.Reference),
                TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.RequestInvalid, ex.ErrorCode);
        Assert.Empty(fixture.Store.Reads);
    }

    [Fact]
    public async Task GenerateAsync_WithNonPngFirstFrame_RejectsBeforeHttp()
    {
        byte[] fakeJpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 1, 2, 3];
        var fixture = ApprovedReference(customBytes: fakeJpg);
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: TestApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(
                Request(firstFrame: fixture.Reference),
                TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.RequestInvalid, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_WithHashMismatchFirstFrame_RejectsBeforeHttp()
    {
        var fixture = ApprovedReference();
        // Modify bytes in store so hash does not match approved metadata
        fixture.Store.Set(fixture.Reference.AssetId, fixture.Reference.Version, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9]);

        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: TestApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(
                Request(firstFrame: fixture.Reference),
                TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.RequestInvalid, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_WithOversizedFirstFrame_RejectsBeforeHttp()
    {
        var largePng = new byte[8 * 1024 * 1024 + 1];
        Png.CopyTo(largePng, 0);

        var fixture = ApprovedReference(customBytes: largePng);
        var provider = CreateProvider(
            new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP must not be called")),
            enabled: true,
            apiKey: TestApiKey,
            registry: fixture.Registry,
            store: fixture.Store);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(
                Request(firstFrame: fixture.Reference),
                TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.RequestInvalid, ex.ErrorCode);
    }

    [Theory]
    [InlineData("http://evil.com/models/veo/operations/op-1")]
    [InlineData("/../etc/passwd")]
    [InlineData("..\\windows\\system32")]
    public async Task GenerateAsync_RejectsUnsafeOperationName_ThrowsGenerationFailed(string unsafeOpName)
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""
                    {
                      "name": "{{unsafeOpName}}"
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 5.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_OperationReportsError_ThrowsGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-err"
                    }
                    """));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, """
                {
                  "name": "models/veo-3.1-generate-preview/operations/op-err",
                  "done": true,
                  "error": {
                    "code": 400,
                    "message": "Veo safety violation: prompt refused"
                  }
                }
                """));
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 5.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, ex.ErrorCode);
        Assert.Contains("Veo safety violation", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_PollTimeout_ThrowsVideoTimeout()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-timeout"
                    }
                    """));
            }

            // Polling reports done: false indefinitely
            return Task.FromResult(Json(HttpStatusCode.OK, """
                {
                  "name": "models/veo-3.1-generate-preview/operations/op-timeout",
                  "done": false
                }
                """));
        });

        var provider = CreateProvider(
            handler,
            enabled: true,
            apiKey: TestApiKey,
            timeoutSeconds: 1,
            pollIntervalMilliseconds: 50);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.Timeout, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_DownloadUntrustedHost_ThrowsGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-evil"
                    }
                    """));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, """
                {
                  "name": "models/veo-3.1-generate-preview/operations/op-evil",
                  "done": true,
                  "response": {
                    "generateVideoResponse": {
                      "generatedSamples": [
                        {
                          "video": {
                            "uri": "https://untrusted-external-domain.com/video.mp4"
                          }
                        }
                      ]
                    }
                  }
                }
                """));
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 5.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_DownloadUnsafeRelativePath_ThrowsGenerationFailed()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-rel"
                    }
                    """));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, """
                {
                  "name": "models/veo-3.1-generate-preview/operations/op-rel",
                  "done": true,
                  "response": {
                    "generateVideoResponse": {
                      "generatedSamples": [
                        {
                          "video": {
                            "uri": "../../../etc/passwd"
                          }
                        }
                      ]
                    }
                  }
                }
                """));
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 5.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_EmptyArtifact_ThrowsArtifactInvalidWithoutProbing()
    {
        var inspector = new StubMediaInspector();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-empty"
                    }
                    """));
            }

            if (request.RequestUri!.AbsolutePath.Contains("operations"))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-empty",
                      "done": true,
                      "response": {
                        "generateVideoResponse": {
                          "generatedSamples": [
                            {
                              "video": {
                                "uri": "https://generativelanguage.googleapis.com/v1beta/files/empty"
                              }
                            }
                          ]
                        }
                      }
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([])
            });
        });

        var provider = CreateProvider(
            handler,
            enabled: true,
            apiKey: TestApiKey,
            mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.ArtifactInvalid, ex.ErrorCode);
        Assert.Equal(0, inspector.Calls);
    }

    [Fact]
    public async Task GenerateAsync_MediaInspectorProbeFailure_ThrowsArtifactInvalid()
    {
        var inspector = new StubMediaInspector
        {
            Failure = new ProcessExecutionException(
                ProcessExecutionException.MediaProbeFailed,
                "ffprobe failed to parse video container.")
        };

        var provider = CreateProvider(
            SuccessHandler(),
            enabled: true,
            apiKey: TestApiKey,
            mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.ArtifactInvalid, ex.ErrorCode);
        Assert.Equal(1, inspector.Calls);
    }

    [Theory]
    [InlineData(false, 3.0, 1280, 720)]
    [InlineData(true, 0.0, 1280, 720)]
    [InlineData(true, 3.0, 640, 720)]
    [InlineData(true, 3.0, 1280, 360)]
    [InlineData(true, 5.0, 1280, 720)] // requested 3s, tolerance max(0.25, 0.15) = 0.25s, so 5.0s fails
    public async Task GenerateAsync_InspectionMismatch_ThrowsArtifactInvalid(
        bool hasVideo,
        double durationSeconds,
        int width,
        int height)
    {
        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(
                durationSeconds,
                hasVideo,
                HasAudio: false,
                HasSubtitle: false,
                width,
                height)
        };

        var provider = CreateProvider(
            SuccessHandler(),
            enabled: true,
            apiKey: TestApiKey,
            mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(durationSeconds: 3), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.ArtifactInvalid, ex.ErrorCode);
        Assert.Equal(1, inspector.Calls);
    }

    [Fact]
    public async Task GenerateAsync_InspectionDurationWithinTolerance_Succeeds()
    {
        // 3s request, max(0.25, 0.15) = 0.25s tolerance => 3.2s is accepted
        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(
                DurationSeconds: 3.2,
                HasVideo: true,
                HasAudio: false,
                HasSubtitle: false,
                Width: 1280,
                Height: 720)
        };

        var provider = CreateProvider(
            SuccessHandler(),
            enabled: true,
            apiKey: TestApiKey,
            mediaInspector: inspector);

        var result = await provider.GenerateAsync(
            Request(durationSeconds: 3),
            TestContext.Current.CancellationToken);

        Assert.Equal(3.2, result.DurationSeconds);
        Assert.Equal(1, inspector.Calls);
    }

    [Fact]
    public async Task GenerateAsync_NeverLeaksApiKeyOrHostInExceptionMessages()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-leak"
                    }
                    """));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, $$"""
                {
                  "name": "models/veo-3.1-generate-preview/operations/op-leak",
                  "done": true,
                  "error": {
                    "code": 403,
                    "message": "Permission denied for key {{TestApiKey}} at generativelanguage.googleapis.com"
                  }
                }
                """));
        });

        var inspector = new StubMediaInspector
        {
            Result = new MediaInspection(DurationSeconds: 5.0, HasVideo: true, HasAudio: false, HasSubtitle: false, Width: 1280, Height: 720)
        };
        var provider = CreateProvider(handler, enabled: true, apiKey: TestApiKey, mediaInspector: inspector);

        var ex = await Assert.ThrowsAsync<VideoGenerationException>(() =>
            provider.GenerateAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal(VideoGenerationException.GenerationFailed, ex.ErrorCode);
        Assert.DoesNotContain(TestApiKey, ex.Message);
        Assert.DoesNotContain("generativelanguage.googleapis.com", ex.Message);
    }

    [Fact]
    public void Constructor_DoesNotTakeGpuResourceGate()
    {
        var constructors = typeof(GeminiVideoGenerationProvider).GetConstructors();
        Assert.Single(constructors);

        var parameters = constructors[0].GetParameters();
        Assert.DoesNotContain(
            parameters,
            p => p.ParameterType.Name.Contains("Gpu", StringComparison.OrdinalIgnoreCase));
    }

    private static VideoGenerationRequest Request(
        string prompt = "a peaceful stream flowing through autumn woods",
        int durationSeconds = 3,
        int framesPerSecond = 24,
        int width = 1280,
        int height = 720,
        long seed = 1,
        PinnedIdentityAsset? firstFrame = null) =>
        new(prompt, durationSeconds, framesPerSecond, width, height, seed, firstFrame);

    private GeminiVideoGenerationProvider CreateProvider(
        HttpMessageHandler handler,
        bool enabled,
        string apiKey = TestApiKey,
        string outputDirectory = "generated-video/gemini-veo",
        int timeoutSeconds = 600,
        int pollIntervalMilliseconds = 10,
        IIdentityAssetRegistry? registry = null,
        IIdentityAssetStore? store = null,
        IMediaInspector? mediaInspector = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(DefaultBaseAddress) },
            Options.Create(new GeminiVideoOptions
            {
                Enabled = enabled,
                ApiKey = apiKey,
                BaseUrl = DefaultBaseAddress,
                Model = "veo-3.1-generate-preview",
                OutputDirectory = outputDirectory,
                AspectRatio = "16:9",
                Resolution = "720p",
                TimeoutSeconds = timeoutSeconds,
                PollIntervalMilliseconds = pollIntervalMilliseconds,
                MaxSeconds = 8,
                Width = 1280,
                Height = 720,
                Fps = 30
            }),
            registry ?? new IdentityAssetRegistry(),
            store ?? new FakeIdentityAssetStore(),
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })),
            mediaInspector ?? new StubMediaInspector());

    private static StubHttpMessageHandler SuccessHandler() =>
        new((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Post)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-ok"
                    }
                    """));
            }

            if (path == "/v1beta/models/veo-3.1-generate-preview/operations/op-ok")
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """
                    {
                      "name": "models/veo-3.1-generate-preview/operations/op-ok",
                      "done": true,
                      "response": {
                        "generateVideoResponse": {
                          "generatedSamples": [
                            {
                              "video": {
                                "uri": "https://generativelanguage.googleapis.com/v1beta/files/video-ok"
                              }
                            }
                          ]
                        }
                      }
                    }
                    """));
            }

            if (path == "/v1beta/files/video-ok")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Mp4)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

    private static ReferenceFixture ApprovedReference(bool approve = true, byte[]? customBytes = null)
    {
        var rawBytes = customBytes ?? Png;
        var asset = IdentityAssetTestSupport.Asset(
            id: "character-firstframe",
            version: 1,
            kind: "reference-image",
            mediaType: "image/png") with
        {
            ByteSize = rawBytes.Length,
            ContentHash = Hash(rawBytes)
        };

        var registry = new IdentityAssetRegistry([asset]);
        if (approve)
        {
            registry.Approve(asset.Id, asset.Version);
        }

        var store = new FakeIdentityAssetStore();
        store.Set(asset.Id, asset.Version, rawBytes);

        return new ReferenceFixture(
            registry,
            store,
            new PinnedIdentityAsset(asset.Id, asset.Version));
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed record ReferenceFixture(
        IdentityAssetRegistry Registry,
        FakeIdentityAssetStore Store,
        PinnedIdentityAsset Reference);

    private sealed class FakeIdentityAssetStore : IIdentityAssetStore
    {
        private readonly Dictionary<(AssetReferenceId, IdentityAssetVersion), byte[]> bytes = [];

        public List<(AssetReferenceId, IdentityAssetVersion)> Reads { get; } = [];

        public void Set(AssetReferenceId id, IdentityAssetVersion version, byte[] content) =>
            bytes[(id, version)] = content.ToArray();

        public Task<IdentityAssetBlob> WriteAsync(
            AssetReferenceId id,
            IdentityAssetVersion version,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken)
        {
            Set(id, version, content.ToArray());
            return Task.FromResult(new IdentityAssetBlob(content.Length, Hash(content.ToArray())));
        }

        public Task<ReadOnlyMemory<byte>> ReadBytesAsync(
            AssetReferenceId id,
            IdentityAssetVersion version,
            CancellationToken cancellationToken)
        {
            Reads.Add((id, version));
            if (!bytes.TryGetValue((id, version), out var content))
            {
                throw new FileNotFoundException();
            }

            return Task.FromResult<ReadOnlyMemory<byte>>(content.ToArray());
        }

        public bool Exists(AssetReferenceId id, IdentityAssetVersion version) =>
            bytes.ContainsKey((id, version));
    }

    private sealed class StubMediaInspector : IMediaInspector
    {
        public MediaInspection Result { get; set; } = new(
            DurationSeconds: 3,
            HasVideo: true,
            HasAudio: false,
            HasSubtitle: false,
            Width: 1280,
            Height: 720);

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public Task<MediaInspection> InspectAsync(
            string absolutePath,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<MediaInspection>(Failure);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
