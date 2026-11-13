using AIStudio.Application.Learning;

namespace AIStudio.Api.Endpoints;

public static class LearningEndpoints
{
    public static IEndpointRouteBuilder MapLearningApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/learning/guidelines", ListGuidelinesAsync)
            .WithName("ListLearningGuidelines")
            .Produces<IReadOnlyList<LearningGuidelineView>>(StatusCodes.Status200OK);

        api.MapPost("/learning/guidelines", CreateCandidateAsync)
            .WithName("CreateLearningCandidate")
            .Produces<LearningGuidelineView>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        api.MapPost("/learning/guidelines/{id}/promote", PromoteGuidelineAsync)
            .WithName("PromoteLearningGuideline")
            .Produces<LearningGuidelineView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapPost("/learning/guidelines/{id}/retire", RetireGuidelineAsync)
            .WithName("RetireLearningGuideline")
            .Produces<LearningGuidelineView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> ListGuidelinesAsync(
        string? status,
        int? take,
        LearningWorkflow workflow,
        CancellationToken cancellationToken)
    {
        var items = await workflow.ListAsync(status, take ?? 30, cancellationToken);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateCandidateAsync(
        CreateLearningCandidateRequest request,
        LearningWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await workflow.CreateCandidateAsync(
                request.Kind ?? "ProductionLesson",
                request.Title ?? string.Empty,
                request.Summary ?? string.Empty,
                request.SourceCorrectionReason,
                cancellationToken);
            return Results.Created($"/api/learning/guidelines/{created.Id}", created);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> PromoteGuidelineAsync(
        string id,
        LearningWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId) || parsedId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["A non-empty GUID is required."] });
        }

        var updated = await workflow.PromoteAsync(parsedId, cancellationToken);
        return updated is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Learning guideline not found.")
            : Results.Ok(updated);
    }

    private static async Task<IResult> RetireGuidelineAsync(
        string id,
        LearningWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsedId) || parsedId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["A non-empty GUID is required."] });
        }

        var updated = await workflow.RetireAsync(parsedId, cancellationToken);
        return updated is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Learning guideline not found.")
            : Results.Ok(updated);
    }

    public sealed record CreateLearningCandidateRequest(
        string? Kind,
        string? Title,
        string? Summary,
        string? SourceCorrectionReason = null);
}
