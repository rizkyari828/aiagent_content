using System.Reflection;
using Xunit;

namespace AIStudio.Tests.StoryContext;

/// <summary>
/// Guards the trust boundary: story context is a declarative projection. It exposes
/// no path, URL, command, assembly, provider, workflow, or secret, and depends on no
/// infrastructure type. Nothing in the layer executes.
/// </summary>
public sealed class StoryContextSecurityTests
{
    private static readonly string[] ForbiddenPropertyFragments =
    [
        "path", "url", "uri", "command", "assembly", "executable",
        "provider", "workflow", "secret", "blob", "download", "process", "engine", "prompt"
    ];

    public static TheoryData<Type> ContextTypes =>
    [
        typeof(AIStudio.Application.StoryContext.StoryContext),
        typeof(AIStudio.Application.StoryContext.StoryConceptContext),
        typeof(AIStudio.Application.StoryContext.StoryNarrativeContext),
        typeof(AIStudio.Application.StoryContext.StoryBeatContext),
        typeof(AIStudio.Application.StoryContext.StoryStateContext),
        typeof(AIStudio.Application.StoryContext.StoryCharacterContext),
        typeof(AIStudio.Application.StoryContext.StoryRelationshipContext),
        typeof(AIStudio.Application.StoryContext.StoryAssetContext),
        typeof(AIStudio.Application.StoryContext.StoryWorldContext)
    ];

    [Theory]
    [MemberData(nameof(ContextTypes))]
    public void ContextTypesExposeNoExecutableFields(Type type)
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
    [MemberData(nameof(ContextTypes))]
    public void ContextTypesDependOnNoInfrastructureTypes(Type type)
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
    public void ContextBuilderExposesOnlyProjection()
    {
        var methods = typeof(AIStudio.Application.StoryContext.IStoryContextBuilder)
            .GetMethods()
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(["Build"], methods);
    }
}
