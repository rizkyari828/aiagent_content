import { useCallback, useEffect, useState, type FormEvent } from "react";
import { isApiRequestError, type ApiError } from "../api/client";
import { cancelJob, getJob, type JobResponse } from "../api/jobs";
import { Badge, type BadgeTone } from "../components/Badge";
import { Button } from "../components/Button";
import { Card } from "../components/Card";
import { Dialog } from "../components/Dialog";
import { EmptyState } from "../components/EmptyState";
import { InlineError } from "../components/InlineError";
import { Skeleton } from "../components/Skeleton";
import { Table, type TableColumn } from "../components/Table";
import { useToast } from "../components/Toast";
import { useFormatters } from "../i18n/format";
import { useI18n } from "../i18n/i18n";

const RECENT_JOBS_STORAGE_KEY = "aistudio.recentJobs";

export interface RecentJobItem {
  id: string;
  type: string;
  status: string;
  timestamp: string;
}

function loadRecentJobs(): RecentJobItem[] {
  if (typeof window === "undefined") {
    return [];
  }
  try {
    const raw = window.sessionStorage.getItem(RECENT_JOBS_STORAGE_KEY);
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw) as unknown;
    return Array.isArray(parsed) ? (parsed as RecentJobItem[]) : [];
  } catch {
    return [];
  }
}

function saveRecentJobs(items: RecentJobItem[]): void {
  if (typeof window === "undefined") {
    return;
  }
  try {
    window.sessionStorage.setItem(
      RECENT_JOBS_STORAGE_KEY,
      JSON.stringify(items.slice(0, 20)),
    );
  } catch {
    // Ignore storage quota or security restrictions
  }
}

function toApiError(error: unknown): ApiError {
  if (isApiRequestError(error)) {
    return {
      code: error.code,
      title: error.title,
      detail: error.detail,
      status: error.status,
      fieldErrors: error.fieldErrors,
    };
  }

  return {
    code: "unexpected_error",
    title: "Unexpected error.",
    detail: error instanceof Error ? error.message : undefined,
    status: 0,
  };
}

function getJobStatusTone(status: string): BadgeTone {
  switch (status.toLowerCase()) {
    case "succeeded":
    case "completed":
      return "success";
    case "running":
    case "producing":
      return "warning";
    case "queued":
    case "retrying":
      return "accent";
    case "failed":
      return "danger";
    case "cancelled":
    case "canceled":
      return "muted";
    default:
      return "neutral";
  }
}

function isJobActive(status: string): boolean {
  const normalized = status.toLowerCase();
  return (
    normalized === "pending" ||
    normalized === "queued" ||
    normalized === "running"
  );
}

export interface RunsScreenProps {
  initialJobId?: string;
  onOpenProject?: (projectId: string) => void;
  onSelectJob?: (jobId: string) => void;
}

