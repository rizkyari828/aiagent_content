import { fetchJson } from "./client";

export interface ScriptSectionDto {
  heading: string;
  narration: string;
}

export interface ScriptContentDto {
  title: string;
  openingHook: string;
  sections: ScriptSectionDto[];
  closing: string;
}

export interface ReviewedScriptResponse {
  id: string;
  contentProjectId: string;
  sourceJobId: string;
  script: ScriptContentDto;
  status: string;
  revision: number;
  createdAt: string;
  updatedAt: string;
  approvedAt: string | null;
}

export interface StartScriptReviewInput {
  sourceJobId: string;
}

export interface UpdateReviewedScriptInput {
  script: ScriptContentDto;
}

export function getReviewedScript(
  projectId: string,
  signal?: AbortSignal,
): Promise<ReviewedScriptResponse> {
  return fetchJson<ReviewedScriptResponse>(
    `/api/content-projects/${projectId}/script-review`,
    { signal },
  );
}

export function startScriptReview(
  projectId: string,
  sourceJobId: string | StartScriptReviewInput,
  signal?: AbortSignal,
): Promise<ReviewedScriptResponse> {
  const body: StartScriptReviewInput =
    typeof sourceJobId === "string" ? { sourceJobId } : sourceJobId;

  return fetchJson<ReviewedScriptResponse>(
    `/api/content-projects/${projectId}/script-review`,
    {
      method: "POST",
      body,
      signal,
    },
  );
}

export function updateReviewedScript(
  projectId: string,
  content: ScriptContentDto | UpdateReviewedScriptInput,
  signal?: AbortSignal,
): Promise<ReviewedScriptResponse> {
  const body: UpdateReviewedScriptInput =
    "script" in content ? content : { script: content };

  return fetchJson<ReviewedScriptResponse>(
    `/api/content-projects/${projectId}/script-review`,
    {
      method: "PUT",
      body,
      signal,
    },
  );
}

export function approveReviewedScript(
  projectId: string,
  signal?: AbortSignal,
): Promise<ReviewedScriptResponse> {
  return fetchJson<ReviewedScriptResponse>(
    `/api/content-projects/${projectId}/script-review/approve`,
    {
      method: "POST",
      signal,
    },
  );
}
