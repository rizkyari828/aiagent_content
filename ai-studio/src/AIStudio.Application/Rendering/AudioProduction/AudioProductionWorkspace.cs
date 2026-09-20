using System.Text;
using System.Text.Json;
using AIStudio.Application.Assets;
using AIStudio.Application.Rendering;

namespace AIStudio.Application.Rendering.AudioProduction;

/// <summary>
/// Deterministic on-disk layout and reuse validation for one production's audio
/// stages. Artifacts live under <c>audio/{projectId}/{storyboardJobId}/</c> in the
/// approved asset root; a single <c>manifest.json</c> records each stage's input
/// fingerprint and artifact metadata.
/// <para>
/// Reuse is intentionally conservative and milestone-specific: a stage is reused
/// only when the manifest fingerprint matches, the file still exists, its SHA-256
/// still matches, and it still probes as usable audio with the recorded
/// sample rate/channel count. This is not a generic cache: it detects changed
/// generation inputs but does not track provider/config versions beyond the
/// production version constant.
/// </para>
/// </summary>
public sealed class AudioProductionWorkspace(
    IAssetFileStore fileStore,
    IMediaInspector mediaInspector)
{
    public const string Version = "audio-production/v2";

    public const string NarrationStage = "narration";
    public const string MusicStage = "music";
    public const string MasterStage = "master";

    public const string NarrationFileName = "narration.wav";
    public const string MusicFileName = "music.wav";
    public const string MasterFileName = "master.wav";

    private const string SceneNarrationDirectory = "narration";

    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static string Directory(Guid contentProjectId, Guid storyboardJobId) =>
        $"audio/{contentProjectId:N}/{storyboardJobId:N}";

    public static string FilePath(
        Guid contentProjectId,
        Guid storyboardJobId,
        string fileName) =>
        $"{Directory(contentProjectId, storyboardJobId)}/{fileName}";

    /// <summary>Logical stage key for one scene's narration fingerprint.</summary>
    public static string SceneStageName(int sceneIndex) => $"narration.scene.{sceneIndex}";

    /// <summary>Deterministic relative path for one scene's narration clip.</summary>
    public static string SceneNarrationPath(
        Guid contentProjectId,
        Guid storyboardJobId,
        int sceneIndex) =>
        $"{Directory(contentProjectId, storyboardJobId)}/{SceneNarrationDirectory}/scene_{sceneIndex}.wav";

    public static string ManifestPath(Guid contentProjectId, Guid storyboardJobId) =>
        FilePath(contentProjectId, storyboardJobId, ManifestFileName);

    public async Task<AudioProductionManifest?> TryReadManifestAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        CancellationToken cancellationToken)
    {
        AssetFileInfo file;
        try
        {
            file = fileStore.Register(ManifestPath(contentProjectId, storyboardJobId));
        }
        catch (AssetCollectionException)
        {
            return null;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(file.AbsolutePath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<AudioProductionManifest>(
                json,
                JsonOptions);
            return manifest is null || manifest.Version != Version ? null : manifest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveManifestAsync(
        Guid contentProjectId,
        Guid storyboardJobId,
        AudioProductionManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await fileStore.WriteAsync(
            ManifestPath(contentProjectId, storyboardJobId),
            Encoding.UTF8.GetBytes(json),
            cancellationToken);
    }

    /// <summary>
    /// Reuses a stage only when <paramref name="fingerprint"/> matches the recorded
    /// input fingerprint and the file still passes integrity and media checks.
    /// </summary>
    public async Task<AudioProductionArtifact?> TryReuseAsync(
        AudioProductionManifest? manifest,
        string stage,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (manifest is null
            || !manifest.Stages.TryGetValue(stage, out var entry)
            || !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return await ResolveAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Resolves the current artifact for a stage without checking the input
    /// fingerprint. Used by the renderer, which needs valid audio but no
    /// generation-input comparison.
    /// </summary>
    public async Task<AudioProductionArtifact?> TryResolveStageAsync(
        AudioProductionManifest? manifest,
        string stage,
        CancellationToken cancellationToken)
    {
        if (manifest is null || !manifest.Stages.TryGetValue(stage, out var entry))
        {
            return null;
        }

        return await ResolveAsync(entry, cancellationToken);
    }

    /// <summary>Registers and probes a freshly produced artifact.</summary>
    public async Task<AudioProductionArtifact> DescribeAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var file = fileStore.Register(relativePath);
        var inspection = await mediaInspector.InspectAsync(
            file.AbsolutePath,
            cancellationToken);

        if (!inspection.HasAudio || inspection.DurationSeconds <= 0)
        {
            throw new AudioProductionException(
                "audio_artifact_invalid",
                "The produced audio artifact is not a usable audio file.");
        }

        return new AudioProductionArtifact(
            file.RelativePath,
            file.AbsolutePath,
            file.ContentHash,
            file.ByteSize,
            inspection.DurationSeconds,
            inspection.SampleRate,
            inspection.Channels);
    }

    public static AudioProductionStage ToStage(
        string fingerprint,
        AudioProductionArtifact artifact) =>
        new(
            fingerprint,
            artifact.RelativePath,
            artifact.ContentHash,
            artifact.ByteSize,
            artifact.DurationSeconds,
            artifact.SampleRate,
            artifact.Channels);

    private async Task<AudioProductionArtifact?> ResolveAsync(
        AudioProductionStage entry,
        CancellationToken cancellationToken)
    {
        AssetFileInfo file;
        try
        {
            file = fileStore.Register(entry.Path);
        }
        catch (AssetCollectionException)
        {
            return null;
        }

        if (!string.Equals(
                file.ContentHash,
                entry.ContentHash,
                StringComparison.OrdinalIgnoreCase)
            || (entry.ByteSize > 0 && file.ByteSize != entry.ByteSize))
        {
            return null;
        }

        try
        {
            var inspection = await mediaInspector.InspectAsync(
                file.AbsolutePath,
                cancellationToken);

            if (!inspection.HasAudio || inspection.DurationSeconds <= 0)
            {
                return null;
            }

            if (entry.SampleRate > 0 && inspection.SampleRate != entry.SampleRate)
            {
                return null;
            }

            if (entry.Channels > 0 && inspection.Channels != entry.Channels)
            {
                return null;
            }

            return new AudioProductionArtifact(
                entry.Path,
                file.AbsolutePath,
                entry.ContentHash,
                entry.ByteSize,
                inspection.DurationSeconds,
                inspection.SampleRate,
                inspection.Channels);
        }
        catch (ProcessExecutionException)
        {
            return null;
        }
    }
}
