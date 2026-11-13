import { fetchJson } from "./client";

export interface EnqueuedJobResponse {
  jobId: string;
  status: string;
}

export type EnqueueJobResponse = EnqueuedJobResponse;

export interface JobCancelResponse {
  jobId: string;
  status: string;
}

export interface JobResponse<TResult = unknown> {
  id: string;
  contentProjectId: string;
  type: string;
  status: string;
  retryCount?: number;
  maxRetries?: number;
  result: TResult | null;
  errorCode: string | null;
  errorSummary?: string | null;
  errorMessage?: string | null;
  createdAt: string;
  updatedAt: string;
  startedAt?: string | null;
  completedAt: string | null;
}

export interface GenerateIdeaResult {
  title: string;
  hook: string;
  summary: string;
  angle: string;
  targetAudience: string;
  suggestedFormat: string;
}

export interface GenerateScriptSection {
  heading: string;
  narration: string;
}

export interface GenerateScriptResult {
  title: string;
  openingHook: string;
  sections: GenerateScriptSection[];
  closing: string;
}

export interface StoryboardScene {
  heading: string;
  visual: string;
}

export interface GenerateStoryboardResult {
  title: string;
  scenes: StoryboardScene[];
}

export interface GeneratedSceneVisual {
  sceneIndex: number;
  engine: string;
  template: string;
  path: string;
  byteSize: number;
  contentHash: string;
  intendedEngine?: string;
  status?: string;
  intent?: string;
}

export interface SceneVisualRouting {
  sceneIndex: number;
  intendedEngine: string;
  engine: string;
  status: string;
  template: string;
}

export interface GenerateSceneVisualsResult {
  storyboardJobId: string;
  sceneCount: number;
  generatedCount: number;
  skippedCount: number;
  visuals: GeneratedSceneVisual[];
  routing?: SceneVisualRouting[] | null;
}

export interface GenerateAudioArtifact {
  path: string;
  contentHash: string;
  byteSize: number;
  durationSeconds: number;
  sampleRate: number;
  channels: number;
}

export interface GenerateAudioScene {
  sceneIndex: number;
  heading: string;
  narrationText: string;
  narrationPath: string;
  contentHash: string;
  narrationDurationSeconds: number;
  narrationStartSeconds: number;
  visualStartSeconds: number;
  visualDurationSeconds: number;
  reused: boolean;
}

export interface GenerateAudioResult {
  storyboardJobId: string;
  narration: GenerateAudioArtifact;
  music: GenerateAudioArtifact;
  master: GenerateAudioArtifact;
  narrationReused: boolean;
  musicReused: boolean;
  masterReused: boolean;
  scenes?: GenerateAudioScene[] | null;
  transitionSeconds?: number;
}

export interface RenderVideoResult {
  outputPath: string;
  contentHash: string;
  byteSize: number;
  durationSeconds: number;
  width: number;
  height: number;
  storyboardJobId: string;
  narrationTrackId?: string | null;
  sceneCount: number;
  narrationContentHash?: string | null;
  subtitleTrackId?: string | null;
  subtitleContentHash?: string | null;
  subtitleBurnedIn?: boolean;
  audioSource?: string;
  masteredAudioPath?: string | null;
  masteredAudioContentHash?: string | null;
  subtitleSource?: string | null;
}

export interface FinalVideoQaResult {
  outputPath: string;
  contentHash: string;
  durationSeconds: number;
  width: number;
  height: number;
  hasVideo: boolean;
  hasAudio: boolean;
  hasSubtitle: boolean;
}

export interface EnqueueGenerateIdeaInput {
  topic: string;
  targetAudience?: string;
  language?: string;
  model?: string;
  provider?: string;
}

export interface EnqueueGenerateScriptInput {
  selectedIdea: GenerateIdeaResult;
  language?: string;
  model?: string;
  provider?: string;
}

export interface EnqueueStoryboardInput {
  [key: string]: unknown;
}

export interface VisualProductionRecipeReference {
  id?: string | null;
  version?: number | null;
}

export interface VisualIdentityReference {
  assetId?: string | null;
  version?: number | null;
}

export interface EnqueueVisualJobOptions {
  force?: boolean;
  productionRecipe?: VisualProductionRecipeReference | null;
  identityReference?: VisualIdentityReference | null;
  artDirection?: string | null;
  imageProvider?: string;
  videoProvider?: string;
}

export type EnqueueGenerateVisualsInput = EnqueueVisualJobOptions;

export interface EnqueueAudioJobOptions {
  speechProvider?: string;
}

