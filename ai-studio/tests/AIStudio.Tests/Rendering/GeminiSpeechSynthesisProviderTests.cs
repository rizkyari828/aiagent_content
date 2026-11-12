using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Assets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering.AudioGeneration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GeminiSpeechSynthesisProviderTests : IDisposable
{
    private readonly string root;
    private readonly string assetRoot;

    public GeminiSpeechSynthesisProviderTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-gemini-speech-tests",
            Guid.NewGuid().ToString("N"));
        assetRoot = Path.Combine(root, "assets");
        Directory.CreateDirectory(assetRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, "secret-key", true)]
    [InlineData(false, "secret-key", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    public void IsEnabled_ReturnsTrueOnlyWhenEnabledAndApiKeyConfigured(
        bool enabled,
        string apiKey,
        bool expected)
    {
        var provider = CreateProvider(options: BuildOptions(enabled: enabled, apiKey: apiKey));
        Assert.Equal(expected, provider.IsEnabled);
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsWhenDisabled()
    {
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(processRunner: runner, options: BuildOptions(enabled: false));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_disabled", exception.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsWhenApiKeyMissing()
    {
        var runner = new FakeProcessRunner();
        var provider = CreateProvider(processRunner: runner, options: BuildOptions(enabled: true, apiKey: "   "));

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_disabled", exception.ErrorCode);
        Assert.Equal(0, runner.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SynthesizeAsync_RejectsEmptyText(string invalidText)
    {
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest(invalidText, SpeechVoiceProfile.Formal, "audio/test.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsTextTooLong()
    {
        var provider = CreateProvider();
        var tooLong = new string('a', 2001);

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest(tooLong, SpeechVoiceProfile.Formal, "audio/test.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsInvalidVoice()
    {
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", (SpeechVoiceProfile)99, "audio/test.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SynthesizeAsync_RejectsEmptyOutputPath(string invalidPath)
    {
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, invalidPath),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsOutputPathEscapingAssetRoot()
    {
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, "../escape.wav"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
        Assert.False(File.Exists(Path.Combine(root, "escape.wav")));
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsUnsupportedLocale()
    {
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(
                new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, "audio/test.wav", "en-US"),
                TestContext.Current.CancellationToken));

        Assert.Equal("speech_invalid_input", exception.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("id")]
    [InlineData("id-ID")]
    [InlineData("id_ID")]
    public async Task SynthesizeAsync_AcceptsValidIndonesianLocales(string? locale)
    {
        var provider = CreateProvider();

        var result = await provider.SynthesizeAsync(
            new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, "audio/narration/loc.wav", locale),
            TestContext.Current.CancellationToken);

        Assert.Equal("audio/narration/loc.wav", result.RelativePath);
    }

    [Theory]
    [InlineData(SpeechVoiceProfile.Formal, "Kore", "Ucapkan dengan suara profesional, tenang, dan jelas dalam Bahasa Indonesia: ")]
    [InlineData(SpeechVoiceProfile.Playful, "Puck", "Ucapkan dengan suara ramah, ceria, dan ekspresif dalam Bahasa Indonesia: ")]
    [InlineData(SpeechVoiceProfile.Energetic, "Fenrir", "Ucapkan dengan suara bersemangat dan dinamis dalam Bahasa Indonesia: ")]
    [InlineData(SpeechVoiceProfile.Documentary, "Charon", "Ucapkan dengan suara dokumenter yang hangat dan sinematik dalam Bahasa Indonesia: ")]
    public async Task SynthesizeAsync_MapsAllVoiceProfiles_SendsApiKeyHeader_NormalizesViaFfmpeg_AndPersists(
        SpeechVoiceProfile profile,
        string expectedVoice,
        string expectedPrefix)
    {
        var rawAudio = new byte[] { 1, 2, 3, 4, 5 };
        var normalizedWav = new byte[] { 10, 20, 30, 40 };
        HttpRequestMessage? capturedRequest = null;
        string? requestBody = null;

        var httpHandler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            if (req.Content is not null)
            {
                requestBody = await req.Content.ReadAsStringAsync(ct);
            }
            return SuccessResponse(rawAudio, "audio/pcm;rate=24000");
        });

        var runner = CreateDefaultProcessRunner(normalizedWav);
        var inspector = FakeMediaInspector.Returning(AudioGenerationTestSupport.Audio(sampleRate: 48000, channels: 1));
        var provider = CreateProvider(httpHandler, runner, inspector: inspector);

        var request = new SpeechSynthesisRequest("Halo Dunia", profile, "audio/narration/test.wav");
        var result = await provider.SynthesizeAsync(request, TestContext.Current.CancellationToken);

        // Verify HTTP request
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Contains("v1beta/models/gemini-2.5-flash-preview-tts:generateContent", capturedRequest.RequestUri!.ToString());
        Assert.True(capturedRequest.Headers.TryGetValues("x-goog-api-key", out var apiKeys));
        Assert.Equal("test-gemini-key", apiKeys.Single());

        // Verify payload JSON
        Assert.NotNull(requestBody);
        using var doc = JsonDocument.Parse(requestBody);
        var rootElement = doc.RootElement;
        var promptText = rootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal(expectedPrefix + "Halo Dunia", promptText);

        var voiceName = rootElement.GetProperty("generationConfig").GetProperty("speechConfig").GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString();
        Assert.Equal(expectedVoice, voiceName);

        var modality = rootElement.GetProperty("generationConfig").GetProperty("responseModalities")[0].GetString();
        Assert.Equal("AUDIO", modality);

        // Verify ProcessRunner (FFmpeg)
        var ffmpegReq = Assert.Single(runner.Requests);
        Assert.Equal("ffmpeg", ffmpegReq.FileName);
        Assert.Contains("-y", ffmpegReq.Arguments);
        Assert.Contains("-f", ffmpegReq.Arguments);
        Assert.Contains("s16le", ffmpegReq.Arguments);
        Assert.Contains("-ar", ffmpegReq.Arguments);
        Assert.Contains("24000", ffmpegReq.Arguments);
        Assert.Contains("-ar", ffmpegReq.Arguments);
        Assert.Contains("48000", ffmpegReq.Arguments);
        Assert.Contains("-ac", ffmpegReq.Arguments);
        Assert.Contains("1", ffmpegReq.Arguments);
        Assert.Contains("-c:a", ffmpegReq.Arguments);
        Assert.Contains("pcm_s16le", ffmpegReq.Arguments);

        // Verify stored asset
        Assert.Equal("audio/narration/test.wav", result.RelativePath);
        Assert.Equal(RenderVideoTestData.Hash(normalizedWav), result.ContentHash);
        Assert.Equal(normalizedWav.Length, result.ByteSize);
        Assert.Equal(1.5, result.DurationSeconds);
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(profile, result.Voice);
        Assert.True(File.Exists(Path.Combine(assetRoot, "audio", "narration", "test.wav")));
    }

    [Fact]
    public async Task SynthesizeAsync_HandlesNonPcmMimeType_WithoutRawPcmFlags()
    {
        var rawAudio = new byte[] { 1, 2, 3 };
        var normalizedWav = new byte[] { 10, 20 };
        var httpHandler = new StubHttpMessageHandler(_ => SuccessResponse(rawAudio, "audio/wav"));
        var runner = CreateDefaultProcessRunner(normalizedWav);
        var provider = CreateProvider(httpHandler, runner);

        var request = new SpeechSynthesisRequest("Halo", SpeechVoiceProfile.Formal, "audio/narration/wav.wav");
        var result = await provider.SynthesizeAsync(request, TestContext.Current.CancellationToken);

        var ffmpegReq = Assert.Single(runner.Requests);
        Assert.DoesNotContain("s16le", ffmpegReq.Arguments);
        Assert.Contains("-i", ffmpegReq.Arguments);
        Assert.Contains("pcm_s16le", ffmpegReq.Arguments);
        Assert.Equal("audio/narration/wav.wav", result.RelativePath);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsHttpTimeoutToSpeechSynthesisTimeout()
    {
        var httpHandler = new StubHttpMessageHandler((_, _) =>
            throw new TaskCanceledException("HTTP request timed out"));
        var provider = CreateProvider(httpHandler);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_timeout", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsFfmpegTimeoutToSpeechSynthesisTimeout()
    {
        var runner = new FakeProcessRunner(_ =>
            throw new ProcessExecutionException(ProcessExecutionException.TimedOut, "FFmpeg timed out"));
        var provider = CreateProvider(processRunner: runner);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_timeout", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsMediaInspectionTimeoutToSpeechSynthesisTimeout()
    {
        var inspector = FakeMediaInspector.Failing(ProcessExecutionException.TimedOut);
        var provider = CreateProvider(inspector: inspector);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_timeout", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsHttpErrorToSpeechSynthesisFailed_WithoutLeakingApiKey()
    {
        const string apiKey = "super-secret-gemini-key";
        var httpHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = CreateProvider(httpHandler, options: BuildOptions(apiKey: apiKey));

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_failed", ex.ErrorCode);
        Assert.DoesNotContain(apiKey, ex.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsInvalidResponseJsonToSpeechSynthesisFailed()
    {
        var httpHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ broken json ...", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(httpHandler);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsMissingAudioDataToSpeechSynthesisFailed()
    {
        var httpHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"candidates":[]}""", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(httpHandler);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsInvalidBase64ToSpeechSynthesisFailed()
    {
        var json = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inlineData": {
                      "mimeType": "audio/pcm",
                      "data": "not-valid-base-64!!!"
                    }
                  }
                ]
              }
            }
          ]
        }
        """;
        var httpHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(httpHandler);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsEmptyAudioDataToSpeechSynthesisFailed()
    {
        var json = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  {
                    "inlineData": {
                      "mimeType": "audio/pcm",
                      "data": ""
                    }
                  }
                ]
              }
            }
          ]
        }
        """;
        var httpHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(httpHandler);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_failed", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsFfmpegFailureToSpeechSynthesisFailed_WithoutLeakingTempPaths()
    {
        var runner = new FakeProcessRunner(_ => new ProcessResult(1, string.Empty, "error output"));
        var provider = CreateProvider(processRunner: runner);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_synthesis_failed", ex.ErrorCode);
        Assert.DoesNotContain("aistudio-gemini-speech-", ex.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsMissingNormalizedOutputToSpeechOutputInvalid()
    {
        // Runner returns 0 exit code but does not write normalized WAV file
        var runner = new FakeProcessRunner(_ => new ProcessResult(0, string.Empty, string.Empty));
        var provider = CreateProvider(processRunner: runner);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsInvalidProbeWithoutAudioToSpeechOutputInvalid()
    {
        var inspector = FakeMediaInspector.Returning(new MediaInspection(1.5, false, false, false, 0, 0, 48000, 1));
        var provider = CreateProvider(inspector: inspector);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsInvalidProbeWithWrongSampleRateToSpeechOutputInvalid()
    {
        var inspector = FakeMediaInspector.Returning(new MediaInspection(1.5, false, true, false, 0, 0, 44100, 1));
        var provider = CreateProvider(inspector: inspector);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_MapsInvalidProbeWithWrongChannelsToSpeechOutputInvalid()
    {
        var inspector = FakeMediaInspector.Returning(new MediaInspection(1.5, false, true, false, 0, 0, 48000, 2));
        var provider = CreateProvider(inspector: inspector);

        var ex = await Assert.ThrowsAsync<SpeechSynthesisException>(
            () => provider.SynthesizeAsync(Request(), TestContext.Current.CancellationToken));

        Assert.Equal("speech_output_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task SynthesizeAsync_PropagatesCancellation()
    {
        var provider = CreateProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.SynthesizeAsync(Request(), cancellation.Token));

        Assert.False(File.Exists(Path.Combine(assetRoot, "audio", "narration", "clip.wav")));
    }

    [Fact]
    public void Constructor_DoesNotDependOnGpuResourceGate()
    {
        var constructor = typeof(GeminiSpeechSynthesisProvider).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(IGpuResourceGate), parameterTypes);
        Assert.Contains(typeof(HttpClient), parameterTypes);
        Assert.Contains(typeof(IOptions<GeminiSpeechOptions>), parameterTypes);
        Assert.Contains(typeof(IAssetFileStore), parameterTypes);
        Assert.Contains(typeof(IProcessRunner), parameterTypes);
        Assert.Contains(typeof(IMediaInspector), parameterTypes);
    }

    private static SpeechSynthesisRequest Request() =>
        new("Teknologi cloud", SpeechVoiceProfile.Formal, "audio/narration/clip.wav");

    private static GeminiSpeechOptions BuildOptions(
        bool enabled = true,
        string apiKey = "test-gemini-key",
        string model = "gemini-2.5-flash-preview-tts",
        int sampleRate = 48000) =>
        new()
        {
            Enabled = enabled,
            ApiKey = apiKey,
            Model = model,
            ExpectedSampleRate = sampleRate,
            TimeoutSeconds = 30
        };

    private GeminiSpeechSynthesisProvider CreateProvider(
        HttpMessageHandler? httpHandler = null,
        IProcessRunner? processRunner = null,
        GeminiSpeechOptions? options = null,
        IMediaInspector? inspector = null)
    {
        var opts = options ?? BuildOptions();
        var handler = httpHandler ?? new StubHttpMessageHandler(_ => SuccessResponse([1, 2, 3, 4]));
        var httpClient = new HttpClient(handler);
        var assetStore = new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = assetRoot }));
        var runner = processRunner ?? CreateDefaultProcessRunner();
        var mediaInspector = inspector ?? FakeMediaInspector.Returning(AudioGenerationTestSupport.Audio(sampleRate: 48000, channels: 1));

        return new GeminiSpeechSynthesisProvider(
            httpClient,
            Options.Create(opts),
            assetStore,
            runner,
            mediaInspector);
    }

    private static FakeProcessRunner CreateDefaultProcessRunner(byte[]? wavBytes = null) =>
        new(request =>
        {
            var outputPath = request.Arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, wavBytes ?? [9, 8, 7, 6]);
            return new ProcessResult(0, string.Empty, string.Empty);
        });

    private static HttpResponseMessage SuccessResponse(
        byte[] rawAudioBytes,
        string mimeType = "audio/pcm;rate=24000") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "candidates": [
                    {
                      "content": {
                        "parts": [
                          {
                            "inlineData": {
                              "mimeType": "{{mimeType}}",
                              "data": "{{Convert.ToBase64String(rawAudioBytes)}}"
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> syncHandler)
            : this((req, _) => Task.FromResult(syncHandler(req)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
