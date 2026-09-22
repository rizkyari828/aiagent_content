using System.Reflection;
using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

/// <summary>
/// Guards the trust boundary: bible data is declarative narrative identity only. It
/// cannot name code, providers, paths, commands, or workflows, and asset references
/// stay ids. Nothing here can activate or execute anything.
/// </summary>
public sealed class BibleSecurityTests
{
    private static readonly string[] ForbiddenPropertyFragments =
    [
        "path", "url", "uri", "command", "assembly", "executable",
        "provider", "workflow", "secret", "blob", "download", "process", "engine"
    ];

    public static TheoryData<Type> BibleTypes =>
    [
        typeof(CharacterBible),
        typeof(CharacterIdentity),
        typeof(CharacterVariant),
        typeof(CharacterRelationship),
        typeof(CharacterState),
        typeof(WorldBible),
        typeof(WorldIdentity),
        typeof(WorldLocation),
        typeof(WorldState),
        typeof(AssetReference)
    ];

    [Theory]
    [MemberData(nameof(BibleTypes))]
    public void BibleTypesExposeNoExecutableFields(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.Name.ToLowerInvariant();

            foreach (var fragment in ForbiddenPropertyFragments)
            {
                Assert.DoesNotContain(fragment, name);
            }
        }
    }

    [Theory]
    [MemberData(nameof(BibleTypes))]
    public void BibleTypesDependOnNoInfrastructureTypes(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var assemblyName = propertyType.Assembly.GetName().Name ?? string.Empty;

            Assert.DoesNotContain("Infrastructure", assemblyName);
            Assert.DoesNotContain("Api", assemblyName);
            Assert.DoesNotContain("Domain", assemblyName);
        }
    }

    [Fact]
    public void AssetReferenceCarriesIdentityOnly()
    {
        var properties = typeof(AssetReference)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.Equal(["AssetId", "Version", "Purpose", "Variant"], properties);

    }

    [Fact]
    public void ParserRejectsUnknownExecutableLookingMembers()
    {
        const string json = """
            {
              "id": "rio",
              "version": 1,
              "displayName": "Rio",
              "identity": { "role": "protagonist" },
              "providerImplementation": "AIStudio.Evil.Type"
            }
            """;

        var exception = Assert.Throws<BibleException>(() => BibleParser.ParseCharacter(json));

        Assert.Equal(BibleParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void BibleRegistriesAreDataOnlyAndExposeNoExecution()
    {
        var registryMethods = typeof(ICharacterBibleRegistry)
            .GetMethods()
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain("Execute", registryMethods);
        Assert.DoesNotContain("Load", registryMethods);
        Assert.DoesNotContain("Generate", registryMethods);
        Assert.DoesNotContain("Render", registryMethods);
    }
}
