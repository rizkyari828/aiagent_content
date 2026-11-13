using AIStudio.Application.AI;
using AIStudio.Application.Research;

namespace AIStudio.Infrastructure.Research;

public sealed class ResearchSynthesizer(
    IAiTextGenerator textGenerator,
    TimeProvider timeProvider) : IResearchSynthesizer
{
    public async Task<ResearchPackage> SynthesizeAsync(
        ResearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Topic))
        {
            throw new ResearchException("research_topic_required", "Research topic is required.");
        }

        var utcNow = timeProvider.GetUtcNow();
        var packageId = Guid.NewGuid();
        var region = string.IsNullOrWhiteSpace(request.RegionCode)
            ? "ID"
            : request.RegionCode.Trim().ToUpperInvariant();

        var trimmedSignals = ResearchTokenDiet.TrimAndDeduplicate(
            request.Signals ?? [],
            maxSignals: 12);

        var sources = trimmedSignals
            .Select(s => new ResearchSource(
                Guid.NewGuid(),
                null,
                s.Title,
                s.Url,
                s.SourceType,
                s.RetrievedAt,
                string.IsNullOrWhiteSpace(s.ContentHash)
                    ? ContentHashHelper.ComputeSnippetHash(s.Title, s.RawSummary)
                    : s.ContentHash))
            .ToArray();

        if (sources.Length == 0)
        {
            throw new ResearchException(
                "research_signals_empty",
                "At least one trend signal is required before synthesis.");
        }

        var prompt = ResearchPromptBuilder.Build(
            request,
            sources,
            trimmedSignals);

        try
        {
            var response = await textGenerator.GenerateAsync(
                new AiTextRequest(
                    prompt,
                    ResearchPromptBuilder.SystemInstruction,
                    Model: request.Model,
                    ResponseFormat: AiResponseFormat.JsonObject,
                    Temperature: 0.3,
                    MaxTokens: 2_500,
                    Provider: request.Provider),
                cancellationToken);

            var providerUsed = string.IsNullOrWhiteSpace(request.Provider)
                ? response.Model
                : $"{request.Provider.Trim()}:{response.Model}";

            return ResearchPackageParser.Parse(
                packageId,
                request.Topic.Trim(),
                region,
                utcNow,
                providerUsed,
                sources,
                response.Text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AiGenerationException ex)
        {
            return ResearchPackageParser.BuildDegradedFallback(
                packageId,
                request.Topic.Trim(),
                region,
                utcNow,
                sources,
                ex.ErrorCode.ToString());
        }
        catch (ResearchException ex)
        {
            return ResearchPackageParser.BuildDegradedFallback(
                packageId,
                request.Topic.Trim(),
                region,
                utcNow,
                sources,
                ex.ErrorCode);
        }
    }
}
