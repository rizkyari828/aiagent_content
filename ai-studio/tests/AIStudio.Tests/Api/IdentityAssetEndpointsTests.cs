using System.Reflection;
using System.Security.Cryptography;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.IdentityAssets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Api;

/// <summary>
/// Transport tests for the identity-asset authoring endpoints. Handlers are invoked
/// directly (no HTTP server); the strongly-typed Minimal API results carry status
/// and payload, so no external services are required.
/// </summary>
public sealed class IdentityAssetEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 23, 2, 0, 0, TimeSpan.Zero);

    private static readonly AssetReferenceId ReferenceId = new("student-01-reference");

    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly string ExpectedHash =
        Convert.ToHexString(SHA256.HashData(ValidPng)).ToLowerInvariant();

    private readonly string root;

    public IdentityAssetEndpointsTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-identity-endpoint-tests",
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
    public async Task Import_ValidMultipartPngCreatesDraft()
    {
        var registry = Registry();

        var response = AsOk(await ImportResult(
            "student-01-reference",
            FormFile(ValidPng),
            registry));

        Assert.Equal("student-01-reference", response.AssetId);
        Assert.Equal(1, response.Version);
        Assert.Equal("Draft", response.Status);
        Assert.Equal(ImportIdentityAssetWorkflow.SupportedKind, response.Kind);
        Assert.Equal(ImportIdentityAssetWorkflow.SupportedMediaType, response.MediaType);
        Assert.Equal(ValidPng.Length, response.ByteSize);
        Assert.Equal(ExpectedHash, response.ContentHash);
        Assert.Null(response.ApprovedAt);

        var stored = registry.Get(ReferenceId, new IdentityAssetVersion(1));
        Assert.Equal(IdentityAssetStatus.Draft, stored.Status);
    }

    [Fact]
    public async Task Import_ForwardsAssetIdExactly()
    {
        var registry = Registry();

        var response = AsOk(await ImportResult(
            "student-01-reference",
            FormFile(ValidPng),
            registry));

        Assert.Equal("student-01-reference", response.AssetId);
        Assert.True(registry.TryGet(
            new AssetReferenceId("student-01-reference"),
            new IdentityAssetVersion(1),
            out _));
    }

    [Fact]
    public async Task Import_MissingFileRejected()
    {
        var status = StatusOf(await ImportResult("student-01-reference", file: null, Registry()));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task Import_EmptyFileRejected()
    {
        var status = StatusOf(await ImportResult("student-01-reference", FormFile([]), Registry()));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task Import_OversizedFileRejected()
    {
        var oversized = new byte[IdentityAssetEndpoints.MaxUploadBytes + 1];

        var status = StatusOf(await ImportResult(
            "student-01-reference",
            FormFile(oversized),
            Registry()));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, status);
    }

    [Fact]
    public async Task Import_InvalidPngMapsSafely()
    {
        var registry = Registry();

        var problem = AsProblem(await ImportResult(
            "student-01-reference",
            FormFile([1, 2, 3]),
            registry));

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("identity_import_png_invalid", problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(registry.Assets);
    }

    [Fact]
    public async Task Import_InvalidAssetIdRejected()
    {
        var status = StatusOf(await ImportResult("Not Valid!", FormFile(ValidPng), Registry()));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public void Import_ResponseExposesNoPhysicalStorageOrProviderData()
    {
        foreach (var property in typeof(IdentityAssetResponse).GetProperties())
        {
            foreach (var fragment in new[] { "Path", "StorageKey", "Url", "Uri", "Workflow", "Checkpoint", "Absolute" })
            {
                Assert.DoesNotContain(fragment, property.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Approve_ConcreteVersionApproves()
    {
        var registry = Registry();
        AsOk(await ImportResult("student-01-reference", FormFile(ValidPng), registry));

        var response = AsOk(IdentityAssetEndpoints.Approve(
            "student-01-reference",
            1,
            new ApproveIdentityAssetWorkflow(registry)));

        Assert.Equal("Approved", response.Status);
        Assert.NotNull(response.ApprovedAt);
        Assert.Equal(1, response.Version);
    }

    [Fact]
    public void Approve_InvalidVersionRejected()
    {
        var status = StatusOf(IdentityAssetEndpoints.Approve(
            "student-01-reference",
            0,
            new ApproveIdentityAssetWorkflow(Registry())));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task Approve_MissingVersionMapsToNotFound()
    {
        var registry = Registry();
        AsOk(await ImportResult("student-01-reference", FormFile(ValidPng), registry));

        var problem = AsProblem(IdentityAssetEndpoints.Approve(
            "student-01-reference",
            9,
            new ApproveIdentityAssetWorkflow(registry)));

        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public void Approve_RequiresConcreteVersionAndHasNoLatestRoute()
    {
        Assert.Contains("{version}", IdentityAssetEndpoints.ApproveRoute, StringComparison.Ordinal);
        Assert.DoesNotContain("latest", IdentityAssetEndpoints.ApproveRoute, StringComparison.OrdinalIgnoreCase);

        var methods = typeof(IdentityAssetEndpoints)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(IdentityAssetEndpoints))
            .ToArray();

        Assert.DoesNotContain(
            methods,
            method => method.Name.Contains("Latest", StringComparison.OrdinalIgnoreCase));

        var approve = Assert.Single(methods, method => method.Name == "Approve");
        var version = Assert.Single(approve.GetParameters(), parameter => parameter.Name == "version");
        Assert.Equal(typeof(int), version.ParameterType);
    }

    [Fact]
    public void EndpointsDelegateToWorkflowsAndNeverTouchStoreOrRegistry()
    {
        var handlers = typeof(IdentityAssetEndpoints)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name is "ImportAsync" or "Approve")
            .ToArray();

        Assert.Equal(2, handlers.Length);

        var forbidden = new[]
        {
            typeof(IIdentityAssetStore),
            typeof(IIdentityAssetRegistry),
            typeof(LocalIdentityAssetStore),
            typeof(LocalIdentityAssetMetadataPersistence)
        };

        foreach (var parameter in handlers.SelectMany(handler => handler.GetParameters()))
        {
            Assert.DoesNotContain(parameter.ParameterType, forbidden);
        }

        Assert.DoesNotContain(
            typeof(IdentityAssetEndpoints)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            field => field.FieldType.Namespace?.StartsWith(
                "System.Security.Cryptography",
                StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task RealisticInProcess_ImportApproveSurvivesRegistryRecreation()
    {
        var store = Store();
        var registry = Registry(Persistence());
        AsOk(await ImportResult("student-01-reference", FormFile(ValidPng), registry, store));

        var approved = AsOk(IdentityAssetEndpoints.Approve(
            "student-01-reference",
            1,
            new ApproveIdentityAssetWorkflow(registry)));
        Assert.Equal("Approved", approved.Status);

        var reopened = Registry(Persistence());
        var stored = reopened.Get(ReferenceId, new IdentityAssetVersion(1));
        Assert.Equal(IdentityAssetStatus.Approved, stored.Status);
    }

    private async Task<IResult> ImportResult(
        string assetId,
        IFormFile? file,
        IdentityAssetRegistry registry,
        IIdentityAssetStore? store = null)
    {
        var workflow = new ImportIdentityAssetWorkflow(
            store ?? Store(),
            registry,
            new FixedTimeProvider(CreatedAt));

        return await IdentityAssetEndpoints.ImportAsync(
            assetId,
            file,
            workflow,
            TestContext.Current.CancellationToken);
    }

    private static IdentityAssetResponse AsOk(IResult result) =>
        Assert.IsType<Ok<IdentityAssetResponse>>(result).Value
        ?? throw new InvalidOperationException("Ok result did not carry an identity asset.");

    private static ProblemHttpResult AsProblem(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result);

    private static int StatusOf(IResult result) => result switch
    {
        ValidationProblem => StatusCodes.Status400BadRequest,
        ProblemHttpResult problem => problem.StatusCode,
        Ok<IdentityAssetResponse> => StatusCodes.Status200OK,
        _ => throw new InvalidOperationException($"Unexpected result type {result.GetType().Name}.")
    };

    private static FormFile FormFile(byte[] content, string fileName = "student-01-reference.png") =>
        new(new MemoryStream(content, writable: false), 0, content.Length, "file", fileName);

    private IdentityAssetRegistry Registry(IIdentityAssetMetadataPersistence? persistence = null) =>
        new(timeProvider: new FixedTimeProvider(CreatedAt), persistence: persistence);

    private LocalIdentityAssetMetadataPersistence Persistence() =>
        new(Options.Create(new AssetStorageOptions { RootPath = root }));

    private LocalIdentityAssetStore Store() =>
        new(new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })));
}