export type EnqueueGenerateAudioInput = EnqueueAudioJobOptions;

export async function getJob<TResult = unknown>(
  jobId: string,
  signal?: AbortSignal,
): Promise<JobResponse<TResult>> {
  const job = await fetchJson<JobResponse<TResult>>(`/api/jobs/${jobId}`, {
    signal,
  });

  if (job && job.errorMessage === undefined && job.errorSummary !== undefined) {
    job.errorMessage = job.errorSummary;
  }

  return job;
}

export function cancelJob(
  jobId: string,
  signal?: AbortSignal,
): Promise<JobCancelResponse> {
  return fetchJson<JobCancelResponse>(`/api/jobs/${jobId}/cancel`, {
    method: "POST",
    signal,
  });
}

export function enqueueGenerateIdeaJob(
  projectId: string,
  input: EnqueueGenerateIdeaInput,
  signal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/generate-idea-jobs`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function enqueueGenerateScriptJob(
  projectId: string,
  input: EnqueueGenerateScriptInput,
  signal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/generate-script-jobs`,
    {
      method: "POST",
      body: input,
      signal,
    },
  );
}

export function enqueueStoryboardJob(
  projectId: string,
  input?: EnqueueStoryboardInput,
  signal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/storyboard-jobs`,
    {
      method: "POST",
      body: input && Object.keys(input).length > 0 ? input : undefined,
      signal,
    },
  );
}

export function enqueueVisualJob(
  projectId: string,
  storyboardJobId: string,
  options?: EnqueueVisualJobOptions,
  signal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  const query = options?.force ? "?force=true" : "";
  const trimmedImageProvider = options?.imageProvider?.trim();
  const trimmedVideoProvider = options?.videoProvider?.trim();
  const hasBody =
    options?.productionRecipe !== undefined ||
    options?.identityReference !== undefined ||
    options?.artDirection !== undefined ||
    (trimmedImageProvider !== undefined && trimmedImageProvider.length > 0) ||
    (trimmedVideoProvider !== undefined && trimmedVideoProvider.length > 0);

  const body = hasBody
    ? {
        ...(options?.productionRecipe !== undefined
          ? { productionRecipe: options.productionRecipe }
          : {}),
        ...(options?.identityReference !== undefined
          ? { identityReference: options.identityReference }
          : {}),
        ...(options?.artDirection !== undefined
          ? { artDirection: options.artDirection }
          : {}),
        ...(trimmedImageProvider && trimmedImageProvider.length > 0
          ? { imageProvider: trimmedImageProvider }
          : {}),
        ...(trimmedVideoProvider && trimmedVideoProvider.length > 0
          ? { videoProvider: trimmedVideoProvider }
          : {}),
      }
    : undefined;

  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/storyboard-jobs/${storyboardJobId}/visual-jobs${query}`,
    {
      method: "POST",
      body,
      signal,
    },
  );
}

export function enqueueAudioJob(
  projectId: string,
  storyboardJobId: string,
  optionsOrSignal?: EnqueueAudioJobOptions | AbortSignal,
  maybeSignal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  let options: EnqueueAudioJobOptions | undefined;
  let signal: AbortSignal | undefined;

  if (optionsOrSignal instanceof AbortSignal) {
    signal = optionsOrSignal;
  } else {
    options = optionsOrSignal;
    signal = maybeSignal;
  }

  const trimmedSpeechProvider = options?.speechProvider?.trim();
  const hasBody =
    trimmedSpeechProvider !== undefined && trimmedSpeechProvider.length > 0;

  const body = hasBody
    ? { speechProvider: trimmedSpeechProvider }
    : undefined;

  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/storyboard-jobs/${storyboardJobId}/audio-jobs`,
    {
      method: "POST",
      body,
      signal,
    },
  );
}

export function enqueueRenderJob(
  projectId: string,
  storyboardJobId: string,
  variant?: string,
  signal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  const query = variant ? `?variant=${encodeURIComponent(variant)}` : "";
  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/storyboard-jobs/${storyboardJobId}/render-jobs${query}`,
    {
      method: "POST",
      signal,
    },
  );
}

export function enqueueFinalVideoQaJob(
  projectId: string,
  renderJobId: string,
  signal?: AbortSignal,
): Promise<EnqueuedJobResponse> {
  return fetchJson<EnqueuedJobResponse>(
    `/api/content-projects/${projectId}/render-jobs/${renderJobId}/qa-jobs`,
    {
      method: "POST",
      signal,
    },
  );
}
