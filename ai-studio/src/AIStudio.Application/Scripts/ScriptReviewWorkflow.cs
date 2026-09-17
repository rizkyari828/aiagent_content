using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Scripts;

public sealed class ScriptReviewWorkflow(
    IScriptReviewRepository scripts,
    IJobReader jobs,
    TimeProvider timeProvider)
{
    public async Task<StartScriptReviewResult> StartAsync(
        Guid contentProjectId,
        Guid sourceJobId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        RequireIdentifier(sourceJobId, nameof(sourceJobId));

        var existing = await scripts.FindByProjectIdAsync(
            contentProjectId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.SourceJobId != sourceJobId)
            {
                throw Error(
                    "script_review_conflict",
                    "A script review already exists for a different GenerateScript job.");
            }

            return new StartScriptReviewResult(ToSnapshot(existing), false);
        }

        var source = await jobs.FindByIdAsync(sourceJobId, cancellationToken);
        if (source is null)
        {
            throw Error(
                "script_review_source_not_found",
                $"GenerateScript job '{sourceJobId}' does not exist.");
        }

        if (source.ContentProjectId != contentProjectId
            || source.Type != JobType.GenerateScript
            || source.Status != JobStatus.Succeeded
            || source.Result is null)
        {
            throw Error(
                "script_review_invalid_source",
                "The source must be a completed GenerateScript job for this content project.");
        }

        string canonicalContent;
        try
        {
            canonicalContent = GenerateScriptResult
                .Deserialize(source.Result)
                .Serialize();
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "script_review_invalid_source",
                "The GenerateScript job result is not a valid structured script.",
                exception);
        }

        var script = ReviewedScript.Create(
            contentProjectId,
            sourceJobId,
            canonicalContent,
            timeProvider.GetUtcNow());
        scripts.Add(script);
        await scripts.SaveChangesAsync(cancellationToken);

        return new StartScriptReviewResult(ToSnapshot(script), true);
    }

    public async Task<ReviewedScriptSnapshot?> FindAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        var script = await scripts.FindByProjectIdAsync(contentProjectId, cancellationToken);
        return script is null ? null : ToSnapshot(script);
    }

    public async Task<ReviewedScriptSnapshot?> EditAsync(
        Guid contentProjectId,
        GenerateScriptResult scriptContent,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        var canonicalContent = SerializeHumanEdit(scriptContent);
        var script = await scripts.FindByProjectIdAsync(contentProjectId, cancellationToken);
        if (script is null)
        {
            return null;
        }

        try
        {
            if (script.Edit(canonicalContent, timeProvider.GetUtcNow()))
            {
                await scripts.SaveChangesAsync(cancellationToken);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw Error("script_review_invalid_state", exception.Message, exception);
        }

        return ToSnapshot(script);
    }

    public async Task<ReviewedScriptSnapshot?> ApproveAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        var script = await scripts.FindByProjectIdAsync(contentProjectId, cancellationToken);
        if (script is null)
        {
            return null;
        }

        if (script.Approve(timeProvider.GetUtcNow()))
        {
            await scripts.SaveChangesAsync(cancellationToken);
        }

        return ToSnapshot(script);
    }

    private static string SerializeHumanEdit(GenerateScriptResult? result)
    {
        if (result is null)
        {
            throw Error("script_review_invalid_content", "A structured script is required.");
        }

        try
        {
            return result.Serialize();
        }
        catch (JobExecutionException exception)
        {
            throw Error(
                "script_review_invalid_content",
                exception.Message,
                exception);
        }
    }

    private static ReviewedScriptSnapshot ToSnapshot(ReviewedScript script) =>
        new(
            script.Id,
            script.ContentProjectId,
            script.SourceJobId,
            GenerateScriptResult.Deserialize(script.Content),
            script.Status,
            script.Revision,
            script.CreatedAt,
            script.UpdatedAt,
            script.ApprovedAt);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static ScriptReviewException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
