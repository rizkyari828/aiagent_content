using System.Security.Cryptography;
using AIStudio.Application.Bibles;

namespace AIStudio.Application.IdentityAssets;

/// <summary>
/// Production-capable authoring of an EXISTING reference image. It never generates
/// imagery: the caller supplies the bytes, the workflow validates them, persists
/// them through the byte-authority <see cref="IIdentityAssetStore"/>, and registers
/// a Draft metadata record through the metadata-authority
/// <see cref="IIdentityAssetRegistry"/>. Approval is always a separate explicit
/// action, so import never produces an Approved asset.
///
/// Ordering: every check runs before durable mutation, then bytes are written, then
/// Draft metadata is registered. Metadata is therefore never exposed without bytes.
/// If registration fails after the bytes exist, only unreachable bytes remain (the
/// store and registry expose no delete/rollback); a retry re-allocates the same
/// version and the store write is idempotent for identical bytes.
/// </summary>
public sealed class ImportIdentityAssetWorkflow(
    IIdentityAssetStore store,
    IIdentityAssetRegistry registry,
    TimeProvider timeProvider)
{
    public const string SupportedKind = "reference-image";
    public const string SupportedMediaType = "image/png";

    public async Task<IdentityAsset> ImportAsync(
        ImportIdentityAssetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.AssetId.Value))
        {
            throw Error(
                "identity_import_invalid_id",
                "A valid asset reference id is required.");
        }

        if (!string.Equals(request.Kind, SupportedKind, StringComparison.Ordinal))
        {
            throw Error(
                "identity_import_kind_unsupported",
                $"Identity asset kind '{request.Kind}' is not supported; only '{SupportedKind}' can be imported in v1.");
        }

        if (!string.Equals(request.MediaType, SupportedMediaType, StringComparison.Ordinal))
        {
            throw Error(
                "identity_import_media_type_unsupported",
                $"Identity asset media type '{request.MediaType}' is not supported; only '{SupportedMediaType}' can be imported in v1.");
        }

        if (request.Content is null || request.Content.Length == 0)
        {
            throw Error(
                "identity_import_content_empty",
                "Identity asset content is empty.");
        }

        if (!HasPngSignature(request.Content))
        {
            throw Error(
                "identity_import_png_invalid",
                "Identity asset content is not a PNG image.");
        }

        var version = registry.NextVersion(request.AssetId);

        // Validate the complete candidate metadata (including provenance) before any
        // durable write, so invalid input can never leave orphaned bytes.
        var candidate = new IdentityAsset
        {
            Id = request.AssetId,
            Version = version,
            Kind = request.Kind,
            MediaType = request.MediaType,
            ByteSize = request.Content.Length,
            ContentHash = ContentHash(request.Content),
            Status = IdentityAssetStatus.Draft,
            Provenance = request.Provenance ?? new IdentityAssetProvenance(),
            CreatedAt = timeProvider.GetUtcNow()
        };

        var issues = IdentityAssetValidator.Validate(candidate);
        if (issues.Count > 0)
        {
            throw Error(
                "identity_import_invalid",
                $"Identity asset metadata is invalid: {issues[0].Code}.");
        }

        var blob = await store.WriteAsync(
            request.AssetId,
            version,
            request.Content,
            cancellationToken);

        // The store is the byte/hash/size authority: never register metadata that
        // disagrees with the bytes the store actually persisted.
        if (blob.ByteSize != candidate.ByteSize
            || !string.Equals(blob.ContentHash, candidate.ContentHash, StringComparison.Ordinal))
        {
            throw Error(
                "identity_import_store_mismatch",
                "The identity asset store returned bytes that do not match the imported content.");
        }

        registry.Register(candidate);
        return registry.Get(request.AssetId, version);
    }

    private static string ContentHash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool HasPngSignature(ReadOnlySpan<byte> value) =>
        value.Length >= 8
        && value[0] == 0x89
        && value[1] == 0x50
        && value[2] == 0x4E
        && value[3] == 0x47
        && value[4] == 0x0D
        && value[5] == 0x0A
        && value[6] == 0x1A
        && value[7] == 0x0A;

    private static IdentityAssetAuthoringException Error(string errorCode, string message) =>
        new(errorCode, message);
}

/// <summary>
/// Authoring input for one existing reference image. It carries only application
/// identity and bytes — never a filesystem path, storage key, or provider detail.
/// </summary>
public sealed record ImportIdentityAssetRequest(
    AssetReferenceId AssetId,
    string Kind,
    string MediaType,
    byte[] Content,
    IdentityAssetProvenance? Provenance = null);
