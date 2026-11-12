/**
 * Project Transfer API client for Content Studio.
 *
 * Implements export and import endpoints against:
 * - POST /api/content-projects/{contentProjectId}/exports
 * - GET  /api/content-projects/{contentProjectId}/exports
 * - POST /api/projects/imports
 *
 * Handles problem+json normalized error envelopes using ApiRequestError from client.ts.
 */

import {
  API_BASE_URL,
  ApiRequestError,
  fetchJson,
} from "./client";

export interface ProjectExportResponse {
  contentProjectId: string;
  schemaVersion: number;
  relativePackagePath: string;
  byteSize: number;
  contentHash: string;
  exportedAt: string;
  filesCount: number;

  // Real backend DTO properties from ProjectTransferEndpoints.cs
  fileName?: string;
  sha256?: string | null;
  createdAtUtc?: string;
}

export interface ProjectImportResponse {
  contentProjectId: string;
  schemaVersion: number;
  importedAt: string;
  totalFilesImported: number;
  totalBytesImported: number;
  packageContentHash: string;

  // Real backend DTO properties from ProjectTransferEndpoints.cs
  files?: number;
  reviewedScripts?: number;
  sourceJobs?: number;
  sceneAssets?: number;
  narrationTracks?: number;
  subtitleTracks?: number;
  finalApprovals?: number;
  publicationRecords?: number;
  metricSnapshots?: number;
  timeEntries?: number;
  costEntries?: number;
}

interface RawExportDto {
  fileName?: string;
  byteSize?: number;
  sha256?: string | null;
  createdAtUtc?: string;

  // Potential alternate/extended fields
  contentProjectId?: string;
  schemaVersion?: number;
  relativePackagePath?: string;
  contentHash?: string;
  exportedAt?: string;
  filesCount?: number;
}

interface RawImportDto {
  contentProjectId?: string;
  schemaVersion?: number;
  importedAt?: string;
  totalFilesImported?: number;
  totalBytesImported?: number;
  packageContentHash?: string;

  files?: number;
  reviewedScripts?: number;
  sourceJobs?: number;
  sceneAssets?: number;
  narrationTracks?: number;
  subtitleTracks?: number;
  finalApprovals?: number;
  publicationRecords?: number;
  metricSnapshots?: number;
  timeEntries?: number;
  costEntries?: number;
}

function normalizeExport(
  raw: RawExportDto,
  fallbackProjectId: string,
): ProjectExportResponse {
  const fileName = raw.fileName ?? raw.relativePackagePath ?? "";
  const byteSize = typeof raw.byteSize === "number" ? raw.byteSize : 0;
  const hash = raw.sha256 ?? raw.contentHash ?? "";
  const timestamp = raw.createdAtUtc ?? raw.exportedAt ?? new Date().toISOString();

  return {
    contentProjectId: raw.contentProjectId ?? fallbackProjectId,
    schemaVersion: typeof raw.schemaVersion === "number" ? raw.schemaVersion : 2,
    relativePackagePath: raw.relativePackagePath ?? fileName,
    byteSize,
    contentHash: hash,
    exportedAt: timestamp,
    filesCount: typeof raw.filesCount === "number" ? raw.filesCount : 0,

    fileName,
    sha256: raw.sha256 ?? (hash || null),
    createdAtUtc: timestamp,
  };
}

function normalizeImport(raw: RawImportDto): ProjectImportResponse {
  const contentProjectId = raw.contentProjectId ?? "";
  const files = typeof raw.files === "number" ? raw.files : 0;
  const totalFiles =
    typeof raw.totalFilesImported === "number"
      ? raw.totalFilesImported
      : files;
  const totalBytes =
    typeof raw.totalBytesImported === "number" ? raw.totalBytesImported : 0;
  const hash = raw.packageContentHash ?? "";
  const importedAt = raw.importedAt ?? new Date().toISOString();
  const schemaVersion =
    typeof raw.schemaVersion === "number" ? raw.schemaVersion : 2;

  return {
    contentProjectId,
    schemaVersion,
    importedAt,
    totalFilesImported: totalFiles,
    totalBytesImported: totalBytes,
    packageContentHash: hash,

    files,
    reviewedScripts: typeof raw.reviewedScripts === "number" ? raw.reviewedScripts : 0,
    sourceJobs: typeof raw.sourceJobs === "number" ? raw.sourceJobs : 0,
    sceneAssets: typeof raw.sceneAssets === "number" ? raw.sceneAssets : 0,
    narrationTracks: typeof raw.narrationTracks === "number" ? raw.narrationTracks : 0,
    subtitleTracks: typeof raw.subtitleTracks === "number" ? raw.subtitleTracks : 0,
    finalApprovals: typeof raw.finalApprovals === "number" ? raw.finalApprovals : 0,
    publicationRecords: typeof raw.publicationRecords === "number" ? raw.publicationRecords : 0,
    metricSnapshots: typeof raw.metricSnapshots === "number" ? raw.metricSnapshots : 0,
    timeEntries: typeof raw.timeEntries === "number" ? raw.timeEntries : 0,
    costEntries: typeof raw.costEntries === "number" ? raw.costEntries : 0,
  };
}

