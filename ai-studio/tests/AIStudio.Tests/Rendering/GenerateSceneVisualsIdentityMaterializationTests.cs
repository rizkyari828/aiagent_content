using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.IdentityAssets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Materialization boundary proof: a floating Bible reference is resolved exactly
/// once, before persistence, and the durable payload then only ever carries a
/// concrete pin. Execution/retry never re-resolves.
/// </summary>
public sealed class GenerateSceneVisualsIdentityMaterializationTests
{
    private const string ReferenceId = "student-01-reference";

    [Fact]
    public async Task Enqueue_ResolvesFloatingReferenceToLatestApprovedOnce()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        await Enqueue(context, registry, Reference(version: null));

        var pin = Assert.Single(Pins(context));
        Assert.Equal(ReferenceId, pin.AssetId.Value);
        Assert.Equal(1, pin.Version.Value);
    }

    [Fact]
    public async Task Enqueue_ExplicitVersionResolvesExactApprovedVersion()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1), Create(2));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(2));

        await Enqueue(context, registry, Reference(version: 1));

        Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);
    }

    [Fact]
    public async Task Enqueue_ExplicitDraftVersionFails()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));

        var exception = await Assert.ThrowsAsync<SceneVisualGenerationException>(
            () => Enqueue(context, registry, Reference(version: 1)));

        Assert.Equal("visual_identity_reference_unresolved", exception.ErrorCode);
        Assert.Null(context.AddedJob);
    }

    [Fact]
    public async Task Enqueue_ExplicitMissingVersionFails()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        var exception = await Assert.ThrowsAsync<SceneVisualGenerationException>(
            () => Enqueue(context, registry, Reference(version: 9)));

        Assert.Equal("visual_identity_reference_unresolved", exception.ErrorCode);
        Assert.Null(context.AddedJob);
    }

    [Fact]
    public async Task Enqueue_FloatingReferenceWithoutApprovedVersionFails()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));

        var exception = await Assert.ThrowsAsync<SceneVisualGenerationException>(
            () => Enqueue(context, registry, Reference(version: null)));

        Assert.Equal("visual_identity_reference_unresolved", exception.ErrorCode);
        Assert.Null(context.AddedJob);
    }

    [Fact]
    public async Task Enqueue_NewerDraftDoesNotReplaceOlderApprovedDuringMaterialization()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1), Create(2));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        await Enqueue(context, registry, Reference(version: null));

        Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);
    }

    [Fact]
    public async Task Enqueue_MoreThanOneReferenceFailsClearly()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        var exception = await Assert.ThrowsAsync<SceneVisualGenerationException>(
            () => Enqueue(
                context,
                registry,
                Reference(version: null),
                Reference(version: null, id: "teacher-02-reference")));

        Assert.Equal("visual_identity_reference_count_unsupported", exception.ErrorCode);
        Assert.Null(context.AddedJob);
    }

    [Fact]
    public async Task Enqueue_ZeroReferencesLeavesPayloadUnchanged()
    {
        var context = new RecordingDbContext();
        var registry = Registry();

        await Enqueue(context, registry);

        Assert.DoesNotContain("identityReferences", context.AddedJob!.Payload, StringComparison.Ordinal);
        Assert.Empty(Pins(context));
    }

    [Fact]
    public async Task MaterializedPayloadCarriesConcreteVersionOnly()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        await Enqueue(context, registry, Reference(version: null));

        var json = context.AddedJob!.Payload;
        Assert.Contains("\"identityReferences\"", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(Pins(context), pin => Assert.True(pin.Version.IsValid));
    }

    [Fact]
    public async Task MaterializedPayloadRoundTripPreservesVersion()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        await Enqueue(context, registry, Reference(version: null));

        var roundTripped = GenerateSceneVisualsJobPayload.Deserialize(context.AddedJob!.Payload);
        var pin = Assert.Single(roundTripped.IdentityReferences!);
        Assert.Equal(ReferenceId, pin.AssetId.Value);
        Assert.Equal(1, pin.Version.Value);
    }

    [Fact]
    public async Task ApprovingNextVersionAfterwardsDoesNotChangeExistingPayload()
    {
        var context = new RecordingDbContext();
        var registry = Registry(Create(1), Create(2));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        await Enqueue(context, registry, Reference(version: null));
        var persisted = context.AddedJob!.Payload;
        Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);

        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(2));

        Assert.Equal(persisted, context.AddedJob!.Payload);
        Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);
    }

    [Fact]
    public async Task Enqueue_RealStoryFixturePinsApprovedV1EvenAfterV2Approval()
    {
        // student-01 + AssetReference(student-01-reference, version null);
        // registry has v1 Approved and v2 Draft.
        var context = new RecordingDbContext();
        var registry = Registry(Create(1), Create(2));
        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

        await Enqueue(context, registry, Reference(version: null));
        Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);

        registry.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(2));
        Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);
    }

    [Fact]
    public async Task SelectedReferenceStaysPinnedAfterRegistryRestart()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-identity-materialization-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = RegistryAt(root);
            first.Register(Create(1));
            first.Register(Create(2));
            first.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(1));

            // Process-equivalent restart: reopen the same durable metadata root.
            var reopened = RegistryAt(root);
            var context = new RecordingDbContext();

            await Enqueue(context, reopened, Reference(version: null));
            Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);

            reopened.Approve(new AssetReferenceId(ReferenceId), new IdentityAssetVersion(2));
            Assert.Equal(1, Assert.Single(Pins(context)).Version.Value);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PayloadExposesNoInfrastructureOrAuthoringMetadata()
    {
        var types = typeof(GenerateSceneVisualsJobPayload)
            .GetProperties()
            .Select(property => property.PropertyType)
            .Concat(typeof(PinnedIdentityAsset)
                .GetProperties()
                .Select(property => property.PropertyType));

        foreach (var type in types)
        {
            Assert.DoesNotContain(
                "AIStudio.Infrastructure",
                type.FullName ?? string.Empty,
                StringComparison.Ordinal);
        }

        var properties = typeof(PinnedIdentityAsset).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain("Path", properties);
        Assert.DoesNotContain("StorageKey", properties);
        Assert.DoesNotContain("Kind", properties);
        Assert.DoesNotContain("MediaType", properties);
        Assert.DoesNotContain("Purpose", properties);
    }

    private static IdentityAsset Create(int version) =>
        IdentityAssetTestSupport.Asset(id: ReferenceId, version: version);

    private static AssetReference Reference(int? version, string id = ReferenceId) =>
        IdentityAssetTestSupport.Reference(version: version, id: id);

    private static IdentityAssetRegistry Registry(params IdentityAsset[] assets) => new(assets);

    private static IdentityAssetRegistry RegistryAt(string root) =>
        new(
            timeProvider: new FixedTimeProvider(AssetTestData.Now),
            persistence: new LocalIdentityAssetMetadataPersistence(
                Options.Create(new AssetStorageOptions { RootPath = root })));

    private static IReadOnlyList<PinnedIdentityAsset> Pins(RecordingDbContext context) =>
        GenerateSceneVisualsJobPayload.Deserialize(context.AddedJob!.Payload).IdentityReferences!;

    private static async Task Enqueue(
        RecordingDbContext context,
        IdentityAssetRegistry registry,
        params AssetReference[] references)
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var workflow = new GenerateSceneVisualsWorkflow(
            context,
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            new IdentityAssetResolver(registry),
            new AssetStubTimeProvider(AssetTestData.Now));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboard.Id,
            force: false,
            identityReferences: references.Length == 0 ? null : references,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
    }
}
