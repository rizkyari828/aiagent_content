using System.Reflection;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure;
using Xunit;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Tests.Architecture;

/// <summary>
/// Cheap assembly-level guardrails for the extractable-module rules (ADR-018). They
/// use only <see cref="Assembly.GetReferencedAssemblies"/> and the existing test
/// framework — no architecture-testing package, no dependency-analysis framework.
/// They prevent the obvious violations: a lower layer referencing a higher layer and
/// the application/domain referencing infrastructure. Finer-grained cross-module
/// namespace rules remain a documented, deferred follow-up.
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void DomainDoesNotReferenceUpperLayers()
    {
        var referenced = ReferencedAssemblies(typeof(Job).Assembly);

        Assert.DoesNotContain("AIStudio.Application", referenced);
        Assert.DoesNotContain("AIStudio.Infrastructure", referenced);
        Assert.DoesNotContain("AIStudio.Api", referenced);
    }

    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureOrApi()
    {
        var referenced = ReferencedAssemblies(typeof(StoryContextModel).Assembly);

        Assert.DoesNotContain("AIStudio.Infrastructure", referenced);
        Assert.DoesNotContain("AIStudio.Api", referenced);
    }

    [Fact]
    public void InfrastructureDoesNotReferenceApi()
    {
        var referenced = ReferencedAssemblies(typeof(DependencyInjection).Assembly);

        Assert.DoesNotContain("AIStudio.Api", referenced);
    }

    private static string[] ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
}
