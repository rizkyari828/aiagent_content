import { fetchJson } from "./client";

export interface ProjectSummary {
  id: string;
  title: string;
  status: string;
  updatedAt: string;
}

export interface CreatedProject {
  id: string;
  title: string;
  brief: string | null;
  status: string;
}

export interface CreateProjectInput {
  title: string;
  brief?: string;
}

const MIN_TAKE = 1;
const MAX_TAKE = 100;

export function listProjects(
  take = MAX_TAKE,
  signal?: AbortSignal,
): Promise<ProjectSummary[]> {
  const safeTake = Math.min(MAX_TAKE, Math.max(MIN_TAKE, Math.trunc(take)));
  return fetchJson<ProjectSummary[]>(`/api/content-projects?take=${safeTake}`, {
    signal,
  });
}

export function createProject(
  input: CreateProjectInput,
  signal?: AbortSignal,
): Promise<CreatedProject> {
  return fetchJson<CreatedProject>("/api/content-projects", {
    method: "POST",
    body: {
      title: input.title,
      ...(input.brief ? { brief: input.brief } : {}),
    },
    signal,
  });
}

export interface ProjectLastJob {
  id: string;
  type: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  completedAt: string | null;
}

export interface ProjectDetail {
  id: string;
  title: string;
  brief: string | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  lastJob: ProjectLastJob | null;
}

export interface ReadinessReason {
  code: string;
  message: string;
  blocking: boolean;
}

export interface ProjectReadiness {
  contentProjectId: string;
  status: string;
  readyToPublish: boolean;
  projectStatus: string;
  reasons: ReadinessReason[];
}

export interface UpdateProjectInput {
  title: string;
  brief: string | null;
}

export function getProject(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectDetail> {
  return fetchJson<ProjectDetail>(`/api/content-projects/${projectId}`, { signal });
}

export function updateProject(
  projectId: string,
  input: UpdateProjectInput,
  signal?: AbortSignal,
): Promise<ProjectDetail> {
  return fetchJson<ProjectDetail>(`/api/content-projects/${projectId}`, {
    method: "PUT",
    body: { title: input.title, brief: input.brief },
    signal,
  });
}

export function getProjectReadiness(
  projectId: string,
  signal?: AbortSignal,
): Promise<ProjectReadiness> {
  return fetchJson<ProjectReadiness>(
    `/api/content-projects/${projectId}/readiness`,
    { signal },
  );
}