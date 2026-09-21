namespace AIStudio.Application.Creative;

/// <summary>
/// Plans HOW an approved idea should be expressed: it selects a creative
/// treatment and a production-ready concept. It consumes an already-approved idea
/// and never performs idea generation, trend discovery, ranking, or profitability
/// scoring, and it never registers a concept or starts production.
/// </summary>
public interface ICreativeDirector
{
    Task<CreativeDirectionResult> DirectAsync(
        ApprovedIdea idea,
        CreativeDirectionOptions? options,
        CancellationToken cancellationToken);
}
