import { fetchJson } from "./client";

export interface TrendMetricsDto {
  viewCount?: number | null;
  likeCount?: number | null;
  commentCount?: number | null;
  viewsPerHour?: number | null;
  outlierRatio?: number | null;
  engagementRate?: number | null;
}

export interface CommentInsightDto {
  text: string;
  likeCount: number;
  category?: string | null;
}

export interface TrendSignalDto {
  sourceType: string;
  externalId: string;
  title: string;
  url: string;
  channelOrAuthor?: string | null;
  publishedAt?: string | null;
  retrievedAt: string;
  metrics: TrendMetricsDto;
  rawSummary: string;
  freshnessTier: string;
  contentHash: string;
  topComments?: CommentInsightDto[] | null;
}

export interface ResearchSourceDto {
  id: string;
  contentProjectId?: string | null;
  title: string;
  url: string;
  sourceType: string;
  retrievedAt: string;
  snapshotHash: string;
}

export interface EvidenceClaimDto {
  id: string;
  researchPackageId: string;
  sourceId?: string | null;
  claimText: string;
  claimKind: string;
  confidenceNote?: string | null;
  visualProofSuggestion?: string | null;
}

export interface IdeaCandidateDto {
  title: string;
  hook: string;
  targetAudience: string;
  angle: string;
  suggestedFormat: string;
  whyTrendingNow: string;
  supportingSourceIds: string[];
  recommendedVisualStyle: string;
  angleCategory: string;
  strategicIntent: string;
  noveltyScore?: number | null;
  atomizedShorts?: string[] | null;
  titleVariants?: string[] | null;
}

export interface PacingBlueprintDto {
  hookPattern: string;
  patternInterrupts: string;
  proofOrDemoStyle: string;
  ctaStyle: string;
}

export interface FactPolicyWarningDto {
  severity: string;
  category: string;
  message: string;
}

export interface ResearchPackageDto {
  id: string;
  queryTopic: string;
  regionCode: string;
  createdAt: string;
  providerUsed: string;
  sources: ResearchSourceDto[];
  claims: EvidenceClaimDto[];
  candidates: IdeaCandidateDto[];
  pacingBlueprint?: PacingBlueprintDto | null;
  warnings?: FactPolicyWarningDto[] | null;
  degradedFallback: boolean;
}

export interface ResearchPackageSummaryDto {
  id: string;
  queryTopic: string;
  regionCode: string;
  providerUsed: string;
  sourceCount: number;
  claimCount: number;
  candidateCount: number;
  degradedFallback: boolean;
  createdAt: string;
}

export interface ProjectSourcesAndClaimsDto {
  contentProjectId: string;
  sourceResearchPackageId?: string | null;
  sources: ResearchSourceDto[];
  claims: EvidenceClaimDto[];
}

export interface PromotedProjectResultDto {
  contentProjectId: string;
  title: string;
  researchPackageId: string;
  candidateIndex: number;
  linkedSourceCount: number;
}

export interface PreRenderIssueDto {
  severity: string;
  category: string;
  message: string;
}

export interface PreRenderCheckReportDto {
  passed: boolean;
  scriptWordCount: number;
  estimatedSpeechSeconds: number;
  subtitleCueCount: number;
  issues: PreRenderIssueDto[];
}

export interface PackagingMetadataDto {
  titleVariants: string[];
  seoDescription: string;
  hashtags: string[];
  thumbnailConcept: {
    textOverlay: string;
    visualComposition: string;
  };
}

export interface LearningGuidelineDto {
  id: string;
  kind: string;
  status: string;
  title: string;
  summary: string;
  sourceCorrectionReason?: string | null;
  createdAt: string;
  updatedAt: string;
  promotedAt?: string | null;
}

export function collectTrends(
  input: {
    topic: string;
    regionCode?: string;
    includeWatchlist?: boolean;
    operatorSeedUrls?: string[];
  },
  signal?: AbortSignal,
): Promise<TrendSignalDto[]> {
  return fetchJson<TrendSignalDto[]>("/api/research/collect", {
    method: "POST",
    body: input,
    signal,
  });
}

export function synthesizeResearch(
  input: {
    topic: string;
    regionCode?: string;
    includeWatchlist?: boolean;
    operatorSeedUrls?: string[];
    signals?: TrendSignalDto[];
    targetAudience?: string;
    offerContext?: string;
    language?: string;
    provider?: string;
  },
  signal?: AbortSignal,
): Promise<ResearchPackageDto> {
  return fetchJson<ResearchPackageDto>("/api/research/synthesize", {
    method: "POST",
    body: input,
    signal,
  });
}

export function listResearchPackages(
  take = 10,
  signal?: AbortSignal,
): Promise<ResearchPackageSummaryDto[]> {
  return fetchJson<ResearchPackageSummaryDto[]>(
    `/api/research/packages?take=${take}`,
    { signal },
  );
}

export function getResearchPackage(
  id: string,
  signal?: AbortSignal,
): Promise<ResearchPackageDto> {
  return fetchJson<ResearchPackageDto>(`/api/research/packages/${id}`, {
    signal,
  });
}

export function promoteResearchCandidate(
  packageId: string,
  candidateIndex: number,
  signal?: AbortSignal,
): Promise<PromotedProjectResultDto> {
  return fetchJson<PromotedProjectResultDto>(
    `/api/research/packages/${packageId}/candidates/${candidateIndex}/promote`,
    { method: "POST", signal },
  );
}

export function getProjectSources(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectSourcesAndClaimsDto> {
  return fetchJson<ProjectSourcesAndClaimsDto>(
    `/api/content-projects/${projectId}/sources`,
    { signal },
  );
}

export function getPreRenderCheck(
  projectId: string,
  signal?: AbortSignal,
): Promise<PreRenderCheckReportDto> {
  return fetchJson<PreRenderCheckReportDto>(
    `/api/content-projects/${projectId}/pre-render-check`,
    { signal },
  );
}

export function getPackagingMetadata(
  projectId: string,
  signal?: AbortSignal,
): Promise<PackagingMetadataDto> {
  return fetchJson<PackagingMetadataDto>(
    `/api/content-projects/${projectId}/packaging`,
    { signal },
  );
}

export function generatePackagingMetadata(
  projectId: string,
  provider?: string,
  signal?: AbortSignal,
): Promise<PackagingMetadataDto> {
  return fetchJson<PackagingMetadataDto>(
    `/api/content-projects/${projectId}/packaging`,
    {
      method: "POST",
      body: provider ? { provider } : {},
      signal,
    },
  );
}

export function listLearningGuidelines(
  signal?: AbortSignal,
): Promise<LearningGuidelineDto[]> {
  return fetchJson<LearningGuidelineDto[]>("/api/learning/guidelines", {
    signal,
  });
}

export function promoteLearningGuideline(
  id: string,
  signal?: AbortSignal,
): Promise<LearningGuidelineDto> {
  return fetchJson<LearningGuidelineDto>(
    `/api/learning/guidelines/${id}/promote`,
    { method: "POST", signal },
  );
}

export function retireLearningGuideline(
  id: string,
  signal?: AbortSignal,
): Promise<LearningGuidelineDto> {
  return fetchJson<LearningGuidelineDto>(
    `/api/learning/guidelines/${id}/retire`,
    { method: "POST", signal },
  );
}
