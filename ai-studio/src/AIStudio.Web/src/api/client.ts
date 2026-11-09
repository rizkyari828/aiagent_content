/**
 * Small fetch wrapper for the Content Studio API.
 *
 * - Base URL: VITE_API_BASE_URL, defaulting to the local loopback API.
 * - Every request has an AbortController timeout.
 * - Failures are normalized to `{ code?, title, detail?, status }`, with the
 *   stable problem+json `code` extension preserved for the UI.
 */

export const DEFAULT_API_BASE_URL = "http://127.0.0.1:5080";
export const DEFAULT_TIMEOUT_MS = 15_000;

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

const configuredBaseUrl = (
  import.meta.env.VITE_API_BASE_URL as string | undefined
)?.trim();

export const API_BASE_URL = normalizeBaseUrl(configuredBaseUrl || DEFAULT_API_BASE_URL);

export interface ApiError {
  code?: string;
  title: string;
  detail?: string;
  status: number;
  fieldErrors?: Record<string, string[]>;
}

export class ApiRequestError extends Error implements ApiError {
  readonly code?: string;
  readonly title: string;
  readonly detail?: string;
  readonly status: number;
  readonly fieldErrors?: Record<string, string[]>;

  constructor(error: ApiError) {
    super(error.detail ?? error.title);
    this.name = "ApiRequestError";
    this.code = error.code;
    this.title = error.title;
    this.detail = error.detail;
    this.status = error.status;
    this.fieldErrors = error.fieldErrors;
  }
}

export interface RequestOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  timeoutMs?: number;
  signal?: AbortSignal;
  headers?: Record<string, string>;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function readFieldErrors(value: unknown): Record<string, string[]> | undefined {
  const source = asRecord(value);

  if (!source) {
    return undefined;
  }

  const fieldErrors: Record<string, string[]> = {};

  for (const [field, messages] of Object.entries(source)) {
    if (Array.isArray(messages)) {
      const cleaned = messages.filter(
        (message): message is string => typeof message === "string" && message.length > 0,
      );

      if (cleaned.length > 0) {
        fieldErrors[field] = cleaned;
      }
    }
  }

  return Object.keys(fieldErrors).length > 0 ? fieldErrors : undefined;
}

function normalizeError(
  response: Response,
  payload: unknown,
): ApiError {
  const problem = asRecord(payload);
  const rawTitle = problem?.title;
  const rawDetail = problem?.detail;
  const rawCode = problem?.code;

  return {
    status: response.status,
    code: typeof rawCode === "string" && rawCode.length > 0 ? rawCode : undefined,
    title:
      typeof rawTitle === "string" && rawTitle.length > 0
        ? rawTitle
        : `HTTP ${response.status}`,
    detail:
      typeof rawDetail === "string" && rawDetail.length > 0 ? rawDetail : undefined,
    fieldErrors: readFieldErrors(problem?.errors),
  };
}

function parseBody(text: string): unknown {
  if (!text) {
    return null;
  }

  try {
    return JSON.parse(text) as unknown;
  } catch {
    return null;
  }
}

export async function fetchJson<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const {
    method = "GET",
    body,
    timeoutMs = DEFAULT_TIMEOUT_MS,
    signal: callerSignal,
    headers: extraHeaders,
  } = options;

  const controller = new AbortController();
  const abortFromCaller = () => controller.abort();
  callerSignal?.addEventListener("abort", abortFromCaller, { once: true });

  let timedOut = false;
  const timeoutId = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, timeoutMs);

  const url = `${API_BASE_URL}/${path.replace(/^\/+/, "")}`;
  const headers: Record<string, string> = {
    Accept: "application/json",
    ...extraHeaders,
  };

  if (body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  try {
    const response = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal: controller.signal,
    });

    const text = await response.text();
    const payload = parseBody(text);

    if (!response.ok) {
      throw new ApiRequestError(normalizeError(response, payload));
    }

    return payload as T;
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
    callerSignal?.removeEventListener("abort", abortFromCaller);
  }
}

export function isApiRequestError(error: unknown): error is ApiRequestError {
  return error instanceof ApiRequestError;
}
