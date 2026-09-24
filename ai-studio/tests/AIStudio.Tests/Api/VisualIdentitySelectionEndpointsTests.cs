using AIStudio.Api.Endpoints;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.IdentityAssets;
using AIStudio.Tests.Jobs;
using AIStudio.Tests.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Api;

/// <summary>
/// Endpoint-level tests for optional explicit visual identity selection. The
/// handler is invoked directly with a real GenerateSceneVisualsWorkflow over stub
/// infrastructure, so no HTTP server or database is required.
/// </summary>
public sealed class VisualIdentitySelectionEndpointsTests : IDisposable
{
    private const string ReferenceId = "student-01-reference";

    private static readonly AssetReferenceId AssetId = new(ReferenceId);

    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private readonly string root;

    public VisualIdentitySelectionEndpointsTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-visual-selection-tests",
            Guid.NewGuid().ToString("N"));
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
    public async Task NoSelection_RemainsValidWithoutIdentityReferences()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(projectId, storyboard, db, new IdentityAssetRegistry());

        Assert.IsType<Accepted<EnqueueJobResponse>>(result);
        Assert.NotNull(db.AddedJob);
        Assert.DoesNotContain("identityReferences", db.AddedJob!.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceQueryStillForwarded()
    {
        var (projectId, storyboard, db) = Context();

        await Enqueue(projectId, storyboard, db, new IdentityAssetRegistry(), force: true);

        Assert.True(GenerateSceneVisualsJobPayload.Deserialize(db.AddedJob!.Payload).Force);
    }

    [Fact]
    public async Task FloatingSelectionPinsLatestApprovedOnce()
    {
        var (projectId, storyboard, db) = Context();
        var registry = Registry(1, 2);
        registry.Approve(AssetId, new IdentityAssetVersion(1));

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            registry,
            request: Request(version: null));

        Assert.IsType<Accepted<EnqueueJobResponse>>(result);
        Assert.Equal(1, Assert.Single(Pins(db)).Version.Value);
    }

    [Fact]
    public async Task ExplicitVersionPinsExactApprovedVersion()
    {
        var (projectId, storyboard, db) = Context();
        var registry = Registry(1, 2);
        registry.Approve(AssetId, new IdentityAssetVersion(1));
        registry.Approve(AssetId, new IdentityAssetVersion(2));

        await Enqueue(projectId, storyboard, db, registry, request: Request(version: 1));

        Assert.Equal(1, Assert.Single(Pins(db)).Version.Value);
    }

    [Fact]
    public async Task ExplicitDraftVersionFailsWithoutCreatingJob()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            Registry(2),
            request: Request(version: 2));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task MissingVersionFailsWithoutCreatingJob()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            new IdentityAssetRegistry(),
            request: Request(version: 9));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task InvalidAssetIdRejected()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            new IdentityAssetRegistry(),
            request: new EnqueueVisualsRequest(new VisualIdentityReference("Not Valid!", null)));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task InvalidVersionRejected()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            new IdentityAssetRegistry(),
            request: Request(version: 0));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task ApprovingNextVersionDoesNotChangeExistingJob()
    {
        var (projectId, storyboard, db) = Context();
        var registry = Registry(1, 2);
        registry.Approve(AssetId, new IdentityAssetVersion(1));

        await Enqueue(projectId, storyboard, db, registry, request: Request(version: null));
        var firstPayload = db.AddedJob!.Payload;
        Assert.Equal(1, Assert.Single(Pins(db)).Version.Value);

        registry.Approve(AssetId, new IdentityAssetVersion(2));

        // The already-persisted job is immutable; a fresh enqueue resolves the new latest.
        Assert.Equal(firstPayload, db.AddedJob!.Payload);
        var (_, _, secondDb) = Context(projectId, storyboard);
        await Enqueue(projectId, storyboard, secondDb, registry, request: Request(version: null));
        Assert.Equal(2, Assert.Single(Pins(secondDb)).Version.Value);
    }

    [Fact]
    public void RequestShapeExposesAtMostOneAuthoringReference()
    {
        Assert.Equal(
            ["IdentityReference", "ProductionRecipe", "ArtDirection"],
            typeof(EnqueueVisualsRequest).GetProperties().Select(property => property.Name));

        var referenceProperty = typeof(EnqueueVisualsRequest).GetProperty("IdentityReference")!;
        Assert.Equal(
            typeof(VisualIdentityReference),
            Nullable.GetUnderlyingType(referenceProperty.PropertyType) ?? referenceProperty.PropertyType);
        Assert.Equal(
            ["AssetId", "Version"],
            typeof(VisualIdentityReference).GetProperties().Select(property => property.Name));

        var recipeProperty = typeof(EnqueueVisualsRequest).GetProperty("ProductionRecipe")!;
        Assert.Equal(
            typeof(VisualProductionRecipe),
            Nullable.GetUnderlyingType(recipeProperty.PropertyType) ?? recipeProperty.PropertyType);
        Assert.Equal(
            ["Id", "Version"],
            typeof(VisualProductionRecipe).GetProperties().Select(property => property.Name));

        foreach (var property in typeof(VisualIdentityReference).GetProperties())
        {
            Assert.NotEqual(typeof(PinnedIdentityAsset), property.PropertyType);
            Assert.DoesNotContain(
                "List",
                property.PropertyType.Name,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RealisticFlow_SelectionStaysPinnedAcrossAuthoringChanges()
    {
        var (projectId, storyboard, _) = Context();
        var store = new LocalIdentityAssetStore(
            new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })));
        var registry = new IdentityAssetRegistry(
            timeProvider: new FixedTimeProvider(AssetTestData.Now),
            persistence: new LocalIdentityAssetMetadataPersistence(
                Options.Create(new AssetStorageOptions { RootPath = root })));
        var import = new ImportIdentityAssetWorkflow(
            store,
            registry,
            new FixedTimeProvider(AssetTestData.Now));
        var approval = new ApproveIdentityAssetWorkflow(registry);

        var v1 = await import.ImportAsync(ImportRequest(ValidPng), TestContext.Current.CancellationToken);
        approval.Approve(AssetId, v1.Version);

        var firstDb = new RecordingDbContext();
        await Enqueue(projectId, storyboard, firstDb, registry, request: Request(version: null));
        var firstPayload = firstDb.AddedJob!.Payload;
        Assert.Equal(1, Assert.Single(Pins(firstDb)).Version.Value);

        // A newer Draft does not affect the existing job or a new floating resolution.
        await import.ImportAsync(ImportRequest(ReplacementPng()), TestContext.Current.CancellationToken);
        var secondDb = new RecordingDbContext();
        await Enqueue(projectId, storyboard, secondDb, registry, request: Request(version: null));
        Assert.Equal(1, Assert.Single(Pins(secondDb)).Version.Value);

        // Approving v2 only affects new enqueues.
        approval.Approve(AssetId, new IdentityAssetVersion(2));
        var thirdDb = new RecordingDbContext();
        await Enqueue(projectId, storyboard, thirdDb, registry, request: Request(version: null));
        Assert.Equal(2, Assert.Single(Pins(thirdDb)).Version.Value);
        Assert.Equal(firstPayload, firstDb.AddedJob!.Payload);
    }

    private static ImportIdentityAssetRequest ImportRequest(byte[] content) =>
        new(
            AssetId,
            ImportIdentityAssetWorkflow.SupportedKind,
            ImportIdentityAssetWorkflow.SupportedMediaType,
            content);

    private static byte[] ReplacementPng() => [.. ValidPng, 0];

    private static EnqueueVisualsRequest Request(int? version) =>
        new(new VisualIdentityReference(ReferenceId, version));

    private static IReadOnlyList<PinnedIdentityAsset> Pins(RecordingDbContext db) =>
        GenerateSceneVisualsJobPayload.Deserialize(db.AddedJob!.Payload).IdentityReferences!;

    private static IdentityAssetRegistry Registry(params int[] versions)
    {
        var assets = versions
            .Select(version => IdentityAssetTestSupport.Asset(id: ReferenceId, version: version))
            .ToArray();
        return new IdentityAssetRegistry(assets);
    }

    private static (Guid ProjectId, JobSnapshot Storyboard, RecordingDbContext Db) Context() =>
        Context(Guid.NewGuid(), null);

    private static (Guid ProjectId, JobSnapshot Storyboard, RecordingDbContext Db) Context(
        Guid projectId,
        JobSnapshot? storyboard) =>
        (
            projectId,
            storyboard ?? AssetTestData.StoryboardJob(
                projectId,
                GenerateStoryboardTestData.ValidResult),
            new RecordingDbContext()
        );

    private static async Task<IResult> Enqueue(
        Guid projectId,
        JobSnapshot storyboard,
        RecordingDbContext db,
        IdentityAssetRegistry registry,
        bool force = false,
        EnqueueVisualsRequest? request = null)
    {
        var workflow = new GenerateSceneVisualsWorkflow(
            db,
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            new IdentityAssetResolver(registry),
            new AssetStubTimeProvider(AssetTestData.Now));

        return await VisualEndpoints.EnqueueVisualsAsync(
            projectId.ToString(),
            storyboard.Id.ToString(),
            force,
            request,
            workflow,
            TestContext.Current.CancellationToken);
    }
}
