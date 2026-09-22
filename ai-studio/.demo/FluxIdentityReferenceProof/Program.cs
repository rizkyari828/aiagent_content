using System.Security.Cryptography;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;

const string ApprovalPhrase = "I_APPROVE_THIS_REFERENCE_FOR_EPHEMERAL_PROOF";

var referencePath = Require("AISTUDIO_IDENTITY_REFERENCE_PATH");
var assetId = new AssetReferenceId(Require("AISTUDIO_IDENTITY_ASSET_ID"));
if (!int.TryParse(Require("AISTUDIO_IDENTITY_ASSET_VERSION"), out var versionValue))
{
    throw new InvalidOperationException("AISTUDIO_IDENTITY_ASSET_VERSION must be a positive integer.");
}

var version = new IdentityAssetVersion(versionValue);
if (!string.Equals(
        Require("AISTUDIO_IDENTITY_REFERENCE_APPROVAL"),
        ApprovalPhrase,
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"AISTUDIO_IDENTITY_REFERENCE_APPROVAL must equal {ApprovalPhrase}.");
}

var bytes = await File.ReadAllBytesAsync(referencePath);
if (!IsPng(bytes))
{
    throw new InvalidOperationException("The proof harness currently accepts only a PNG reference image.");
}

var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var registry = new IdentityAssetRegistry();
var metadata = new IdentityAsset
{
    Id = assetId,
    Version = version,
    Kind = "reference-image",
    MediaType = "image/png",
    ByteSize = bytes.Length,
    ContentHash = hash,
    Status = IdentityAssetStatus.Draft,
    Provenance = new IdentityAssetProvenance
    {
        ProviderId = "user-approved-proof-input"
    },
    CreatedAt = DateTimeOffset.UtcNow
};
registry.Register(metadata);

// This approval is explicit, ephemeral, and exists only inside this proof process.
// It never writes to or bypasses a production registry.
registry.Approve(assetId, version);
var store = new EphemeralIdentityAssetStore(assetId, version, bytes);

var baseUrl = Get("ComfyUi__BaseUrl", "http://127.0.0.1:8188");
var timeoutSeconds = GetInt("ComfyUi__TimeoutSeconds", 600);
var outputDirectory = Require("AISTUDIO_IDENTITY_PROOF_OUTPUT");
Directory.CreateDirectory(outputDirectory);

using var httpClient = new HttpClient
{
    BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute),
    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
};
var provider = new ComfyUiImageGenerationProvider(
    httpClient,
    Options.Create(new ComfyUiOptions
    {
        Enabled = true,
        BaseUrl = baseUrl,
        WorkflowPath = Require("AISTUDIO_T2I_WORKFLOW_PATH"),
        ImageEditWorkflowPath = Require("AISTUDIO_EDIT_WORKFLOW_PATH"),
        TimeoutSeconds = timeoutSeconds,
        Width = GetInt("ComfyUi__Width", 1280),
        Height = GetInt("ComfyUi__Height", 720)
    }),
    new ProofGpuResourceGate(),
    registry,
    store);

var pinned = new PinnedIdentityAsset(assetId, version);
var prompts = new[]
{
    "Preserve the exact person identity and defining facial features; cinematic library interior, medium shot, no text.",
    "Preserve the exact person identity and defining facial features; bright classroom, three-quarter view, no text.",
    "Preserve the exact person identity and defining facial features; evening city walkway, close portrait, no text."
};
var seedBase = GetLong("AISTUDIO_IDENTITY_PROOF_SEED", 42000);

Console.WriteLine($"Pinned identity: {assetId} v{version.Value}");
Console.WriteLine($"Reference SHA-256: {hash}");
for (var index = 0; index < prompts.Length; index++)
{
    var seed = checked(seedBase + index + 1);
    var output = await provider.GenerateAsync(
        new ImageGenerationRequest(prompts[index], seed, [pinned]),
        CancellationToken.None);
    var path = Path.Combine(outputDirectory, $"scene-{index + 1}-seed-{seed}.png");
    await File.WriteAllBytesAsync(path, output);
    Console.WriteLine($"Scene {index + 1}: seed={seed} path={path}");
}

Console.WriteLine("Human review required: compare identity consistency across all three outputs. No pixel-consistency claim is automated.");
return 0;

static string Require(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

static string Get(string name, string fallback) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

static int GetInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
        ? value
        : fallback;

static long GetLong(string name, long fallback) =>
    long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0
        ? value
        : fallback;

static bool IsPng(ReadOnlySpan<byte> value) =>
    value.Length >= 8
    && value[0] == 0x89
    && value[1] == 0x50
    && value[2] == 0x4E
    && value[3] == 0x47
    && value[4] == 0x0D
    && value[5] == 0x0A
    && value[6] == 0x1A
    && value[7] == 0x0A;

file sealed class EphemeralIdentityAssetStore(
    AssetReferenceId expectedId,
    IdentityAssetVersion expectedVersion,
    byte[] content) : IIdentityAssetStore
{
    public Task<IdentityAssetBlob> WriteAsync(
        AssetReferenceId id,
        IdentityAssetVersion version,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The proof store is read-only.");

    public Task<ReadOnlyMemory<byte>> ReadBytesAsync(
        AssetReferenceId id,
        IdentityAssetVersion version,
        CancellationToken cancellationToken)
    {
        if (id != expectedId || version != expectedVersion)
        {
            throw new FileNotFoundException();
        }

        return Task.FromResult<ReadOnlyMemory<byte>>(content);
    }

    public bool Exists(AssetReferenceId id, IdentityAssetVersion version) =>
        id == expectedId && version == expectedVersion;
}

file sealed class ProofGpuResourceGate : IGpuResourceGate
{
    public ValueTask<IAsyncDisposable> AcquireAsync(
        string workloadName,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IAsyncDisposable>(new Lease());

    private sealed class Lease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
