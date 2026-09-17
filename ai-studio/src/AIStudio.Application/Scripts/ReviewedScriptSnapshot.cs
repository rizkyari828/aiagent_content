using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Scripts;

namespace AIStudio.Application.Scripts;

public sealed record ReviewedScriptSnapshot(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceJobId,
    GenerateScriptResult Script,
    ScriptReviewStatus Status,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ApprovedAt);

public sealed record StartScriptReviewResult(
    ReviewedScriptSnapshot Script,
    bool Created);
