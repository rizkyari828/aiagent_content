import { API_BASE_URL, fetchJson } from "./client";

export interface SceneAssetItem {
  id: string;
  contentProjectId: string;
  sourceJobId: string;
  sceneIndex: number;
  type: string;
  path: string;
  byteSize: number;
  contentHash: string;
  origin: string;
  source: string | null;
  creator: string | null;
  license: string | null;
  retrievedAt: string | null;
  createdAt: string;
}

export interface RegisterSceneAssetInput {
  sceneIndex: number;
  path: string;
  type: string;
  origin: string;
  source?: string | null;
  creator?: string | null;
  license?: string | null;
  retrievedAt?: string | null;
}

export interface NarrationTrackResponse {
  id: string;
  contentProjectId: string;
  sourceJobId: string;
  path: string;
  byteSize: number;
  contentHash: string;
  origin: string;
  source: string | null;
  creator: string | null;
  license: string | null;
  retrievedAt: string | null;
  createdAt: string;
}

export interface RegisterNarrationInput {
  path: string;
  origin: string;
  source?: string | null;
  creator?: string | null;
  license?: string | null;
  retrievedAt?: string | null;
}

export interface SubtitleTrackResponse {
  id: string;
  contentProjectId: string;
  sourceJobId: string;
  path: string;
  byteSize: number;
  contentHash: string;
  origin: string;
  source: string | null;
  creator: string | null;
  license: string | null;
  retrievedAt: string | null;
  createdAt: string;
}

export type SubtitleTrack = SubtitleTrackResponse;

export interface RegisterSubtitleInput {
  path: string;
  origin: string;
  source?: string | null;
  creator?: string | null;
  license?: string | null;
  retrievedAt?: string | null;
}

export interface SubtitleContentResponse {
  contentProjectId: string;
  path: string;
  content: string;
  byteSize: number;
  contentHash: string;
}

export function listProjectAssets(
  projectId: string,
  signal?: AbortSignal,
): Promise<SceneAssetItem[]> {
  return fetchJson<SceneAssetItem[]>(`/api/content-projects/${projectId}/assets`, {
    signal,
  });
}

export function registerSceneAsset(
  projectId: string,
  storyboardJobId: string,
  input: RegisterSceneAssetInput,
  signal?: AbortSignal,
): Promise<SceneAssetItem> {
  return fetchJson<SceneAssetItem>(
    `/api/content-projects/${projectId}/storyboard-jobs/${storyboardJobId}/assets`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function getProjectNarration(
  projectId: string,
  signal?: AbortSignal,
): Promise<NarrationTrackResponse> {
  return fetchJson<NarrationTrackResponse>(
    `/api/content-projects/${projectId}/narration`,
    {
      signal,
    },
  );
}

export function registerProjectNarration(
  projectId: string,
  storyboardJobId: string,
  input: RegisterNarrationInput,
  signal?: AbortSignal,
): Promise<NarrationTrackResponse> {
  return fetchJson<NarrationTrackResponse>(
    `/api/content-projects/${projectId}/storyboard-jobs/${storyboardJobId}/narration`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function getProjectSubtitle(
  projectId: string,
  signal?: AbortSignal,
): Promise<SubtitleTrackResponse> {
  return fetchJson<SubtitleTrackResponse>(
    `/api/content-projects/${projectId}/subtitle`,
    {
      signal,
    },
  );
}

export function registerProjectSubtitle(
  projectId: string,
  storyboardJobId: string,
  input: RegisterSubtitleInput,
  signal?: AbortSignal,
): Promise<SubtitleTrackResponse> {
  return fetchJson<SubtitleTrackResponse>(
    `/api/content-projects/${projectId}/storyboard-jobs/${storyboardJobId}/subtitle`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function getSubtitleContent(
  projectId: string,
  signal?: AbortSignal,
): Promise<SubtitleContentResponse> {
  return fetchJson<SubtitleContentResponse>(
    `/api/content-projects/${projectId}/subtitle/content`,
    {
      signal,
    },
  );
}

export function updateSubtitleContent(
  projectId: string,
  srtContent: string,
  signal?: AbortSignal,
): Promise<SubtitleContentResponse> {
  return fetchJson<SubtitleContentResponse>(
    `/api/content-projects/${projectId}/subtitle/content`,
    {
      method: "PUT",
      body: { srtContent },
      signal,
    },
  );
}

export function getRenderPreviewUrl(
  projectId: string,
  renderJobId: string,
): string {
  return `${API_BASE_URL}/api/content-projects/${encodeURIComponent(projectId)}/render-jobs/${encodeURIComponent(renderJobId)}/preview`;
}

export function transcribeSubtitleFromNarration(
  projectId: string,
  language?: string,
  signal?: AbortSignal,
): Promise<SubtitleTrack> {
  return fetchJson<SubtitleTrack>(
    `/api/content-projects/${encodeURIComponent(projectId)}/subtitle/transcribe`,
    {
      method: "POST",
      body: { language: language ?? null },
      signal,
    },
  );
}
