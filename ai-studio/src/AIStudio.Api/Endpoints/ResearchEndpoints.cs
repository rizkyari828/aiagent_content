using AIStudio.Application.Research;

namespace AIStudio.Api.Endpoints;

public static class ResearchEndpoints
{
    public static IEndpointRouteBuilder MapResearchApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/research/collect", CollectTrendsAsync)
            .WithName("CollectResearchTrends")
            .Produces<IReadOnlyList<TrendSignal>>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        api.MapPost("/research/synthesize", SynthesizeResearchAsync)
            .WithName("SynthesizeResearchPackage")
            .Produces<ResearchPackage>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        api.MapGet("/research/packages", ListPackagesAsync)
            .WithName("ListResearchPackages")
            .Produces<IReadOnlyList<ResearchPackageSummary>>(StatusCodes.Status200OK);

        api.MapGet("/research/packages/{id}", GetPackageAsync)
            .WithName("GetResearchPackage")
            .Produces<ResearchPackage>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapPost("/research/packages/{id}/candidates/{index:int}/promote", PromoteCandidateAsync)
            .WithName("PromoteResearchCandidate")
            .Produces<PromotedProjectResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapGet("/content-projects/{contentProjectId}/sources", GetProjectSourcesAsync)
            .WithName("GetContentProjectSources")
            .Produces<ProjectSourcesAndClaims>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapGet("/research/watchlist", GetWatchlistFeeds)
            .WithName("GetResearchWatchlist")
            .Produces<IReadOnlyList<WatchlistFeedDescriptor>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> CollectTrendsAsync(
        CollectTrendsRequest request,
        ResearchWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var signals = await workflow.CollectAsync(
                new TrendQuery(
                    request.Topic ?? string.Empty,
                    string.IsNullOrWhiteSpace(request.RegionCode) ? "ID" : request.RegionCode,
                    request.IncludeWatchlist,
                    request.OperatorSeedUrls,
                    request.MaxSignals ?? 15),
                cancellationToken);

            return Results.Ok(signals);
        }
        catch (ResearchException ex)
        {
            return ResearchProblem(ex);
        }
    }

    private static async Task<IResult> SynthesizeResearchAsync(
        SynthesizeResearchRequest request,
        ResearchWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var package = await workflow.SynthesizeAndSaveAsync(
                new SynthesizeResearchInput(
                    request.Topic ?? string.Empty,
                    request.RegionCode,
                    request.IncludeWatchlist,
                    request.OperatorSeedUrls,
                    request.Signals,
                    request.TargetAudience,
                    request.OfferContext,
                    request.Language,
                    request.Provider,
                    request.Model),
                cancellationToken);

            return Results.Ok(package);
        }
        catch (ResearchException ex)
        {
            return ResearchProblem(ex);
        }
    }

    private static async Task<IResult> ListPackagesAsync(
        int? take,
        ResearchWorkflow workflow,
        CancellationToken cancellationToken)
    {
        var items = await workflow.ListPackagesAsync(take ?? 20, cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetPackageAsync(
        string id,
        ResearchWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId) || parsedId == Guid.Empty)
        {
            return InvalidIdentifier("id");
        }

        var package = await workflow.GetPackageAsync(parsedId, cancellationToken);
        return package is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Research package not found.",
                detail: $"Research package '{parsedId}' does not exist.",
                extensions: new Dictionary<string, object?> { ["code"] = "research_package_not_found" })
            : Results.Ok(package);
    }

    private static async Task<IResult> PromoteCandidateAsync(
        string id,
        int index,
        ResearchWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId) || parsedId == Guid.Empty)
        {
            return InvalidIdentifier("id");
        }

        try
        {
            var result = await workflow.PromoteCandidateAsync(parsedId, index, cancellationToken);
            if (result is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Research package not found.",
                    detail: $"Research package '{parsedId}' does not exist.",
                    extensions: new Dictionary<string, object?> { ["code"] = "research_package_not_found" });
            }

            return Results.Created($"/api/content-projects/{result.ContentProjectId}", result);
        }
        catch (ResearchException ex)
        {
            return ResearchProblem(ex);
        }
    }

    private static async Task<IResult> GetProjectSourcesAsync(
        string contentProjectId,
        ResearchWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var parsedId) || parsedId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        var result = await workflow.GetProjectSourcesAsync(parsedId, cancellationToken);
        return result is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Content project not found.",
                detail: $"Content project '{parsedId}' does not exist.",
                extensions: new Dictionary<string, object?> { ["code"] = "content_project_not_found" })
            : Results.Ok(result);
    }

    private static IResult GetWatchlistFeeds(IResearchWatchlistProvider provider) =>
        Results.Ok(provider.GetFeeds());

    private static IResult ResearchProblem(ResearchException exception) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Research request invalid.",
            detail: exception.Message,
            extensions: new Dictionary<string, object?> { ["code"] = exception.ErrorCode });

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [fieldName] = ["A non-empty GUID is required."] },
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_identifier" });

    public sealed record CollectTrendsRequest(
        string? Topic,
        string? RegionCode = "ID",
        bool IncludeWatchlist = false,
        IReadOnlyList<string>? OperatorSeedUrls = null,
        int? MaxSignals = 15);

    public sealed record SynthesizeResearchRequest(
        string? Topic,
        string? RegionCode = "ID",
        bool IncludeWatchlist = false,
        IReadOnlyList<string>? OperatorSeedUrls = null,
        IReadOnlyList<TrendSignal>? Signals = null,
        string? TargetAudience = null,
        string? OfferContext = null,
        string? Language = "Indonesian",
        string? Provider = null,
        string? Model = null);
}
