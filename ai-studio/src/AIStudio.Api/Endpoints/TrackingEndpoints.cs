using AIStudio.Application.Tracking;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Manually supplied metrics, time, and cost tracking HTTP surface. Every value is
/// recorded by the operator; nothing here calls YouTube Analytics or any external API,
/// and no route returns a summed metric snapshot.
/// </summary>
public static class TrackingEndpoints
{
    internal const string MetricInvalidRequestCode = "metric_snapshot_invalid_request";
    internal const string TimeEntryInvalidRequestCode = "time_entry_invalid_request";
    internal const string CostEntryInvalidRequestCode = "cost_entry_invalid_request";

    public static IEndpointRouteBuilder MapTrackingApi(this IEndpointRouteBuilder endpoints)
    {
        var metrics = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/metrics");

        metrics.MapPost("", CreateMetricSnapshotAsync)
            .WithName("CreateMetricSnapshot")
            .Produces<MetricSnapshotResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        metrics.MapGet("", ListMetricSnapshotsAsync)
            .WithName("ListMetricSnapshots")
            .Produces<MetricSnapshotResponse[]>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        var timeEntries = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/time-entries");

        timeEntries.MapPost("", CreateTimeEntryAsync)
            .WithName("CreateTimeEntry")
            .Produces<TimeEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        timeEntries.MapGet("", ListTimeEntriesAsync)
            .WithName("ListTimeEntries")
            .Produces<TimeEntryResponse[]>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        var costEntries = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/cost-entries");

        costEntries.MapPost("", CreateCostEntryAsync)
            .WithName("CreateCostEntry")
            .Produces<CostEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        costEntries.MapGet("", ListCostEntriesAsync)
            .WithName("ListCostEntries")
            .Produces<CostEntryResponse[]>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        endpoints.MapGet(
                "/api/content-projects/{contentProjectId}/tracking-summary",
                GetTrackingSummaryAsync)
            .WithName("GetTrackingSummary")
            .Produces<TrackingSummaryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    internal static async Task<IResult> CreateMetricSnapshotAsync(
        string contentProjectId,
        CreateMetricSnapshotRequest request,
        MetricSnapshotWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", MetricInvalidRequestCode);
        }

        try
        {
            var snapshot = await workflow.CreateAsync(
                projectId,
                request.Views,
                request.Impressions,
                request.ClickThroughRate,
                request.AverageViewDurationSeconds,
                request.WatchTimeMinutes,
                request.SubscribersGained,
                request.CapturedAt,
                request.Source,
                request.Notes,
                cancellationToken);

            return snapshot is null
                ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
                : Results.Created(
                    $"/api/content-projects/{projectId}/metrics",
                    MetricSnapshotResponse.From(snapshot));
        }
        catch (TrackingException exception)
        {
            return TrackingProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception, MetricInvalidRequestCode);
        }
    }

    internal static async Task<IResult> ListMetricSnapshotsAsync(
        string contentProjectId,
        int? take,
        MetricSnapshotWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", MetricInvalidRequestCode);
        }

        try
        {
            var snapshots = await workflow.ListAsync(
                projectId,
                take ?? TrackingTake.Default,
                cancellationToken);

            return snapshots is null
                ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
                : Results.Ok(snapshots.Select(MetricSnapshotResponse.From).ToArray());
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return InvalidRequest(exception, MetricInvalidRequestCode);
        }
    }

    internal static async Task<IResult> CreateTimeEntryAsync(
        string contentProjectId,
        CreateTimeEntryRequest request,
        TimeEntryWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", TimeEntryInvalidRequestCode);
        }

        try
        {
            var entry = await workflow.CreateAsync(
                projectId,
                request.Kind,
                request.Category,
                request.Minutes,
                request.RecordedAt,
                request.Notes,
                cancellationToken);

            return entry is null
                ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
                : Results.Created(
                    $"/api/content-projects/{projectId}/time-entries",
                    TimeEntryResponse.From(entry));
        }
        catch (TrackingException exception)
        {
            return TrackingProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception, TimeEntryInvalidRequestCode);
        }
    }

    internal static async Task<IResult> ListTimeEntriesAsync(
        string contentProjectId,
        int? take,
        TimeEntryWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", TimeEntryInvalidRequestCode);
        }

        try
        {
            var entries = await workflow.ListAsync(
                projectId,
                take ?? TrackingTake.Default,
                cancellationToken);

            return entries is null
                ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
                : Results.Ok(entries.Select(TimeEntryResponse.From).ToArray());
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return InvalidRequest(exception, TimeEntryInvalidRequestCode);
        }
    }

    internal static async Task<IResult> CreateCostEntryAsync(
        string contentProjectId,
        CreateCostEntryRequest request,
        CostEntryWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", CostEntryInvalidRequestCode);
        }

        try
        {
            var entry = await workflow.CreateAsync(
                projectId,
                request.Amount,
                request.Currency,
                request.Kind,
                request.Category,
                request.IncurredAt,
                request.Notes,
                cancellationToken);

            return entry is null
                ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
                : Results.Created(
                    $"/api/content-projects/{projectId}/cost-entries",
                    CostEntryResponse.From(entry));
        }
        catch (TrackingException exception)
        {
            return TrackingProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception, CostEntryInvalidRequestCode);
        }
    }

    internal static async Task<IResult> ListCostEntriesAsync(
        string contentProjectId,
        int? take,
        CostEntryWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", CostEntryInvalidRequestCode);
        }

        try
        {
            var entries = await workflow.ListAsync(
                projectId,
                take ?? TrackingTake.Default,
                cancellationToken);

            return entries is null
                ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
                : Results.Ok(entries.Select(CostEntryResponse.From).ToArray());
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return InvalidRequest(exception, CostEntryInvalidRequestCode);
        }
    }

    internal static async Task<IResult> GetTrackingSummaryAsync(
        string contentProjectId,
        TrackingSummaryWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId", MetricInvalidRequestCode);
        }

        var summary = await workflow.GetAsync(projectId, cancellationToken);
        return summary is null
            ? ContentProjectEndpoints.ContentProjectNotFound(projectId)
            : Results.Ok(TrackingSummaryResponse.From(summary));
    }

    private static IResult TrackingProblem(TrackingException exception) =>
        exception.ErrorCode == "metric_snapshot_already_recorded"
            ? CodedProblem(
                StatusCodes.Status409Conflict,
                "Metric snapshot rejected.",
                exception.Message,
                exception.ErrorCode)
            : CodedProblem(
                StatusCodes.Status400BadRequest,
                "Tracking request rejected.",
                exception.Message,
                exception.ErrorCode);

    private static IResult CodedProblem(
        int statusCode,
        string title,
        string detail,
        string code) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });

    private static IResult InvalidIdentifier(string fieldName, string code) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });

    private static IResult InvalidRequest(ArgumentException exception, string code) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;
}

