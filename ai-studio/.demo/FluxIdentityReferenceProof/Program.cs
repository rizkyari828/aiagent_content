// Anime Identity Visual Proof Harness v1 — USER-RUN only.
//
// Proves that ONE explicitly user-approved PNG identity reference is consumed by
// the real ComfyUiImageGenerationProvider across three materially different scenes
// while every request carries the SAME single pinned (AssetReferenceId, Version).
//
// This harness does NOT create a production asset: the registry stays in-memory and
// the explicit approval transition exists only inside this process.
//
// Run the deterministic self-check (no ComfyUI, no generation):
//   dotnet run --project .demo/FluxIdentityReferenceProof -- --self-check
//
// Run the real three-scene proof through scripts/e2e-flux-identity-reference.sh.

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Rendering;
using Microsoft.Extensions.Options;

const string ApprovalPhrase = "I_APPROVE_THIS_REFERENCE_FOR_EPHEMERAL_PROOF";

if (args.Contains("--self-check", StringComparer.Ordinal))
{
    return HarnessSelfCheck.Run();
}

await HarnessProof.RunAsync(ApprovalPhrase, CancellationToken.None);
return 0;

file static class HarnessEnv
{
    public const string ReferencePath = "AISTUDIO_IDENTITY_REFERENCE_PATH";
    public const string AssetId = "AISTUDIO_IDENTITY_ASSET_ID";
    public const string AssetVersion = "AISTUDIO_IDENTITY_ASSET_VERSION";
    public const string Approval = "AISTUDIO_IDENTITY_REFERENCE_APPROVAL";
    public const string Output = "AISTUDIO_IDENTITY_PROOF_OUTPUT";
    public const string Seed = "AISTUDIO_IDENTITY_PROOF_SEED";
    public const string T2iWorkflow = "AISTUDIO_T2I_WORKFLOW_PATH";
    public const string EditWorkflow = "AISTUDIO_EDIT_WORKFLOW_PATH";

    public static string Require(Func<string, string?> get, string name) =>
        get(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Required environment variable '{name}' is not set.");

    public static string Get(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    public static int GetInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;

    public static long GetLong(string name, long fallback) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0
            ? value
            : fallback;
}

file sealed record ProofInputs(string ReferencePath, AssetReferenceId AssetId, IdentityAssetVersion Version);

file sealed record ScenePlan(string Prompt, long Seed);

file sealed record ReferenceMetadata(
    string AssetId,
    int Version,
    string Status,
    string Kind,
    string MediaType,
    long ByteSize,
    string ContentHash,
    string? ApprovedAt,
    string SourcePath,
    string? ProviderId);

file sealed record SceneRequestMetadata(
    int Scene,
    string Prompt,
    long Seed,
    string AssetId,
    int Version,
    string ContentHash,
    string Workflow,
    string ResultPath);

file sealed record ProofSummary(
    string GeneratedAtUtc,
    string AssetId,
    int Version,
    string ContentHash,
    string ReferencePath,
    string ReferenceArtifactPath,
    string T2iWorkflow,
    string EditWorkflow,
    string BaseUrl,
    int Width,
    int Height,
    bool SamePinAcrossScenes,
    IReadOnlyList<SceneRequestMetadata> Scenes);

file static class HarnessCore
{
    public const int SceneCount = 3;

    // The reference image is the primary identity anchor: one compact clause names the
    // canonical stable traits, and the three scenes vary only pose/camera/action/lighting.
    private const string IdentityAnchor =
        "Keep exactly the same person as the reference image: identical face, young adult, "
        + "slender build, short black hair, dark brown eyes, small scar on the left brow. ";

    private static readonly string[] ScenePrompts =
    [
        IdentityAnchor
            + "Scene 1 of 3, neutral establishing view: standing still, facing the camera, "
            + "calm neutral expression, even soft daylight, plain background, full upper body. "
            + "No text, no watermark.",
        IdentityAnchor
            + "Scene 2 of 3, different pose and camera angle: seated in three-quarter side view, "
            + "actively typing on a computer keyboard, focused expression, medium shot, indoor room. "
            + "No text, no watermark.",
        IdentityAnchor
            + "Scene 3 of 3, different mood and lighting: close portrait at night, the only light "
            + "is a dark computer monitor with a faint screen reflection on the face, low-key moody "
            + "lighting, quiet contemplative expression. No text, no watermark."
    ];

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ProofInputs ParseInputs(Func<string, string?> get, string approvalPhrase)
    {
        var referencePath = HarnessEnv.Require(get, HarnessEnv.ReferencePath);
        var assetId = new AssetReferenceId(HarnessEnv.Require(get, HarnessEnv.AssetId));
        var versionText = HarnessEnv.Require(get, HarnessEnv.AssetVersion);
        if (!int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var versionValue)
            || versionValue < IdentityAssetVersion.Minimum)
        {
            throw new InvalidOperationException(
                $"{HarnessEnv.AssetVersion} must be a positive integer.");
        }

        var approval = HarnessEnv.Require(get, HarnessEnv.Approval);
        if (!string.Equals(approval, approvalPhrase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{HarnessEnv.Approval} must equal {approvalPhrase}. Set it only when you approve this exact PNG for this ephemeral proof.");
        }

        return new ProofInputs(referencePath, assetId, new IdentityAssetVersion(versionValue));
    }

    public static bool IsPng(ReadOnlySpan<byte> value) =>
        value.Length >= 8
        && value[0] == 0x89
        && value[1] == 0x50
        && value[2] == 0x4E
        && value[3] == 0x47
        && value[4] == 0x0D
        && value[5] == 0x0A
        && value[6] == 0x1A
        && value[7] == 0x0A;

    public static string RequirePng(byte[] bytes)
    {
        if (!IsPng(bytes))
        {
            throw new InvalidOperationException("The proof harness accepts only a PNG reference image.");
        }

        return Sha256(bytes);
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static IIdentityAssetStore CreateStore(string rootPath) =>
        new LocalIdentityAssetStore(new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = rootPath })));

    public static IdentityAsset BuildDraft(ProofInputs inputs, IdentityAssetBlob blob) => new()
    {
        Id = inputs.AssetId,
        Version = inputs.Version,
        Kind = "reference-image",
        MediaType = "image/png",
        ByteSize = blob.ByteSize,
        ContentHash = blob.ContentHash,
        Status = IdentityAssetStatus.Draft,
        Provenance = new IdentityAssetProvenance { ProviderId = "user-approved-proof-input" },
        CreatedAt = DateTimeOffset.UtcNow
    };

    public static ScenePlan[] BuildScenePlans(long seedBase) =>
        [.. ScenePrompts.Select((prompt, index) => new ScenePlan(prompt, seedBase + index + 1))];

    public static ImageGenerationRequest[] BuildRequests(
        PinnedIdentityAsset pin,
        IReadOnlyList<ScenePlan> plans)
    {
        var requests = new ImageGenerationRequest[plans.Count];
        for (var index = 0; index < plans.Count; index++)
        {
            // The exact same concrete pin instance is reused for every scene. No scene
            // re-resolves a version and no latest lookup is involved.
            requests[index] = new ImageGenerationRequest(plans[index].Prompt, plans[index].Seed, [pin]);
        }

        return requests;
    }

    public static void RequireSinglePin(ImageGenerationRequest[] requests, PinnedIdentityAsset pin)
    {
        if (requests.Length != SceneCount)
        {
            throw new InvalidOperationException(
                $"Expected exactly {SceneCount} scene requests but built {requests.Length}.");
        }

        foreach (var request in requests)
        {
            if (request.IdentityReferences.Count != 1
                || !ReferenceEquals(request.IdentityReferences[0], pin))
            {
                throw new InvalidOperationException(
                    "Every scene request must carry the same single pinned identity.");
            }
        }
    }
}