export function RunsScreen({
  initialJobId,
  onOpenProject,
  onSelectJob,
}: RunsScreenProps) {
  const { t } = useI18n();
  const { dateTime } = useFormatters();
  const { showToast } = useToast();

  const [inputJobId, setInputJobId] = useState(() => initialJobId ?? "");
  const [activeJob, setActiveJob] = useState<JobResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const [recentJobs, setRecentJobs] = useState<RecentJobItem[]>(() =>
    loadRecentJobs(),
  );

  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  const recordRecentJob = useCallback((job: JobResponse) => {
    setRecentJobs((prev) => {
      const filtered = prev.filter((item) => item.id !== job.id);
      const updated: RecentJobItem[] = [
        {
          id: job.id,
          type: job.type,
          status: job.status,
          timestamp: job.updatedAt || job.createdAt || new Date().toISOString(),
        },
        ...filtered,
      ];
      saveRecentJobs(updated);
      return updated;
    });
  }, []);

  const inspectJob = useCallback(
    async (idToInspect: string, signal?: AbortSignal) => {
      const trimmed = idToInspect.trim();
      if (!trimmed) {
        return;
      }

      setLoading(true);
      setError(null);

      try {
        const job = await getJob(trimmed, signal);
        if (signal?.aborted) {
          return;
        }
        setActiveJob(job);
        recordRecentJob(job);
        setLoading(false);
      } catch (err: unknown) {
        if (signal?.aborted) {
          return;
        }
        setLoading(false);
        setError(toApiError(err));
      }
    },
    [recordRecentJob],
  );

  useEffect(() => {
    if (initialJobId) {
      setInputJobId(initialJobId);
      const controller = new AbortController();
      inspectJob(initialJobId, controller.signal);
      return () => controller.abort();
    }
  }, [initialJobId, inspectJob]);

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = inputJobId.trim();
    if (!trimmed) {
      return;
    }
    onSelectJob?.(trimmed);
    inspectJob(trimmed);
  };

  const handleSelectRecent = (jobId: string) => {
    setInputJobId(jobId);
    onSelectJob?.(jobId);
    inspectJob(jobId);
  };

  const handleConfirmCancel = async () => {
    if (!activeJob) {
      return;
    }

    setCancelling(true);
    try {
      await cancelJob(activeJob.id);
      showToast(t("runs.cancel.successToast"), "success");
      setCancelDialogOpen(false);
      // Refresh the job status
      await inspectJob(activeJob.id);
    } catch (err: unknown) {
      const apiErr = toApiError(err);
      showToast(apiErr.detail ?? apiErr.title, "error");
    } finally {
      setCancelling(false);
    }
  };

  const recentColumns: readonly TableColumn<RecentJobItem>[] = [
    {
      key: "id",
      header: t("runs.table.jobId"),
      render: (row) => (
        <button
          type="button"
          className="link-button mono"
          onClick={() => handleSelectRecent(row.id)}
        >
          {row.id}
        </button>
      ),
    },
    {
      key: "type",
      header: t("runs.table.type"),
      render: (row) => <span className="mono">{row.type}</span>,
    },
    {
      key: "status",
      header: t("runs.table.status"),
      render: (row) => {
        const tone = getJobStatusTone(row.status);
        const lower = row.status.toLowerCase();
        const labelKey = `runs.status.${lower}`;
        return <Badge tone={tone}>{t(labelKey)}</Badge>;
      },
    },
    {
      key: "timestamp",
      header: t("runs.table.timestamp"),
      render: (row) => dateTime(row.timestamp),
    },
    {
      key: "action",
      header: t("runs.table.action"),
      align: "end",
      render: (row) => (
        <Button
          size="sm"
          variant="ghost"
          onClick={() => handleSelectRecent(row.id)}
        >
          {t("runs.inspector.inspectAction")}
        </Button>
      ),
    },
  ];

  return (
    <>
      <div className="page-header">
        <div className="page-header__text">
          <h1 className="page-header__title">{t("runs.title")}</h1>
          <p className="page-header__subtitle">{t("runs.subtitle")}</p>
        </div>
      </div>

      <div className="stack">
        {/* Card 1: Job Inspector by ID */}
        <Card
          title={t("runs.inspector.title")}
          description={t("runs.inspector.description")}
        >
          <form className="form-stack" onSubmit={handleSubmit} noValidate>
            <div className="field">
              <label className="field__label" htmlFor="job-inspector-input">
                {t("runs.inspector.jobIdLabel")}
              </label>
              <div
                style={{
                  display: "flex",
                  gap: "var(--space-3)",
                  alignItems: "center",
                  flexWrap: "wrap",
                }}
              >
                <input
                  id="job-inspector-input"
                  type="text"
                  className="field__input mono"
                  style={{ flex: 1, minWidth: "260px" }}
                  placeholder={t("runs.inspector.jobIdPlaceholder")}
                  value={inputJobId}
                  onChange={(e) => setInputJobId(e.target.value)}
                />
                <Button
                  type="submit"
                  variant="primary"
                  loading={loading}
                  loadingLabel={t("runs.inspector.loading")}
                  disabled={!inputJobId.trim()}
                >
                  {t("runs.inspector.inspectAction")}
                </Button>
              </div>
            </div>
          </form>

          {loading ? (
            <div
              role="status"
              aria-busy="true"
              style={{
                marginTop: "var(--space-5)",
                display: "flex",
                flexDirection: "column",
                gap: "var(--space-3)",
              }}
            >
              <span className="visually-hidden">
                {t("runs.inspector.loading")}
              </span>
              <Skeleton width="100%" height={36} />
              <Skeleton width="100%" height={80} />
              <Skeleton width="100%" height={120} />
            </div>
          ) : error ? (
            <div style={{ marginTop: "var(--space-5)" }}>
              <InlineError
                title={t("runs.inspector.errorTitle")}
                detail={error.detail ?? error.title}
                code={error.code}
                actionLabel={t("action.retry")}
                onAction={() => inspectJob(inputJobId)}
              />
            </div>
          ) : activeJob ? (
            <div className="stack" style={{ marginTop: "var(--space-5)" }}>
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: "var(--space-3)",
                }}
              >
                <div
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: "var(--space-2)",
                  }}
                >
                  <Badge tone={getJobStatusTone(activeJob.status)}>
                    {t(`runs.status.${activeJob.status.toLowerCase()}`)}
                  </Badge>
                  <span
                    className="mono"
                    style={{
                      fontSize: "var(--text-lg)",
                      fontWeight: "var(--font-semibold)",
                    }}
                  >
                    {activeJob.type}
                  </span>
                </div>

                {isJobActive(activeJob.status) ? (
                  <Button
                    variant="danger"
                    size="sm"
                    onClick={() => setCancelDialogOpen(true)}
                  >
                    {t("runs.cancel.button")}
                  </Button>
                ) : null}
              </div>

              <dl className="definition-list">
                <div>
                  <dt>{t("runs.inspector.field.id")}</dt>
                  <dd className="mono">{activeJob.id}</dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.projectId")}</dt>
                  <dd>
                    {onOpenProject ? (
                      <button
                        type="button"
                        className="link-button mono"
                        onClick={() => onOpenProject(activeJob.contentProjectId)}
                      >
                        {activeJob.contentProjectId}
                      </button>
                    ) : (
                      <a
                        href={`#/projects/${activeJob.contentProjectId}`}
                        className="mono"
                      >
                        {activeJob.contentProjectId}
                      </a>
                    )}
                  </dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.type")}</dt>
                  <dd className="mono">{activeJob.type}</dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.status")}</dt>
                  <dd>
                    <Badge tone={getJobStatusTone(activeJob.status)}>
                      {t(`runs.status.${activeJob.status.toLowerCase()}`)}
                    </Badge>
                  </dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.createdAt")}</dt>
                  <dd>{dateTime(activeJob.createdAt)}</dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.startedAt")}</dt>
                  <dd>
                    {activeJob.startedAt ? dateTime(activeJob.startedAt) : "—"}
                  </dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.completedAt")}</dt>
                  <dd>
                    {activeJob.completedAt
                      ? dateTime(activeJob.completedAt)
                      : "—"}
                  </dd>
                </div>
                <div>
                  <dt>{t("runs.inspector.field.retries")}</dt>
                  <dd className="mono">
                    {t("runs.inspector.field.retriesValue", {
                      current: activeJob.retryCount ?? 0,
                      max: activeJob.maxRetries ?? 0,
                    })}
                  </dd>
                </div>
                {activeJob.errorCode ||
                activeJob.errorMessage ||
                activeJob.errorSummary ? (
                  <div>
                    <dt>{t("runs.inspector.field.error")}</dt>
                    <dd>
                      <span className="badge badge--danger">
                        {activeJob.errorCode ??
                          t("runs.inspector.field.error")}
                      </span>{" "}
                      <span>
                        {activeJob.errorMessage ?? activeJob.errorSummary}
                      </span>
                    </dd>
                  </div>
                ) : null}
              </dl>

              {/* Expandable JSON views */}
              <div
                className="stack-sm"
                style={{
                  marginTop: "var(--space-3)",
                  background: "var(--color-surface-muted)",
                  padding: "var(--space-4)",
                  borderRadius: "var(--radius-md)",
                }}
              >
                {"payload" in activeJob &&
                (activeJob as { payload?: unknown }).payload ? (
                  <details style={{ marginBottom: "var(--space-3)" }}>
                    <summary
                      style={{
                        cursor: "pointer",
                        fontWeight: "var(--font-semibold)",
                      }}
                    >
                      {t("runs.inspector.payload")}
                    </summary>
                    <pre
                      className="mono"
                      style={{
                        marginTop: "var(--space-2)",
                        maxHeight: "300px",
                        overflow: "auto",
                        fontSize: "var(--text-xs)",
                        padding: "var(--space-3)",
                        background: "var(--color-surface)",
                        borderRadius: "var(--radius-sm)",
                      }}
                    >
                      {JSON.stringify(
                        (activeJob as { payload?: unknown }).payload,
                        null,
                        2,
                      )}
                    </pre>
                  </details>
                ) : null}

                <details open={Boolean(activeJob.result)}>
                  <summary
                    style={{
                      cursor: "pointer",
                      fontWeight: "var(--font-semibold)",
                    }}
                  >
                    {t("runs.inspector.result")}
                  </summary>
                  <pre
                    className="mono"
                    style={{
                      marginTop: "var(--space-2)",
                      maxHeight: "350px",
                      overflow: "auto",
                      fontSize: "var(--text-xs)",
                      padding: "var(--space-3)",
                      background: "var(--color-surface)",
                      borderRadius: "var(--radius-sm)",
                    }}
                  >
                    {activeJob.result
                      ? JSON.stringify(activeJob.result, null, 2)
                      : t("runs.inspector.noResult")}
                  </pre>
                </details>
              </div>
            </div>
          ) : null}
        </Card>

        {/* Card 2: Recent Jobs in Session */}
        <Card
          title={t("runs.recent.title")}
          description={t("runs.recent.description")}
        >
          {recentJobs.length === 0 ? (
            <EmptyState
              title={t("runs.empty.title")}
              description={t("runs.empty.description")}
            />
          ) : (
            <Table
              columns={recentColumns}
              rows={recentJobs}
              rowKey={(row) => row.id}
              caption={t("runs.table.caption")}
            />
          )}
        </Card>
      </div>

      {/* Confirmation Dialog for Cancellation */}
      <Dialog
        open={cancelDialogOpen}
        title={t("runs.cancel.confirmTitle")}
        description={
          activeJob
            ? t("runs.cancel.confirmMessage", { id: activeJob.id })
            : undefined
        }
        onClose={() => setCancelDialogOpen(false)}
      >
        <div className="dialog-form__actions">
          <Button
            variant="secondary"
            onClick={() => setCancelDialogOpen(false)}
            data-autofocus
            disabled={cancelling}
          >
            {t("action.cancel")}
          </Button>
          <Button
            variant="danger"
            onClick={handleConfirmCancel}
            loading={cancelling}
            loadingLabel={t("runs.cancel.cancelling")}
          >
            {t("runs.cancel.confirmButton")}
          </Button>
        </div>
      </Dialog>
    </>
  );
}
