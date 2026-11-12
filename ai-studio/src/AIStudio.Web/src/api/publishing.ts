import { fetchJson } from "./client";

export type FinalApprovalDecision = "approved" | "changes_requested";

export interface FinalApprovalDto {
  id: string;
  contentProjectId: string;
  sourceRenderJobId: string;
  artifactRelativePath: string;
  artifactPath?: string;
  artifactContentHash: string;
  artifactByteSize: number;
  destinationPlatform: string;
  destination?: string;
  decision: FinalApprovalDecision;
  decisionNote: string | null;
  decidedAt: string;
  notes: string | null;
  approvedBy: string | null;
  approverLabel?: string | null;
  revision: number;
  approvedAt: string;
  createdAt?: string;
  updatedAt: string;
}

export type FinalApprovalResponse = FinalApprovalDto;

export interface FinalApprovalStatusResponse {
  contentProjectId: string;
  status: string;
  isValid: boolean;
  decision: FinalApprovalDecision | null;
  decisionNote: string | null;
  decidedAt: string | null;
  reason: string | null;
  reasons: string[];
  approval: FinalApprovalDto | null;
}

export interface RecordFinalApprovalInput {
  sourceRenderJobId: string;
  destinationPlatform: string;
  decision?: FinalApprovalDecision;
  note?: string;
  notes?: string;
  approvedBy?: string;
  /** Backwards compatibility alias for sourceRenderJobId */
  renderJobId?: string;
  /** Backwards compatibility alias for destinationPlatform */
  destination?: string;
  /** Backwards compatibility alias for approvedBy */
  approverLabel?: string;
}

export interface PublicationRecordResponse {
  id: string;
  contentProjectId: string;
  finalApprovalId: string;
  finalApprovalRevision: number;
  artifactContentHash: string;
  platform: string;
  url: string;
  videoId: string;
  publishedAt: string;
  notes: string | null;
  createdAt: string;
}

export type PublicationResponse = PublicationRecordResponse;

export interface RecordPublicationInput {
  platform: string;
  url: string;
  videoId: string;
  publishedAt: string;
  notes?: string | null;
}

export interface TransitionReadinessReason {
  code: string;
  message: string;
  blocking: boolean;
}

export interface ProjectTransitionResponse {
  contentProjectId: string;
  transitioned: boolean;
  status: string;
  projectStatus: string;
  reasons: TransitionReadinessReason[];
}

export type ContentProjectTransitionResponse = ProjectTransitionResponse;

export interface ProjectExportMetadataResponse {
  fileName: string;
  byteSize: number;
  sha256: string | null;
  createdAtUtc: string;
  createdAt?: string;
}

export type ProjectExportResponse = ProjectExportMetadataResponse;

interface RawFinalApprovalDto {
  id: string;
  contentProjectId: string;
  sourceRenderJobId: string;
  artifactPath?: string;
  artifactRelativePath?: string;
  artifactContentHash: string;
  artifactByteSize: number;
  destination?: string;
  destinationPlatform?: string;
  notes: string | null;
  approverLabel?: string | null;
  approvedBy?: string | null;
  decision: string;
  decisionNote: string | null;
  decidedAt: string;
  revision: number;
  approvedAt: string;
  createdAt?: string;
  updatedAt: string;
}

interface RawFinalApprovalStatusResponse {
  contentProjectId?: string;
  status: string;
  isValid?: boolean;
  reason?: string | null;
  reasons?: string[];
  decision?: string | null;
  decisionNote?: string | null;
  decidedAt?: string | null;
  approval?: RawFinalApprovalDto | null;
}

