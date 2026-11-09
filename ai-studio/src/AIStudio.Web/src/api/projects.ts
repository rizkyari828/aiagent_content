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