file static class HarnessProof
{
    public static async Task RunAsync(string approvalPhrase, CancellationToken cancellationToken)
    {
        var inputs = HarnessCore.ParseInputs(Environment.GetEnvironmentVariable, approvalPhrase);
        if (!File.Exists(inputs.ReferencePath))
        {
            throw new InvalidOperationException(
                $"The reference PNG does not exist: {inputs.ReferencePath}");
        }

        var bytes = await File.ReadAllBytesAsync(inputs.ReferencePath, cancellationToken);
        var sourceHash = HarnessCore.RequirePng(bytes);
        var outputDirectory = HarnessEnv.Require(Environment.GetEnvironmentVariable, HarnessEnv.Output);
        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine($"Pinned identity: {inputs.AssetId} v{inputs.Version.Value}");
        Console.WriteLine($"Reference: {inputs.ReferencePath}");
        Console.WriteLine($"Size: {bytes.Length} bytes");
        Console.WriteLine($"hash: {sourceHash}");

        var storeRoot = Path.Combine(
            Path.GetTempPath(),
            $"aistudio-identity-proof-{Guid.NewGuid():N}");
        try
        {
            var store = HarnessCore.CreateStore(storeRoot);
            var blob = await store.WriteAsync(inputs.AssetId, inputs.Version, bytes, cancellationToken);
            if (blob.ByteSize != bytes.Length
                || !string.Equals(blob.ContentHash, sourceHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Store bytes do not match the supplied reference image.");
            }

            var registry = new IdentityAssetRegistry();
            registry.Register(HarnessCore.BuildDraft(inputs, blob));
            if (registry.Get(inputs.AssetId, inputs.Version).Status != IdentityAssetStatus.Draft)
            {
                throw new InvalidOperationException("The reference asset did not begin as Draft.");
            }

            var approved = registry.Approve(inputs.AssetId, inputs.Version);
            if (approved.Status != IdentityAssetStatus.Approved)
            {
                throw new InvalidOperationException("The reference asset was not explicitly approved.");
            }

            var pin = new PinnedIdentityAsset(inputs.AssetId, inputs.Version);
            var plans = HarnessCore.BuildScenePlans(HarnessEnv.GetLong(HarnessEnv.Seed, 42000));
            var requests = HarnessCore.BuildRequests(pin, plans);
            HarnessCore.RequireSinglePin(requests, pin);

            var editWorkflow = HarnessEnv.Require(Environment.GetEnvironmentVariable, HarnessEnv.EditWorkflow);
            var provider = CreateProvider(registry, store, editWorkflow);
            var referenceArtifact = await WriteReferenceArtifacts(
                outputDirectory,
                inputs,
                approved,
                bytes,
                cancellationToken);

            var scenes = new List<SceneRequestMetadata>(plans.Length);
            for (var index = 0; index < plans.Length; index++)
            {
                var sceneDirectory = Path.Combine(outputDirectory, $"scene-{index + 1}");
                Directory.CreateDirectory(sceneDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(sceneDirectory, "prompt.txt"),
                    plans[index].Prompt,
                    cancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(sceneDirectory, "seed.txt"),
                    plans[index].Seed.ToString(CultureInfo.InvariantCulture),
                    cancellationToken);

                var result = await provider.GenerateAsync(requests[index], cancellationToken);
                if (result.Length == 0)
                {
                    throw new InvalidOperationException($"Scene {index + 1} produced no image bytes.");
                }

                var resultPath = Path.Combine(sceneDirectory, "result.png");
                await File.WriteAllBytesAsync(resultPath, result, cancellationToken);

                var metadata = new SceneRequestMetadata(
                    index + 1,
                    plans[index].Prompt,
                    plans[index].Seed,
                    pin.AssetId.Value,
                    pin.Version.Value,
                    sourceHash,
                    Path.GetFileName(editWorkflow),
                    resultPath);
                await File.WriteAllTextAsync(
                    Path.Combine(sceneDirectory, "request.json"),
                    JsonSerializer.Serialize(metadata, HarnessCore.JsonOptions),
                    cancellationToken);
                scenes.Add(metadata);
                Console.WriteLine($"Scene {index + 1} generated: seed={metadata.Seed} output={resultPath}");
            }

            var summary = new ProofSummary(
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                pin.AssetId.Value,
                pin.Version.Value,
                sourceHash,
                inputs.ReferencePath,
                referenceArtifact,
                Path.GetFileName(HarnessEnv.Require(Environment.GetEnvironmentVariable, HarnessEnv.T2iWorkflow)),
                Path.GetFileName(editWorkflow),
                GetBaseUrl(),
                HarnessEnv.GetInt("ComfyUi__Width", 1280),
                HarnessEnv.GetInt("ComfyUi__Height", 720),
                SamePinAcrossScenes: true,
                scenes);
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "summary.json"),
                JsonSerializer.Serialize(summary, HarnessCore.JsonOptions),
                cancellationToken);

            PrintReview(scenes, pin);
        }
        finally
        {
            TryDelete(storeRoot);
        }
    }

    private static ComfyUiImageGenerationProvider CreateProvider(
        IIdentityAssetRegistry registry,
        IIdentityAssetStore store,
        string editWorkflow)
    {
        var baseUrl = GetBaseUrl();
        var timeoutSeconds = HarnessEnv.GetInt("ComfyUi__TimeoutSeconds", 600);
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        return new ComfyUiImageGenerationProvider(
            httpClient,
            Options.Create(new ComfyUiOptions
            {
                Enabled = true,
                BaseUrl = baseUrl,
                WorkflowPath = HarnessEnv.Require(Environment.GetEnvironmentVariable, HarnessEnv.T2iWorkflow),
                ImageEditWorkflowPath = editWorkflow,
                TimeoutSeconds = timeoutSeconds,
                Width = HarnessEnv.GetInt("ComfyUi__Width", 1280),
                Height = HarnessEnv.GetInt("ComfyUi__Height", 720)
            }),
            new ProofGpuResourceGate(),
            registry,
            store);
    }

    private static async Task<string> WriteReferenceArtifacts(
        string outputDirectory,
        ProofInputs inputs,
        IdentityAsset approved,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var referenceDirectory = Path.Combine(outputDirectory, "reference");
        Directory.CreateDirectory(referenceDirectory);
        var referencePath = Path.Combine(referenceDirectory, "reference.png");
        await File.WriteAllBytesAsync(referencePath, bytes, cancellationToken);

        var metadata = new ReferenceMetadata(
            approved.Id.Value,
            approved.Version.Value,
            approved.Status.ToString(),
            approved.Kind,
            approved.MediaType,
            approved.ByteSize,
            approved.ContentHash,
            approved.ApprovedAt?.ToString("O", CultureInfo.InvariantCulture),
            inputs.ReferencePath,
            approved.Provenance.ProviderId);
        await File.WriteAllTextAsync(
            Path.Combine(referenceDirectory, "metadata.json"),
            JsonSerializer.Serialize(metadata, HarnessCore.JsonOptions),
            cancellationToken);
        return referencePath;
    }

    private static void PrintReview(IReadOnlyList<SceneRequestMetadata> scenes, PinnedIdentityAsset pin)
    {
        Console.WriteLine();
        Console.WriteLine("Pinned identity reused across all scenes:");
        Console.WriteLine($"  {pin.AssetId} v{pin.Version.Value}");
        Console.WriteLine();
        Console.WriteLine("Scene   Seed    Same pinned asset   Output");
        foreach (var scene in scenes)
        {
            Console.WriteLine($"  {scene.Scene}     {scene.Seed}   yes                 {scene.ResultPath}");
        }

        Console.WriteLine();
        Console.WriteLine("Visual review (human authoritative; this harness never auto-marks PASS):");
        Console.WriteLine("  FACE / IDENTITY       USER REVIEW");
        Console.WriteLine("  HAIR                  USER REVIEW");
        Console.WriteLine("  BODY                  USER REVIEW");
        Console.WriteLine("  DISTINGUISHING TRAIT  USER REVIEW");
        Console.WriteLine("  OUTFIT DRIFT          USER REVIEW");
        Console.WriteLine("  WORLD CONSISTENCY     USER REVIEW");
    }

    private static string GetBaseUrl() => HarnessEnv.Get("ComfyUi__BaseUrl", "http://127.0.0.1:8188");

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
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

