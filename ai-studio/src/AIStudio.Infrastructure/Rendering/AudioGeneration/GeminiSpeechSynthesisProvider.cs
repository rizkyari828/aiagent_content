using System.Globalization;
using System.Text;
using System.Text.Json;
using AIStudio.Application.Assets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioGeneration;
using Microsoft.Extensions.Options;

namespace AIStudio.Infrastructure.Rendering.AudioGeneration;

/// <summary>
/// Cloud speech synthesis provider using Google's Gemini multimodal TTS API.
/// Maps curated Indonesian narration voice profiles to Gemini prebuilt voices
/// with explicit Indonesian style guidance, then normalizes and resamples
/// the returned audio to a standard 48 kHz mono WAV file via FFmpeg.
/// Runs cloud-side and CPU-only; does not require GPU resources.
/// </summary>
public sealed class GeminiSpeechSynthesisProvider : ISpeechSynthesisProvider
{
    private const int MaxTextLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<SpeechVoiceProfile, (string VoiceName, string StylePrefix)> VoiceProfiles =
        new Dictionary<SpeechVoiceProfile, (string VoiceName, string StylePrefix)>
        {
            [SpeechVoiceProfile.Formal] =
                ("Kore", "Ucapkan dengan suara profesional, tenang, dan jelas dalam Bahasa Indonesia: "),
            [SpeechVoiceProfile.Playful] =
                ("Puck", "Ucapkan dengan suara ramah, ceria, dan ekspresif dalam Bahasa Indonesia: "),
            [SpeechVoiceProfile.Energetic] =
                ("Fenrir", "Ucapkan dengan suara bersemangat dan dinamis dalam Bahasa Indonesia: "),
            [SpeechVoiceProfile.Documentary] =
                ("Charon", "Ucapkan dengan suara dokumenter yang hangat dan sinematik dalam Bahasa Indonesia: ")
        };

    private readonly GeminiSpeechOptions options;
    private readonly HttpClient httpClient;
    private readonly IAssetFileStore assetFileStore;
    private readonly IProcessRunner processRunner;
    private readonly IMediaInspector mediaInspector;

    public GeminiSpeechSynthesisProvider(
        HttpClient httpClient,
        IOptions<GeminiSpeechOptions> options,
        IAssetFileStore assetFileStore,
        IProcessRunner processRunner,
        IMediaInspector mediaInspector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(assetFileStore);
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(mediaInspector);

        this.options = options.Value;
        this.httpClient = httpClient;
        this.assetFileStore = assetFileStore;
        this.processRunner = processRunner;
        this.mediaInspector = mediaInspector;
    }

