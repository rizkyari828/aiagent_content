using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Domain.Assets;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

public sealed class IdentityAssetSecurityTests
{
    private static readonly string[] ForbiddenFragments =
    [
        "Path", "StorageKey", "Url", "Uri", "Workflow", "Checkpoint", "Cuda", "Absolute"
    ];

    [Theory]
    [InlineData(typeof(IdentityAsset))]
    [InlineData(typeof(IdentityAssetProvenance))]
    [InlineData(typeof(IdentityAssetBlob))]
    public void ApplicationContractExposesNoPhysicalStorageOrProviderConfiguration(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            foreach (var fragment in ForbiddenFragments)
            {
                Assert.DoesNotContain(
                    fragment,
                    property.Name,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void IdentityAssetHasExactMetadataContract()
    {
        Assert.Equal(
            [
                "Id", "Version", "Kind", "MediaType", "ByteSize", "ContentHash",
                "Status", "ApprovedAt", "Provenance", "CreatedAt"
            ],
            typeof(IdentityAsset).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void ProvenanceIsMinimalAndIdentifierOnly()
    {
        Assert.Equal(
            [
                "SourceBibleId", "SourceBibleVersion", "CapabilityId", "ProviderId",
                "Seed", "PromptHash", "ParentAssetIds"
            ],
            typeof(IdentityAssetProvenance).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void ApplicationAssemblyDoesNotReferenceInfrastructure()
    {
        var references = typeof(IdentityAsset).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain("AIStudio.Infrastructure", references);
    }

    [Fact]
    public void ProviderRequestContractsRemainUnchanged()
    {
        Assert.Equal(
            ["Prompt", "Seed"],
            typeof(ImageGenerationRequest).GetProperties().Select(property => property.Name));
        Assert.Equal(
            ["Template", "Palette", "DurationSeconds", "Seed"],
            typeof(ThreeDRenderRequest).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void SceneAssetContractRemainsUnchanged()
    {
        Assert.Equal(
            [
                "Id", "ContentProjectId", "ContentProject", "SourceJobId", "SourceJob",
                "SceneIndex", "Type", "Path", "ByteSize", "ContentHash", "Origin",
                "Source", "Creator", "License", "RetrievedAt", "CreatedAt"
            ],
            typeof(SceneAsset).GetProperties().Select(property => property.Name));
    }
}
