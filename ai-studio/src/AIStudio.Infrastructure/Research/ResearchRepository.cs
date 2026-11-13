using System.Text.Json;
using AIStudio.Application.Research;
using AIStudio.Domain.Content;
using AIStudio.Domain.Research;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AIStudio.Infrastructure.Research;

internal sealed class ResearchRepository(ApplicationDbContext dbContext) : IResearchRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SavePackageAsync(ResearchPackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var record = ResearchPackageRecord.Create(
            package.Id,
            package.QueryTopic,
            package.RegionCode,
            package.ProviderUsed,
            JsonSerializer.Serialize(package.Candidates, JsonOptions),
            package.PacingBlueprint is null ? null : JsonSerializer.Serialize(package.PacingBlueprint, JsonOptions),
            package.Warnings is { Count: > 0 } ? JsonSerializer.Serialize(package.Warnings, JsonOptions) : null,
            package.DegradedFallback,
            package.CreatedAt);

        dbContext.ResearchPackages.Add(record);

        foreach (var s in package.Sources)
        {
            dbContext.ResearchSources.Add(ResearchSourceRecord.Create(
                s.Id,
                package.Id,
                s.ContentProjectId,
                s.Title,
                s.Url,
                s.SourceType,
                s.SnapshotHash,
                s.RetrievedAt));
        }

        foreach (var c in package.Claims)
        {
            dbContext.EvidenceClaims.Add(EvidenceClaimRecord.Create(
                c.Id,
                package.Id,
                c.SourceId,
                null,
                c.ClaimText,
                c.ClaimKind,
                c.ConfidenceNote,
                c.VisualProofSuggestion));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ResearchPackage?> FindPackageByIdAsync(Guid packageId, CancellationToken cancellationToken)
    {
        var record = await dbContext.ResearchPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken);

        if (record is null)
        {
            return null;
        }

        var sources = await dbContext.ResearchSources
            .AsNoTracking()
            .Where(s => s.ResearchPackageId == packageId)
            .OrderBy(s => s.RetrievedAt)
            .Select(s => new ResearchSource(
                s.Id,
                s.ContentProjectId,
                s.Title,
                s.Url,
                s.SourceType,
                s.RetrievedAt,
                s.SnapshotHash))
            .ToArrayAsync(cancellationToken);

        var claims = await dbContext.EvidenceClaims
            .AsNoTracking()
            .Where(c => c.ResearchPackageId == packageId)
            .Select(c => new EvidenceClaim(
                c.Id,
                c.ResearchPackageId,
                c.SourceId,
                c.ClaimText,
                c.ClaimKind,
                c.ConfidenceNote,
                c.VisualProofSuggestion))
            .ToArrayAsync(cancellationToken);

        var candidates = JsonSerializer.Deserialize<IdeaCandidate[]>(record.CandidatesJson, JsonOptions) ?? [];
        var pacing = string.IsNullOrWhiteSpace(record.PacingBlueprintJson)
            ? null
            : JsonSerializer.Deserialize<PacingBlueprint>(record.PacingBlueprintJson, JsonOptions);
        var warnings = string.IsNullOrWhiteSpace(record.WarningsJson)
            ? null
            : JsonSerializer.Deserialize<FactPolicyWarning[]>(record.WarningsJson, JsonOptions);

        return new ResearchPackage(
            record.Id,
            record.QueryTopic,
            record.RegionCode,
            record.CreatedAt,
            record.ProviderUsed,
            sources,
            claims,
            candidates,
            pacing,
            warnings,
            record.DegradedFallback);
    }

    public async Task<IReadOnlyList<ResearchPackageSummary>> ListPackagesAsync(
        int take,
        CancellationToken cancellationToken)
    {
        var packages = await dbContext.ResearchPackages
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var packageIds = packages.Select(p => p.Id).ToArray();
        var sourceCounts = await dbContext.ResearchSources
            .AsNoTracking()
            .Where(s => packageIds.Contains(s.ResearchPackageId))
            .GroupBy(s => s.ResearchPackageId)
            .Select(g => new { PackageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PackageId, x => x.Count, cancellationToken);

        var claimCounts = await dbContext.EvidenceClaims
            .AsNoTracking()
            .Where(c => packageIds.Contains(c.ResearchPackageId))
            .GroupBy(c => c.ResearchPackageId)
            .Select(g => new { PackageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PackageId, x => x.Count, cancellationToken);

        return packages.Select(p =>
        {
            var candidates = JsonSerializer.Deserialize<IdeaCandidate[]>(p.CandidatesJson, JsonOptions) ?? [];
            return new ResearchPackageSummary(
                p.Id,
                p.QueryTopic,
                p.RegionCode,
                p.ProviderUsed,
                sourceCounts.GetValueOrDefault(p.Id, 0),
                claimCounts.GetValueOrDefault(p.Id, 0),
                candidates.Length,
                p.DegradedFallback,
                p.CreatedAt);
        }).ToArray();
    }

    public async Task<ProjectSourcesAndClaims?> GetProjectSourcesAndClaimsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.ContentProjects
            .AsNoTracking()
            .Where(p => p.Id == contentProjectId)
            .Select(p => new { p.Id, p.SourceResearchPackageId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return null;
        }

        var sources = await dbContext.ResearchSources
            .AsNoTracking()
            .Where(s => s.ContentProjectId == contentProjectId
                || (project.SourceResearchPackageId.HasValue && s.ResearchPackageId == project.SourceResearchPackageId.Value))
            .OrderBy(s => s.RetrievedAt)
            .Select(s => new ResearchSource(
                s.Id,
                s.ContentProjectId,
                s.Title,
                s.Url,
                s.SourceType,
                s.RetrievedAt,
                s.SnapshotHash))
            .ToArrayAsync(cancellationToken);

        var claims = await dbContext.EvidenceClaims
            .AsNoTracking()
            .Where(c => c.ContentProjectId == contentProjectId
                || (project.SourceResearchPackageId.HasValue && c.ResearchPackageId == project.SourceResearchPackageId.Value))
            .Select(c => new EvidenceClaim(
                c.Id,
                c.ResearchPackageId,
                c.SourceId,
                c.ClaimText,
                c.ClaimKind,
                c.ConfidenceNote,
                c.VisualProofSuggestion))
            .ToArrayAsync(cancellationToken);

        return new ProjectSourcesAndClaims(
            project.Id,
            project.SourceResearchPackageId,
            sources,
            claims);
    }

    public async Task<PromotedProjectResult?> PromoteCandidateAsync(
        Guid packageId,
        int candidateIndex,
        string projectTitle,
        string projectBrief,
        IReadOnlyList<Guid> sourceIds,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var packageExists = await dbContext.ResearchPackages
            .AnyAsync(p => p.Id == packageId, cancellationToken);
        if (!packageExists)
        {
            return null;
        }

        var project = ContentProject.Create(projectTitle, projectBrief, utcNow);
        project.LinkResearchPackage(packageId, utcNow);
        dbContext.ContentProjects.Add(project);

        var sourcesToLink = await dbContext.ResearchSources
            .Where(s => s.ResearchPackageId == packageId && (sourceIds.Count == 0 || sourceIds.Contains(s.Id)))
            .ToListAsync(cancellationToken);

        foreach (var src in sourcesToLink)
        {
            src.AttachToProject(project.Id);
        }

        var claimsToLink = await dbContext.EvidenceClaims
            .Where(c => c.ResearchPackageId == packageId)
            .ToListAsync(cancellationToken);

        foreach (var claim in claimsToLink)
        {
            claim.AttachToProject(project.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PromotedProjectResult(
            project.Id,
            project.Title,
            packageId,
            candidateIndex,
            sourcesToLink.Count);
    }

    public async Task<IReadOnlyList<string>> ListRecentProjectTitlesAsync(
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.ContentProjects
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(Math.Clamp(take, 1, 50))
            .Select(p => p.Title)
            .ToArrayAsync(cancellationToken);
}