function normalizeFinalApprovalDto(raw: RawFinalApprovalDto): FinalApprovalDto {
  const artifactRelativePath = raw.artifactRelativePath ?? raw.artifactPath ?? "";
  const destinationPlatform = raw.destinationPlatform ?? raw.destination ?? "";
  const approvedBy = raw.approvedBy ?? raw.approverLabel ?? null;

  return {
    id: raw.id,
    contentProjectId: raw.contentProjectId,
    sourceRenderJobId: raw.sourceRenderJobId,
    artifactRelativePath,
    artifactPath: raw.artifactPath ?? artifactRelativePath,
    artifactContentHash: raw.artifactContentHash,
    artifactByteSize: raw.artifactByteSize,
    destinationPlatform,
    destination: raw.destination ?? destinationPlatform,
    decision: (raw.decision === "changes_requested"
      ? "changes_requested"
      : "approved") as FinalApprovalDecision,
    decisionNote: raw.decisionNote ?? null,
    decidedAt: raw.decidedAt,
    notes: raw.notes ?? null,
    approvedBy,
    approverLabel: raw.approverLabel ?? approvedBy,
    revision: raw.revision,
    approvedAt: raw.approvedAt,
    createdAt: raw.createdAt,
    updatedAt: raw.updatedAt,
  };
}

export async function getFinalApproval(
  projectId: string,
  signal?: AbortSignal,
): Promise<FinalApprovalStatusResponse> {
  const raw = await fetchJson<RawFinalApprovalStatusResponse>(
    `/api/content-projects/${projectId}/final-approvals`,
    { signal },
  );

  const approval = raw.approval ? normalizeFinalApprovalDto(raw.approval) : null;
  const status = raw.status ?? "missing";
  const isValid = raw.isValid ?? (status === "approved");

  return {
    contentProjectId: raw.contentProjectId ?? projectId,
    status,
    isValid,
    decision: (raw.decision as FinalApprovalDecision | null) ?? (approval?.decision ?? null),
    decisionNote: raw.decisionNote ?? (approval?.decisionNote ?? null),
    decidedAt: raw.decidedAt ?? (approval?.decidedAt ?? null),
    reason: raw.reason ?? null,
    reasons: Array.isArray(raw.reasons) ? raw.reasons : raw.reason ? [raw.reason] : [],
    approval,
  };
}

export async function recordFinalApproval(
  projectId: string,
  input: RecordFinalApprovalInput,
  signal?: AbortSignal,
): Promise<FinalApprovalDto> {
  const renderJobId = input.sourceRenderJobId || input.renderJobId;
  const destination = input.destinationPlatform || input.destination;
  const approverLabel = input.approvedBy ?? input.approverLabel;

  const raw = await fetchJson<RawFinalApprovalDto>(
    `/api/content-projects/${projectId}/final-approvals`,
    {
      method: "POST",
      body: {
        renderJobId,
        destination,
        ...(input.decision ? { decision: input.decision } : {}),
        ...(input.note ? { note: input.note } : {}),
        ...(input.notes ? { notes: input.notes } : {}),
        ...(approverLabel ? { approverLabel } : {}),
      },
      signal,
    },
  );

  return normalizeFinalApprovalDto(raw);
}

export function getPublication(
  projectId: string,
  signal?: AbortSignal,
): Promise<PublicationRecordResponse> {
  return fetchJson<PublicationRecordResponse>(
    `/api/content-projects/${projectId}/publications`,
    { signal },
  );
}

export function recordPublication(
  projectId: string,
  input: RecordPublicationInput,
  signal?: AbortSignal,
): Promise<PublicationRecordResponse> {
  return fetchJson<PublicationRecordResponse>(
    `/api/content-projects/${projectId}/publications`,
    {
      method: "POST",
      body: {
        platform: input.platform,
        url: input.url,
        videoId: input.videoId,
        publishedAt: input.publishedAt,
        notes: input.notes ?? null,
      },
      signal,
    },
  );
}

export function transitionProjectToReady(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectTransitionResponse> {
  return fetchJson<ProjectTransitionResponse>(
    `/api/content-projects/${projectId}/transitions`,
    {
      method: "POST",
      body: {
        to: "ready_to_publish",
        targetStatus: "ready_to_publish",
      },
      signal,
    },
  );
}

export function listProjectExports(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectExportMetadataResponse[]> {
  return fetchJson<ProjectExportMetadataResponse[]>(
    `/api/content-projects/${projectId}/exports`,
    { signal },
  );
}

export function createProjectExport(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectExportMetadataResponse> {
  return fetchJson<ProjectExportMetadataResponse>(
    `/api/content-projects/${projectId}/exports`,
    {
      method: "POST",
      signal,
    },
  );
}
