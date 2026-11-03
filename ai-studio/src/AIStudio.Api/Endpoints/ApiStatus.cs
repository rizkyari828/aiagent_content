using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// One place for the HTTP status vocabulary. Domain enums stay unchanged; the API
/// exposes stable lowercase tokens.
/// </summary>
internal static class ApiStatus
{
    public static string ToApiStatus(JobStatus status) =>
        status switch
        {
            JobStatus.Queued => "queued",
            JobStatus.Running => "running",
            JobStatus.Succeeded => "completed",
            JobStatus.Failed => "failed",
            JobStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    public static string ToApiStatus(ContentStatus status) =>
        status switch
        {
            ContentStatus.Draft => "draft",
            ContentStatus.Researching => "researching",
            ContentStatus.IdeaReview => "idea_review",
            ContentStatus.Scripting => "scripting",
            ContentStatus.ScriptReview => "script_review",
            ContentStatus.Producing => "producing",
            ContentStatus.FinalReview => "final_review",
            ContentStatus.ReadyToPublish => "ready_to_publish",
            ContentStatus.Published => "published",
            ContentStatus.Archived => "archived",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