function buildUrl(path: string): string {
  return `${API_BASE_URL}/${path.replace(/^\/+/, "")}`;
}

/**
 * Triggers a project export archive generation.
 * POST /api/content-projects/{contentProjectId}/exports
 */
export async function exportProject(
  contentProjectId: string,
  signal?: AbortSignal,
): Promise<ProjectExportResponse> {
  const raw = await fetchJson<RawExportDto>(
    `/api/content-projects/${encodeURIComponent(contentProjectId)}/exports`,
    {
      method: "POST",
      body: {},
      signal,
    },
  );
  return normalizeExport(raw, contentProjectId);
}

/**
 * Lists previously exported packages for the given project.
 * GET /api/content-projects/{contentProjectId}/exports
 */
export async function listProjectExports(
  contentProjectId: string,
  signal?: AbortSignal,
): Promise<ProjectExportResponse[]> {
  const list = await fetchJson<RawExportDto[]>(
    `/api/content-projects/${encodeURIComponent(contentProjectId)}/exports`,
    {
      signal,
    },
  );
  const safeList = Array.isArray(list) ? list : [];
  return safeList.map((item) => normalizeExport(item, contentProjectId));
}

/**
 * Uploads and imports an exported ZIP project package.
 * POST /api/projects/imports
 *
 * Uses FormData multipart upload with key "file" matching ProjectTransferEndpoints.ImportRoute.
 * Content-Type header is left unset so the browser generates the multipart boundary.
 */
export async function importProjectPackage(
  file: File,
  signal?: AbortSignal,
  timeoutMs = 60_000,
): Promise<ProjectImportResponse> {
  const controller = new AbortController();
  const abortFromCaller = () => controller.abort();
  signal?.addEventListener("abort", abortFromCaller, { once: true });
  if (signal?.aborted) {
    controller.abort();
  }

  let timedOut = false;
  const timeoutId = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, timeoutMs);

  const formData = new FormData();
  formData.append("file", file, file.name);

  const url = buildUrl("/api/projects/imports");

  try {
    const response = await fetch(url, {
      method: "POST",
      headers: {
        Accept: "application/json",
      },
      body: formData,
      signal: controller.signal,
    });

    const text = await response.text();
    let payload: unknown = null;
    if (text) {
      try {
        payload = JSON.parse(text) as unknown;
      } catch {
        payload = null;
      }
    }

    if (!response.ok) {
      const problem =
        typeof payload === "object" && payload !== null && !Array.isArray(payload)
          ? (payload as Record<string, unknown>)
          : null;

      const rawTitle = problem?.title;
      const rawDetail = problem?.detail;
      const rawCode = problem?.code;

      let fieldErrors: Record<string, string[]> | undefined;
      if (
        problem?.errors &&
        typeof problem.errors === "object" &&
        !Array.isArray(problem.errors)
      ) {
        const parsedErrors: Record<string, string[]> = {};
        for (const [key, messages] of Object.entries(
          problem.errors as Record<string, unknown>,
        )) {
          if (Array.isArray(messages)) {
            const cleaned = messages.filter(
              (m): m is string => typeof m === "string" && m.length > 0,
            );
            if (cleaned.length > 0) {
              parsedErrors[key] = cleaned;
            }
          }
        }
        if (Object.keys(parsedErrors).length > 0) {
          fieldErrors = parsedErrors;
        }
      }

      throw new ApiRequestError({
        status: response.status,
        code:
          typeof rawCode === "string" && rawCode.length > 0 ? rawCode : undefined,
        title:
          typeof rawTitle === "string" && rawTitle.length > 0
            ? rawTitle
            : `HTTP ${response.status}`,
        detail:
          typeof rawDetail === "string" && rawDetail.length > 0
            ? rawDetail
            : undefined,
        fieldErrors,
      });
    }

    return normalizeImport((payload ?? {}) as RawImportDto);
  } catch (error) {
    if (error instanceof ApiRequestError) {
      throw error;
    }

    if (controller.signal.aborted) {
      throw new ApiRequestError(
        timedOut
          ? {
              status: 0,
              code: "request_timeout",
              title: "Request timed out.",
            }
          : {
              status: 0,
              code: "request_cancelled",
              title: "Request cancelled.",
            },
      );
    }

    throw new ApiRequestError({
      status: 0,
      code: "network_error",
      title: "Network request failed.",
      detail: error instanceof Error ? error.message : undefined,
    });
  } finally {
    clearTimeout(timeoutId);
    signal?.removeEventListener("abort", abortFromCaller);
  }
}
