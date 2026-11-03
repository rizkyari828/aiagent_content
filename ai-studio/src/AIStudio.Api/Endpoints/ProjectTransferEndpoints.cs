using AIStudio.Application.Transfer;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Project transfer HTTP surface. Export produces one ZIP package under the approved
/// root; import accepts one bounded multipart package. There is no new authentication:
/// the existing loopback-only internal posture applies, and this surface is not claimed
/// safe for public exposure.
/// </summary>
public static class ProjectTransferEndpoints
{
    public static IEndpointRouteBuilder MapProjectTransferApi(
        this IEndpointRouteBuilder endpoints)
    {
        var exports = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/exports");

        exports.MapPost("", ExportAsync)
            .WithName("ExportContentProject")
            .Produces<ProjectExportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesValidationProblem();

        exports.MapGet("", ListAsync)
            .WithName("ListContentProjectExports")
            .Produces<ProjectExportResponse[]>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        var imports = endpoints.MapGroup("/api/projects/imports");
        imports.MapPost("", ImportAsync)
            .WithName("ImportContentProject")
            .DisableAntiforgery()
            .Produces<ProjectImportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return endpoints;
    }

    internal static async Task<IResult> ExportAsync(
        string contentProjectId,
        ProjectExportWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var package = await workflow.ExportAsync(projectId, cancellationToken);
            return Results.Created(
                $"/api/content-projects/{projectId}/exports",
                ProjectExportResponse.From(package));
        }
        catch (ProjectTransferException exception)
        {
            return TransferProblem(exception);
        }
    }

    internal static async Task<IResult> ListAsync(
        string contentProjectId,
        ProjectExportWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var packages = await workflow.ListAsync(projectId, cancellationToken);
            return Results.Ok(packages.Select(ProjectExportResponse.From).ToArray());
        }
        catch (ProjectTransferException exception)
        {
            return TransferProblem(exception);
        }
    }

    internal static async Task<IResult> ImportAsync(
        IFormFile? file,
        ProjectImportWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["file"] = ["One non-empty ZIP package is required."]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "import_package_invalid"
                });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await workflow.ImportAsync(stream, cancellationToken);
            return Results.Created(
                $"/api/content-projects/{result.ContentProjectId}",
                ProjectImportResponse.From(result));
        }
        catch (ProjectTransferException exception)
        {
            return TransferProblem(exception);
        }
    }

    private static IResult TransferProblem(ProjectTransferException exception) =>
        exception.ErrorCode switch
        {
            "export_project_not_found" => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Content project not found.",
                detail: exception.Message,
                extensions: Code(exception.ErrorCode)),
            "import_project_conflict" => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Project import conflict.",
                detail: exception.Message,
                extensions: Code(exception.ErrorCode)),
            "export_artifact_missing"
                or "export_path_invalid"
                or "export_integrity_failed"
                or "export_project_busy"
                or "export_package_too_large"
                or "import_path_conflict" => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Project transfer rejected.",
                    detail: exception.Message,
                    extensions: Code(exception.ErrorCode)),
            "export_file_too_large" or "import_too_large" => Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Project transfer too large.",
                detail: exception.Message,
                extensions: Code(exception.ErrorCode)),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Project transfer invalid.",
                detail: exception.Message,
                extensions: Code(exception.ErrorCode))
        };

    private static Dictionary<string, object?> Code(string code) =>
        new() { ["code"] = code };

    private static bool TryParseIdentifier(string value, out Guid identifier) =>
        Guid.TryParse(value, out identifier) && identifier != Guid.Empty;

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "export_invalid_request"
            });
}

public sealed record ProjectExportResponse(
    string FileName,
    long ByteSize,
    string? Sha256,
    DateTimeOffset CreatedAtUtc)
{
    public static ProjectExportResponse From(ProjectPackageInfo package) =>
        new(package.FileName, package.ByteSize, package.Sha256, package.CreatedAtUtc);
}

public sealed record ProjectImportResponse(
    Guid ContentProjectId,
    int Files,
    int ReviewedScripts,
    int SourceJobs,
    int SceneAssets,
    int NarrationTracks,
    int SubtitleTracks,
    int FinalApprovals,
    int PublicationRecords,
    int MetricSnapshots,
    int TimeEntries,
    int CostEntries)
{
    public static ProjectImportResponse From(ProjectImportResult result) =>
        new(
            result.ContentProjectId,
            result.Files,
            result.Counts.ReviewedScripts,
            result.Counts.SourceJobs,
            result.Counts.SceneAssets,
            result.Counts.NarrationTracks,
            result.Counts.SubtitleTracks,
            result.Counts.FinalApprovals,
            result.Counts.PublicationRecords,
            result.Counts.MetricSnapshots,
            result.Counts.TimeEntries,
            result.Counts.CostEntries);
}
