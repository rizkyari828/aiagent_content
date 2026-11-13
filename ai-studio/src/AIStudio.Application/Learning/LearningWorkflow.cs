using AIStudio.Domain.Learning;

namespace AIStudio.Application.Learning;

public interface ILearningRepository
{
    Task<IReadOnlyList<LearningGuideline>> ListAsync(string? status, int take, CancellationToken cancellationToken);

    Task<LearningGuideline?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(LearningGuideline guideline);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class LearningWorkflow(
    ILearningRepository repository,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<LearningGuidelineView>> ListAsync(
        string? status,
        int take,
        CancellationToken cancellationToken)
    {
        var items = await repository.ListAsync(status, Math.Clamp(take, 1, 50), cancellationToken);
        return items.Select(ToView).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetActiveGuidelineSummariesAsync(
        int maxCount,
        CancellationToken cancellationToken)
    {
        var items = await repository.ListAsync(
            LearningGuidelineStatuses.SupportedGuideline,
            Math.Clamp(maxCount, 1, 5),
            cancellationToken);

        return items.Select(g => $"{g.Title}: {g.Summary}").ToArray();
    }

    public async Task<LearningGuidelineView> CreateCandidateAsync(
        string kind,
        string title,
        string summary,
        string? sourceCorrectionReason,
        CancellationToken cancellationToken)
    {
        var entity = LearningGuideline.CreateCandidate(
            kind,
            title,
            summary,
            sourceCorrectionReason,
            timeProvider.GetUtcNow());

        repository.Add(entity);
        await repository.SaveChangesAsync(cancellationToken);
        return ToView(entity);
    }

    public async Task<LearningGuidelineView?> PromoteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await repository.FindByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.PromoteToSupported(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return ToView(entity);
    }

    public async Task<LearningGuidelineView?> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await repository.FindByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Retire(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return ToView(entity);
    }

    private static LearningGuidelineView ToView(LearningGuideline g) =>
        new(
            g.Id,
            g.Kind,
            g.Status,
            g.Title,
            g.Summary,
            g.SourceCorrectionReason,
            g.CreatedAt,
            g.UpdatedAt,
            g.PromotedAt);
}

public sealed record LearningGuidelineView(
    Guid Id,
    string Kind,
    string Status,
    string Title,
    string Summary,
    string? SourceCorrectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PromotedAt);
