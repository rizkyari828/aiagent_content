using AIStudio.Api.Endpoints;
using AIStudio.Application.Bibles;
using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Tests.Assets;
using AIStudio.Tests.IdentityAssets;
using AIStudio.Tests.Jobs;
using AIStudio.Tests.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Api;

/// <summary>
/// Endpoint-level tests for the optional per-visual-job production recipe
/// selection. The handler is invoked directly with a real
/// GenerateSceneVisualsWorkflow over stub infrastructure; no HTTP server or database
/// is required.
/// </summary>
public sealed class VisualProductionRecipeEndpointsTests
{
    [Fact]
    public async Task BodylessRequestCreatesJobWithoutRecipe()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(projectId, storyboard, db);

        Assert.IsType<Accepted<EnqueueJobResponse>>(result);
        Assert.NotNull(db.AddedJob);
        Assert.DoesNotContain("productionRecipe", db.AddedJob!.Payload, StringComparison.Ordinal);
        Assert.Null(GenerateSceneVisualsJobPayload.Deserialize(db.AddedJob.Payload).ProductionRecipe);
    }

    [Fact]
    public async Task RecipeOnlySelectionPersistsConcreteIdentity()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: Request("motion-comic", 1));

        Assert.IsType<Accepted<EnqueueJobResponse>>(result);
        var recipe = GenerateSceneVisualsJobPayload.Deserialize(db.AddedJob!.Payload).ProductionRecipe;
        Assert.NotNull(recipe);
        Assert.Equal("motion-comic", recipe!.Id.Value);
        Assert.Equal(1, recipe.Version.Value);
    }

    [Fact]
    public async Task RecipeAndIdentitySelectionPersistTogether()
    {
        var (projectId, storyboard, db) = Context();
        var registry = new IdentityAssetRegistry();
        var referenceId = new AssetReferenceId("student-01-reference");
        registry.Register(IdentityAssetTestSupport.Asset(id: "student-01-reference", version: 1));
        registry.Approve(referenceId, new IdentityAssetVersion(1));

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            registry,
            request: new EnqueueVisualsRequest(
                new VisualIdentityReference("student-01-reference", null),
                new VisualProductionRecipe("motion-comic", 1)));

        Assert.IsType<Accepted<EnqueueJobResponse>>(result);
        var payload = GenerateSceneVisualsJobPayload.Deserialize(db.AddedJob!.Payload);
        Assert.NotNull(payload.ProductionRecipe);
        Assert.Equal("motion-comic", payload.ProductionRecipe!.Id.Value);
        Assert.Equal(1, payload.ProductionRecipe.Version.Value);
        Assert.Equal(1, Assert.Single(payload.IdentityReferences!).Version.Value);
    }

    [Fact]
    public async Task UnknownRecipeFailsBeforeJobCreation()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: Request("does-not-exist", 1));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task UnknownRecipeVersionFailsBeforeJobCreation()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: Request("motion-comic", 9));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task InvalidRecipeIdShapeRejected()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: Request("Not Valid!", 1));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task MissingRecipeVersionRejected()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: Request("motion-comic", null));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task InvalidRecipeVersionRejected()
    {
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: Request("motion-comic", 0));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public async Task ArtDirectionSelectionPersistsAndRoundTrips()
    {
        const string artDirection =
            "original cinematic anime, soft cel shading, deep blue night tones with warm amber highlights";
        var (projectId, storyboard, db) = Context();

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: new EnqueueVisualsRequest(null, Request("motion-comic", 1).ProductionRecipe, artDirection));

        Assert.IsType<Accepted<EnqueueJobResponse>>(result);
        Assert.Contains("\"artDirection\"", db.AddedJob!.Payload, StringComparison.Ordinal);
        var payload = GenerateSceneVisualsJobPayload.Deserialize(db.AddedJob.Payload);
        Assert.Equal(artDirection, payload.ArtDirection);
    }

    [Fact]
    public async Task InvalidArtDirectionFailsBeforeJobCreation()
    {
        var (projectId, storyboard, db) = Context();
        var tooLong = new string('a', 401);

        var result = await Enqueue(
            projectId,
            storyboard,
            db,
            request: new EnqueueVisualsRequest(null, Request("motion-comic", 1).ProductionRecipe, tooLong));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result).StatusCode);
        Assert.Null(db.AddedJob);
    }

    [Fact]
    public void PayloadRoundTripPreservesRecipeIdentity()
    {
        var json = GenerateSceneVisualsTestData.Payload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            productionRecipe: new ProductionRecipeReference
            {
                Id = new ProductionRecipeId("motion-comic"),
                Version = new ProductionRecipeVersion(1)
            });

        var roundTripped = GenerateSceneVisualsJobPayload.Deserialize(json);

        Assert.NotNull(roundTripped.ProductionRecipe);
        Assert.Equal("motion-comic", roundTripped.ProductionRecipe!.Id.Value);
        Assert.Equal(1, roundTripped.ProductionRecipe.Version.Value);
    }

    [Fact]
    public void LegacyPayloadWithoutRecipeRemainsValid()
    {
        var json = $$"""
            {
              "contentProjectId": "{{Guid.NewGuid()}}",
              "storyboardJobId": "{{Guid.NewGuid()}}",
              "force": false
            }
            """;

        var payload = GenerateSceneVisualsJobPayload.Deserialize(json);

        Assert.Null(payload.ProductionRecipe);
        Assert.Empty(payload.IdentityReferences!);
    }

    private static VisualProductionRecipe Recipe(string id, int? version) => new(id, version);

    private static EnqueueVisualsRequest Request(string id, int? version) =>
        new(null, Recipe(id, version));

    private static (Guid ProjectId, JobSnapshot Storyboard, RecordingDbContext Db) Context()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        return (projectId, storyboard, new RecordingDbContext());
    }

    private static async Task<IResult> Enqueue(
        Guid projectId,
        JobSnapshot storyboard,
        RecordingDbContext db,
        IdentityAssetRegistry? registry = null,
        EnqueueVisualsRequest? request = null)
    {
        var workflow = new GenerateSceneVisualsWorkflow(
            db,
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            new IdentityAssetResolver(registry ?? new IdentityAssetRegistry()),
            new AssetStubTimeProvider(AssetTestData.Now));

        return await VisualEndpoints.EnqueueVisualsAsync(
            projectId.ToString(),
            storyboard.Id.ToString(),
            force: null,
            request,
            workflow,
            TestContext.Current.CancellationToken);
    }
}