    public bool IsEnabled => options.Enabled && !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<SpeechSynthesisResult> SynthesizeAsync(
        SpeechSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_disabled",
                "The Gemini speech synthesis engine is not enabled or API key is not configured.");
        }

        Validate(request);

        var (voiceName, stylePrefix) = VoiceProfiles[request.Voice];
        var promptText = stylePrefix + request.Text.Trim();

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = promptText }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "AUDIO" },
                speechConfig = new
                {
                    voiceConfig = new
                    {
                        prebuiltVoiceConfig = new
                        {
                            voiceName
                        }
                    }
                }
            }
        };

        var baseString = !string.IsNullOrWhiteSpace(options.BaseUrl)
            ? options.BaseUrl.TrimEnd('/') + "/"
            : "https://generativelanguage.googleapis.com/";

        var baseAddress = httpClient.BaseAddress ?? new Uri(baseString, UriKind.Absolute);
        var relativeEndpoint = $"v1beta/models/{Uri.EscapeDataString(options.Model)}:generateContent";
        var requestUri = new Uri(baseAddress, relativeEndpoint);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri);
        httpRequest.Headers.TryAddWithoutValidation("x-goog-api-key", options.ApiKey);

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        httpRequest.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await httpClient.SendAsync(httpRequest, linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_timeout",
                $"Gemini speech synthesis timed out after {options.TimeoutSeconds} seconds.",
                ex);
        }
        catch (TimeoutException ex)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_timeout",
                $"Gemini speech synthesis timed out after {options.TimeoutSeconds} seconds.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "Gemini speech synthesis HTTP request failed.",
                ex);
        }
        catch (Exception ex)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "An unexpected error occurred while communicating with the Gemini API.",
                ex);
        }

        string responseJson;
        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_failed",
                    $"Gemini speech synthesis returned HTTP {(int)httpResponse.StatusCode}.");
            }

            try
            {
                responseJson = await httpResponse.Content.ReadAsStringAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_timeout",
                    $"Gemini speech synthesis timed out after {options.TimeoutSeconds} seconds.",
                    ex);
            }
            catch (Exception ex)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_failed",
                    "Failed to read Gemini response content.",
                    ex);
            }
        }

        string? audioData = null;
        string? mimeType = null;

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            if (root.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts)
                    && parts.ValueKind == JsonValueKind.Array
                    && parts.GetArrayLength() > 0)
                {
                    var firstPart = parts[0];
                    if (firstPart.TryGetProperty("inlineData", out var inlineData))
                    {
                        if (inlineData.TryGetProperty("data", out var dataProp))
                        {
                            audioData = dataProp.GetString();
                        }

                        if (inlineData.TryGetProperty("mimeType", out var mimeTypeProp))
                        {
                            mimeType = mimeTypeProp.GetString();
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "Failed to parse Gemini response JSON.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(audioData))
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "Gemini response did not contain inline audio data.");
        }

        byte[] rawAudio;
        try
        {
            rawAudio = Convert.FromBase64String(audioData);
        }
        catch (FormatException ex)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "Gemini audio data is not valid base64.",
                ex);
        }

        if (rawAudio.Length == 0)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "Gemini returned empty audio data.");
        }

        var directory = Directory.CreateTempSubdirectory("aistudio-gemini-speech-");
        try
        {
            var rawPath = Path.Combine(directory.FullName, "raw.audio");
            var normalizedWavPath = Path.Combine(directory.FullName, "speech.wav");

            await File.WriteAllBytesAsync(rawPath, rawAudio, cancellationToken);

            var isPcm = !string.IsNullOrWhiteSpace(mimeType) &&
                (mimeType.StartsWith("audio/L16", StringComparison.OrdinalIgnoreCase)
                 || mimeType.StartsWith("audio/pcm", StringComparison.OrdinalIgnoreCase));

            var arguments = new List<string> { "-y" };
            if (isPcm)
            {
                arguments.AddRange(["-f", "s16le", "-ar", "24000", "-ac", "1", "-i", rawPath]);
            }
            else
            {
                arguments.AddRange(["-i", rawPath]);
            }

            arguments.AddRange([
                "-ar",
                options.ExpectedSampleRate.ToString(CultureInfo.InvariantCulture),
                "-ac",
                "1",
                "-c:a",
                "pcm_s16le",
                normalizedWavPath
            ]);

            ProcessResult execution;
            try
            {
                execution = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        options.FfmpegPath,
                        arguments,
                        TimeSpan.FromSeconds(options.TimeoutSeconds)),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ProcessExecutionException ex)
                when (ex.ErrorCode == ProcessExecutionException.TimedOut)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_timeout",
                    $"FFmpeg speech normalization timed out after {options.TimeoutSeconds} seconds.",
                    ex);
            }
            catch (ProcessExecutionException ex)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_failed",
                    "FFmpeg speech normalization failed to execute.",
                    ex);
            }

            if (execution.ExitCode != 0)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_failed",
                    $"FFmpeg speech normalization exited with code {execution.ExitCode}.");
            }

            if (!File.Exists(normalizedWavPath))
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    "The normalized speech file was not produced.");
            }

            var wavBytes = await File.ReadAllBytesAsync(normalizedWavPath, cancellationToken);
            if (wavBytes.Length == 0)
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    "The normalized speech output was empty.");
            }

            AssetFileInfo stored;
            try
            {
                stored = await assetFileStore.WriteAsync(
                    request.RelativeOutputPath,
                    wavBytes,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (AssetCollectionException ex)
                when (ex.ErrorCode == "asset_path_invalid")
            {
                throw new SpeechSynthesisException(
                    "speech_invalid_input",
                    "The speech output path is not valid.",
                    ex);
            }
            catch (AssetCollectionException ex)
            {
                throw new SpeechSynthesisException(
                    "speech_synthesis_failed",
                    "The generated speech could not be stored.",
                    ex);
            }

            var inspection = await InspectAsync(stored.AbsolutePath, cancellationToken);
            if (!inspection.HasAudio || inspection.DurationSeconds <= 0)
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    "The speech output is not a usable audio file.");
            }

            if (options.ExpectedSampleRate > 0 && inspection.SampleRate != options.ExpectedSampleRate)
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    $"The speech output sample rate is not {options.ExpectedSampleRate} Hz.");
            }

            if (inspection.Channels != 1)
            {
                throw new SpeechSynthesisException(
                    "speech_output_invalid",
                    "The speech output is not 1-channel mono audio.");
            }

            return new SpeechSynthesisResult(
                stored.RelativePath,
                stored.ContentHash,
                stored.ByteSize,
                inspection.DurationSeconds,
                inspection.SampleRate,
                request.Voice);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static void Validate(SpeechSynthesisRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "Speech text is required.");
        }

        if (request.Text.Length > MaxTextLength)
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                $"Speech text must be at most {MaxTextLength} characters.");
        }

        if (!Enum.IsDefined(request.Voice))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "The requested voice profile is not supported.");
        }

        if (string.IsNullOrWhiteSpace(request.RelativeOutputPath))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "A relative output path is required.");
        }

        if (request.Locale is not null
            && !request.Locale.Replace('_', '-')
                .StartsWith("id", StringComparison.OrdinalIgnoreCase))
        {
            throw new SpeechSynthesisException(
                "speech_invalid_input",
                "Only the Indonesian locale is supported.");
        }
    }

    private async Task<MediaInspection> InspectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mediaInspector.InspectAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.StartFailed)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_failed",
                "Unable to start the media inspector.",
                exception);
        }
        catch (ProcessExecutionException exception)
            when (exception.ErrorCode == ProcessExecutionException.TimedOut)
        {
            throw new SpeechSynthesisException(
                "speech_synthesis_timeout",
                "Media inspection timed out.",
                exception);
        }
        catch (ProcessExecutionException exception)
        {
            throw new SpeechSynthesisException(
                "speech_output_invalid",
                "The speech output could not be inspected.",
                exception);
        }
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            if (directory.Exists)
            {
                directory.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