file static class HarnessSelfCheck
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static int Run()
    {
        const string phrase = "I_APPROVE_THIS_REFERENCE_FOR_EPHEMERAL_PROOF";
        var passed = 0;
        var total = 0;

        void Check(string name, Action body)
        {
            total++;
            try
            {
                body();
                passed++;
                Console.WriteLine($"  PASS {name}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"  FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine("Flux identity visual proof — deterministic self-check");

        Check("missing reference PNG fails", () =>
            Throws<InvalidOperationException>(
                () => HarnessCore.ParseInputs(_ => null, phrase),
                "AISTUDIO_IDENTITY_REFERENCE_PATH"));

        Check("missing explicit approval fails before generation", () =>
            Throws<InvalidOperationException>(
                () => HarnessCore.ParseInputs(
                    Lookup(
                        (HarnessEnv.ReferencePath, "/tmp/reference.png"),
                        (HarnessEnv.AssetId, "student-01-reference"),
                        (HarnessEnv.AssetVersion, "1")),
                    phrase),
                "AISTUDIO_IDENTITY_REFERENCE_APPROVAL"));

        Check("non-true approval value fails before generation", () =>
            Throws<InvalidOperationException>(
                () => HarnessCore.ParseInputs(
                    Lookup(
                        (HarnessEnv.ReferencePath, "/tmp/reference.png"),
                        (HarnessEnv.AssetId, "student-01-reference"),
                        (HarnessEnv.AssetVersion, "1"),
                        (HarnessEnv.Approval, "true")),
                    phrase),
                "must equal"));

        Check("invalid PNG fails", () =>
            Throws<InvalidOperationException>(
                () => HarnessCore.RequirePng([1, 2, 3]),
                "PNG"));

        Check("valid PNG signature accepted", () =>
            Equal(HarnessCore.Sha256(PngSignature), HarnessCore.RequirePng(PngSignature)));

        var inputs = new ProofInputs(
            "/tmp/reference.png",
            new AssetReferenceId("student-01-reference"),
            new IdentityAssetVersion(1));
        var blob = new IdentityAssetBlob(PngSignature.Length, HarnessCore.Sha256(PngSignature));

        Check("asset begins Draft", () =>
        {
            var registry = new IdentityAssetRegistry();
            registry.Register(HarnessCore.BuildDraft(inputs, blob));
            Equal(IdentityAssetStatus.Draft, registry.Get(inputs.AssetId, inputs.Version).Status);
        });

        Check("explicit approve transitions Draft to Approved", () =>
        {
            var registry = new IdentityAssetRegistry();
            registry.Register(HarnessCore.BuildDraft(inputs, blob));
            var approved = registry.Approve(inputs.AssetId, inputs.Version);
            Equal(IdentityAssetStatus.Approved, approved.Status);
            Equal(approved.Id, registry.Get(inputs.AssetId, inputs.Version).Id);
        });

        var plans = HarnessCore.BuildScenePlans(42000);
        var pin = new PinnedIdentityAsset(inputs.AssetId, inputs.Version);

        Check("exactly three scenes", () =>
        {
            Equal(HarnessCore.SceneCount, plans.Length);
            Equal(HarnessCore.SceneCount, HarnessCore.BuildRequests(pin, plans).Length);
        });

        Check("fixed seeds recorded", () =>
        {
            Equal(42001L, plans[0].Seed);
            Equal(42002L, plans[1].Seed);
            Equal(42003L, plans[2].Seed);
            Equal(plans.Length, plans.Select(plan => plan.Seed).Distinct().Count());
        });

        Check("one concrete version pinned once and reused", () =>
        {
            var requests = HarnessCore.BuildRequests(pin, plans);
            HarnessCore.RequireSinglePin(requests, pin);
            foreach (var request in requests)
            {
                True(ReferenceEquals(request.IdentityReferences[0], pin));
                Equal(inputs.Version, request.IdentityReferences[0].Version);
            }
        });

        Check("no latest-version resolution during the scene loop", () =>
        {
            var registry = new IdentityAssetRegistry();
            registry.Register(HarnessCore.BuildDraft(inputs, blob));
            registry.Register(HarnessCore.BuildDraft(inputs with { Version = new IdentityAssetVersion(2) }, blob));
            registry.Approve(inputs.AssetId, new IdentityAssetVersion(2));
            registry.Approve(inputs.AssetId, inputs.Version);

            var requests = HarnessCore.BuildRequests(pin, plans);
            HarnessCore.RequireSinglePin(requests, pin);
            Equal(2, registry.GetLatestApproved(inputs.AssetId).Version.Value);
            True(requests.All(request => request.IdentityReferences[0].Version.Value == 1));
        });

        Check("artifacts serialize correctly", () =>
        {
            var summary = new ProofSummary(
                "2026-09-22T00:00:00.0000000+00:00",
                inputs.AssetId.Value,
                inputs.Version.Value,
                blob.ContentHash,
                inputs.ReferencePath,
                "reference/reference.png",
                "flux2_klein_4b_distilled.json",
                "flux2_klein_4b_distilled_edit.json",
                "http://127.0.0.1:8188",
                1280,
                720,
                SamePinAcrossScenes: true,
                [new SceneRequestMetadata(
                    1,
                    plans[0].Prompt,
                    plans[0].Seed,
                    inputs.AssetId.Value,
                    inputs.Version.Value,
                    blob.ContentHash,
                    "flux2_klein_4b_distilled_edit.json",
                    "scene-1/result.png")]);

            var json = JsonSerializer.Serialize(summary, HarnessCore.JsonOptions);
            var roundTrip = JsonSerializer.Deserialize<ProofSummary>(json, HarnessCore.JsonOptions)
                ?? throw new InvalidOperationException("summary.json did not round-trip.");
            Equal(summary.AssetId, roundTrip.AssetId);
            Equal(summary.Version, roundTrip.Version);
            Equal(summary.Scenes.Count, roundTrip.Scenes.Count);
            True(roundTrip.SamePinAcrossScenes);
        });

        Check("user source PNG is not modified", () =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"aistudio-identity-selfcheck-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var sourcePath = Path.Combine(directory, "source.png");
                File.WriteAllBytes(sourcePath, PngSignature);
                var before = HarnessCore.Sha256(File.ReadAllBytes(sourcePath));

                var store = HarnessCore.CreateStore(Path.Combine(directory, "store"));
                var written = store
                    .WriteAsync(
                        inputs.AssetId,
                        inputs.Version,
                        File.ReadAllBytes(sourcePath),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                File.WriteAllBytes(
                    Path.Combine(directory, "reference.png"),
                    File.ReadAllBytes(sourcePath));

                var after = HarnessCore.Sha256(File.ReadAllBytes(sourcePath));
                Equal(before, after);
                Equal(before, written.ContentHash);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        });

        Console.WriteLine($"Self-check: {passed}/{total} passed");
        return passed == total ? 0 : 1;
    }

    private static Func<string, string?> Lookup(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    private static void Throws<TException>(Action body, string expectedFragment)
        where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException exception)
        {
            if (exception.Message.Contains(expectedFragment, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Expected '{expectedFragment}' in '{exception.Message}'.");
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}' but was '{actual}'.");
        }
    }

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected condition to be true.");
        }
    }
}
