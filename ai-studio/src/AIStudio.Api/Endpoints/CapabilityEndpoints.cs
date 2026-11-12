using AIStudio.Application.Capabilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Capability discovery endpoint. Exposes registered studio capabilities and their
/// configured providers without leaking filesystem paths, URLs, model paths, or secrets.
/// </summary>
public static class CapabilityEndpoints
{
    public static IEndpointRouteBuilder MapCapabilityApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/capabilities", GetCapabilities)
            .WithName("GetCapabilities")
            .Produces<CapabilitiesResponse>(StatusCodes.Status200OK);

        return endpoints;
    }

    internal static IResult GetCapabilities(ICapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var capabilities = registry.Capabilities
            .Select(capability => new CapabilityView(
                capability.Id.Value,
                capability.DisplayName,
                capability.Category,
                registry.GetProviders(capability.Id)
                    .Select(provider => new CapabilityProviderView(
                        provider.ProviderId,
                        provider.CapabilityId.Value,
                        provider.Priority,
                        provider.Enabled,
                        provider.RuntimeCategory))
                    .ToArray()))
            .ToArray();

        return Results.Ok(new CapabilitiesResponse(capabilities));
    }
}

public sealed record CapabilitiesResponse(IReadOnlyList<CapabilityView> Capabilities);

public sealed record CapabilityView(
    string Id,
    string DisplayName,
    string Category,
    IReadOnlyList<CapabilityProviderView> Providers);

public sealed record CapabilityProviderView(
    string ProviderId,
    string CapabilityId,
    int Priority,
    bool Enabled,
    string RuntimeCategory);
