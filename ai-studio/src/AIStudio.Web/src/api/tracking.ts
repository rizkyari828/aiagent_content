import { fetchJson } from "./client";

export interface MetricSnapshotResponse {
  id: string;
  contentProjectId: string;
  publicationRecordId: string | null;
  views: number | null;
  impressions: number | null;
  clickThroughRate: number | null;
  averageViewDurationSeconds: number | null;
  watchTimeMinutes: number | null;
  subscribersGained: number | null;
  capturedAt: string;
  source: string;
  notes: string | null;
  createdAt: string;
}

export interface CreateMetricSnapshotInput {
  views?: number | null;
  impressions?: number | null;
  clickThroughRate?: number | null;
  averageViewDurationSeconds?: number | null;
  watchTimeMinutes?: number | null;
  subscribersGained?: number | null;
  capturedAt: string;
  source?: string;
  notes?: string;
}

export type TimeEntryKind = "human" | "machine";

export interface TimeEntryResponse {
  id: string;
  contentProjectId: string;
  kind: TimeEntryKind;
  category: string;
  minutes: number;
  recordedAt: string;
  notes: string | null;
  createdAt: string;
}

export interface CreateTimeEntryInput {
  kind: TimeEntryKind;
  category: string;
  minutes: number;
  recordedAt: string;
  notes?: string;
}

export type CostEntryKind = "actual" | "estimated" | "refunded";

export interface CostEntryResponse {
  id: string;
  contentProjectId: string;
  amount: number;
  currency: string;
  kind: CostEntryKind;
  category: string;
  incurredAt: string;
  notes: string | null;
  createdAt: string;
}

export interface CreateCostEntryInput {
  amount: number;
  currency: string;
  kind: CostEntryKind;
  category: string;
  incurredAt: string;
  notes?: string;
}

export interface CurrencyCostSummary {
  currency: string;
  actualTotal: number;
  estimatedTotal: number;
  refundedTotal: number;
  netActualTotal: number;
  refundExceedsActual: boolean;
}

export type MetricSnapshot = MetricSnapshotResponse;

export interface PublicationRollupItem {
  publicationRecordId: string | null;
  snapshotCount: number;
  latestMetricSnapshot: MetricSnapshot | null;
}

export type PublicationRollup = PublicationRollupItem;

export interface TrackingSummaryResponse {
  contentProjectId: string;
  latestMetricSnapshot: MetricSnapshotResponse | null;
  humanMinutesTotal: number;
  machineMinutesTotal: number;
  costs: CurrencyCostSummary[];
  publicationRollups?: PublicationRollupItem[];
}

export type TrackingSummary = TrackingSummaryResponse;

const MIN_TAKE = 1;
const MAX_TAKE = 100;
const DEFAULT_TAKE = 20;

function sanitizeTake(take?: number): number {
  if (take === undefined) {
    return DEFAULT_TAKE;
  }
  return Math.min(MAX_TAKE, Math.max(MIN_TAKE, Math.trunc(take)));
}

export function listMetricSnapshots(
  projectId: string,
  take?: number,
  signal?: AbortSignal,
): Promise<MetricSnapshotResponse[]> {
  const safeTake = sanitizeTake(take);
  return fetchJson<MetricSnapshotResponse[]>(
    `/api/content-projects/${projectId}/metrics?take=${safeTake}`,
    { signal },
  );
}

export function createMetricSnapshot(
  projectId: string,
  input: CreateMetricSnapshotInput,
  signal?: AbortSignal,
): Promise<MetricSnapshotResponse> {
  return fetchJson<MetricSnapshotResponse>(
    `/api/content-projects/${projectId}/metrics`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function listTimeEntries(
  projectId: string,
  take?: number,
  signal?: AbortSignal,
): Promise<TimeEntryResponse[]> {
  const safeTake = sanitizeTake(take);
  return fetchJson<TimeEntryResponse[]>(
    `/api/content-projects/${projectId}/time-entries?take=${safeTake}`,
    { signal },
  );
}

export function createTimeEntry(
  projectId: string,
  input: CreateTimeEntryInput,
  signal?: AbortSignal,
): Promise<TimeEntryResponse> {
  return fetchJson<TimeEntryResponse>(
    `/api/content-projects/${projectId}/time-entries`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function listCostEntries(
  projectId: string,
  take?: number,
  signal?: AbortSignal,
): Promise<CostEntryResponse[]> {
  const safeTake = sanitizeTake(take);
  return fetchJson<CostEntryResponse[]>(
    `/api/content-projects/${projectId}/cost-entries?take=${safeTake}`,
    { signal },
  );
}

export function createCostEntry(
  projectId: string,
  input: CreateCostEntryInput,
  signal?: AbortSignal,
): Promise<CostEntryResponse> {
  return fetchJson<CostEntryResponse>(
    `/api/content-projects/${projectId}/cost-entries`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function getTrackingSummary(
  projectId: string,
  signal?: AbortSignal,
): Promise<TrackingSummaryResponse> {
  return fetchJson<TrackingSummaryResponse>(
    `/api/content-projects/${projectId}/tracking-summary`,
    { signal },
  );
}
