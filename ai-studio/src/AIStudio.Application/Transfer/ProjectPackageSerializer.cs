using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Transfer;

/// <summary>
/// One strict JSON contract for <c>manifest.json</c>. Unknown members are rejected, so
/// an executable or host-specific field cannot be smuggled into a package. Enums use
/// their names (never ordinals) so the format survives enum reordering.
/// </summary>
public static class ProjectPackageSerializer
{
    /// <summary>
    /// Current package contract: v2 adds the A3 review decision (token, note, timestamp)
    /// to the final approval. v1 packages stay importable and restore the legacy approval.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public const int MinimumSchemaVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string FilesEntryPrefix = "files/";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static byte[] Serialize(ProjectTransferManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(manifest, Options);
    }

    public static ProjectTransferManifest Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty)
        {
            throw InvalidManifest("The package manifest is empty.");
        }

        ProjectTransferManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ProjectTransferManifest>(json, Options);
        }
        catch (JsonException exception)
        {
            throw InvalidManifest(
                "The package manifest is not valid JSON for a supported schema version.",
                exception);
        }

        return manifest
            ?? throw InvalidManifest("The package manifest is missing.");
    }

    public static string CurrentAppVersion()
    {
        var version = typeof(ProjectPackageSerializer).Assembly.GetName().Version;
        return version?.ToString() ?? "unknown";
    }

    private static ProjectTransferException InvalidManifest(
        string message,
        Exception? innerException = null) =>
        new("import_package_invalid", message, innerException);
}