/// <summary>
/// Manually recorded observation. Every metric member is nullable: <c>null</c> means "not
/// known" and <c>0</c> means a measured zero, so an omitted field is never treated as
/// zero.
/// </summary>
public sealed record CreateMetricSnapshotRequest(
    int? Views,
    int? Impressions,
    decimal? ClickThroughRate,
    decimal? AverageViewDurationSeconds,
    decimal? WatchTimeMinutes,
    int? SubscribersGained,
    DateTimeOffset CapturedAt,
    string? Source,
    string? Notes);

public sealed record CreateTimeEntryRequest(
    string? Kind,
    string? Category,
    decimal Minutes,
    DateTimeOffset RecordedAt,
    string? Notes);

public sealed record CreateCostEntryRequest(
    decimal Amount,
    string? Currency,
    string? Kind,
    string? Category,
    DateTimeOffset IncurredAt,
    string? Notes);

public sealed record MetricSnapshotResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid? PublicationRecordId,
    int? Views,
    int? Impressions,
    decimal? ClickThroughRate,
    decimal? AverageViewDurationSeconds,
    decimal? WatchTimeMinutes,
    int? SubscribersGained,
    DateTimeOffset CapturedAt,
    string Source,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static MetricSnapshotResponse From(MetricSnapshotView snapshot) =>
        new(
            snapshot.Id,
            snapshot.ContentProjectId,
            snapshot.PublicationRecordId,
            snapshot.Views,
            snapshot.Impressions,
            snapshot.ClickThroughRate,
            snapshot.AverageViewDurationSeconds,
            snapshot.WatchTimeMinutes,
            snapshot.SubscribersGained,
            snapshot.CapturedAt,
            snapshot.Source,
            snapshot.Notes,
            snapshot.CreatedAt);
}

public sealed record TimeEntryResponse(
    Guid Id,
    Guid ContentProjectId,
    string Kind,
    string Category,
    decimal Minutes,
    DateTimeOffset RecordedAt,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static TimeEntryResponse From(TimeEntryView entry) =>
        new(
            entry.Id,
            entry.ContentProjectId,
            entry.Kind,
            entry.Category,
            entry.Minutes,
            entry.RecordedAt,
            entry.Notes,
            entry.CreatedAt);
}

public sealed record CostEntryResponse(
    Guid Id,
    Guid ContentProjectId,
    decimal Amount,
    string Currency,
    string Kind,
    string Category,
    DateTimeOffset IncurredAt,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static CostEntryResponse From(CostEntryView entry) =>
        new(
            entry.Id,
            entry.ContentProjectId,
            entry.Amount,
            entry.Currency,
            entry.Kind,
            entry.Category,
            entry.IncurredAt,
            entry.Notes,
            entry.CreatedAt);
}

public sealed record TrackingSummaryResponse(
    Guid ContentProjectId,
    MetricSnapshotResponse? LatestMetricSnapshot,
    decimal HumanMinutesTotal,
    decimal MachineMinutesTotal,
    CostTotalsResponse[] Costs)
{
    public static TrackingSummaryResponse From(TrackingSummary summary) =>
        new(
            summary.ContentProjectId,
            summary.LatestMetricSnapshot is null
                ? null
                : MetricSnapshotResponse.From(summary.LatestMetricSnapshot),
            summary.HumanMinutesTotal,
            summary.MachineMinutesTotal,
            summary.Costs.Select(CostTotalsResponse.From).ToArray());
}

public sealed record CostTotalsResponse(
    string Currency,
    decimal ActualTotal,
    decimal EstimatedTotal,
    decimal RefundedTotal,
    decimal NetActualTotal,
    bool RefundExceedsActual)
{
    public static CostTotalsResponse From(CostTotals totals) =>
        new(
            totals.Currency,
            totals.ActualTotal,
            totals.EstimatedTotal,
            totals.RefundedTotal,
            totals.NetActualTotal,
            totals.RefundExceedsActual);
}
