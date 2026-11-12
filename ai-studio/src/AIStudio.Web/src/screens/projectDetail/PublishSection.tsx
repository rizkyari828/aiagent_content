import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FormEvent,
} from "react";
import { isApiRequestError, type ApiError } from "../../api/client";
import {
  createProjectExport,
  getFinalApproval,
  getPublication,
  listProjectExports,
  recordFinalApproval,
  recordPublication,
  transitionProjectToReady,
  type FinalApprovalDecision,
  type FinalApprovalStatusResponse,
  type ProjectExportMetadataResponse,
  type PublicationRecordResponse,
  type TransitionReadinessReason,
} from "../../api/publishing";
import type { ProjectLastJob } from "../../api/projects";
import { Badge, StatusBadge, type BadgeTone } from "../../components/Badge";
import { Button } from "../../components/Button";
import { Card } from "../../components/Card";
import { Dialog } from "../../components/Dialog";
import { EmptyState } from "../../components/EmptyState";
import { InlineError } from "../../components/InlineError";
import { Skeleton } from "../../components/Skeleton";
import { Table, type TableColumn } from "../../components/Table";
import { useToast } from "../../components/Toast";
import { useFormatters } from "../../i18n/format";
import { useI18n, type Translate } from "../../i18n/i18n";

export interface PublishSectionProps {
  projectId: string;
  projectStatus: string;
  lastJob: ProjectLastJob | null;
  onProjectChanged: () => void;
}

type LoadState = "loading" | "ready" | "error";

interface ApprovalFormState {
  sourceRenderJobId: string;
  destinationPlatform: string;
  decision: FinalApprovalDecision;
  note: string;
  approvedBy: string;
}

interface ApprovalFormErrors {
  sourceRenderJobId?: string;
  destinationPlatform?: string;
  note?: string;
}

interface PublicationFormState {
  platform: string;
  url: string;
  videoId: string;
  publishedAt: string;
  notes: string;
}

interface PublicationFormErrors {
  platform?: string;
  url?: string;
  videoId?: string;
  publishedAt?: string;
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

function describeLoadError(error: ApiError, t: Translate): string {
  if (error.code === "network_error") {
    return t("project.error.network");
  }

  if (error.code === "request_timeout") {
    return t("project.error.timeout");
  }

  if (error.code === "content_project_not_found") {
    return t("project.error.notFound");
  }

  return t("project.error.http");
}

function errorCode(error: ApiError): string | undefined {
  if (error.code) {
    return error.code;
  }

  return error.status > 0 ? `HTTP ${error.status}` : undefined;
}

function approvalStatusTone(status: string): BadgeTone {
  switch (status) {
    case "approved":
      return "success";
    case "changes_requested":
      return "warning";
    case "invalidated":
      return "danger";
    case "missing":
    default:
      return "neutral";
  }
}

function approvalStatusLabel(status: string, t: Translate): string {
  switch (status) {
    case "approved":
    case "changes_requested":
    case "invalidated":
    case "missing":
      return t(`project.publish.approval.status.${status}`);
    default:
      return t("project.publish.approval.status.unknown");
  }
}

function toLocalDatetimeString(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  const year = date.getFullYear();
  const month = pad(date.getMonth() + 1);
  const day = pad(date.getDate());
  const hours = pad(date.getHours());
  const minutes = pad(date.getMinutes());
  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function toIsoTimestamp(localValue: string): string {
  const date = new Date(localValue);
  if (Number.isNaN(date.getTime())) {
    return new Date().toISOString();
  }
  return date.toISOString();
}

function isValidHttpUrl(candidate: string): boolean {
  try {
    const url = new URL(candidate);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

export function PublishSection({
  projectId,
  projectStatus,
  lastJob,
  onProjectChanged,
}: PublishSectionProps) {
  const { t } = useI18n();
  const { dateTime, number } = useFormatters();
  const { showToast } = useToast();

  const [approvalStatus, setApprovalStatus] =
    useState<FinalApprovalStatusResponse | null>(null);
  const [publication, setPublication] =
    useState<PublicationRecordResponse | null>(null);
  const [exportsList, setExportsList] = useState<
    ProjectExportMetadataResponse[]
  >([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  // Transition state
  const [transitioning, setTransitioning] = useState(false);
  const [transitionReasons, setTransitionReasons] = useState<
    TransitionReadinessReason[]
  >([]);

  // Export creation state
  const [creatingExport, setCreatingExport] = useState(false);

  // Approval Form & Dialog state
  const [approvalDialogOpen, setApprovalDialogOpen] = useState(false);
  const [confirmApprovalOpen, setConfirmApprovalOpen] = useState(false);
  const [submittingApproval, setSubmittingApproval] = useState(false);
  const [approvalFormError, setApprovalFormError] = useState<ApiError | null>(
    null,
  );
  const [approvalForm, setApprovalForm] = useState<ApprovalFormState>({
    sourceRenderJobId: "",
    destinationPlatform: "YouTube",
    decision: "approved",
    note: "",
    approvedBy: "",
  });
  const [approvalFieldErrors, setApprovalFieldErrors] =
    useState<ApprovalFormErrors>({});
  const renderJobInputRef = useRef<HTMLInputElement>(null);

  // Publication Form & Dialog state
  const [publicationDialogOpen, setPublicationDialogOpen] = useState(false);
  const [submittingPublication, setSubmittingPublication] = useState(false);
  const [publicationFormError, setPublicationFormError] =
    useState<ApiError | null>(null);
  const [publicationForm, setPublicationForm] = useState<PublicationFormState>({
    platform: "YouTube",
    url: "",
    videoId: "",
    publishedAt: toLocalDatetimeString(),
    notes: "",
  });
  const [publicationFieldErrors, setPublicationFieldErrors] =
    useState<PublicationFormErrors>({});
  const platformInputRef = useRef<HTMLInputElement>(null);

  // Initial and refreshed data loading
  useEffect(() => {
    const controller = new AbortController();
    setLoadState("loading");
    setLoadError(null);

    Promise.all([
      getFinalApproval(projectId, controller.signal),
      getPublication(projectId, controller.signal).catch((err: unknown) => {
        if (
          isApiRequestError(err) &&
          (err.status === 404 || err.code === "publication_not_found")
        ) {
          return null;
        }
        throw err;
      }),
      listProjectExports(projectId, controller.signal),
    ])
      .then(([nextApproval, nextPublication, nextExports]) => {
        if (controller.signal.aborted) {
          return;
        }
        setApprovalStatus(nextApproval);
        setPublication(nextPublication);
        setExportsList(nextExports);
        setLoadState("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) {
          return;
        }
        setLoadError(toApiError(err));
        setLoadState("error");
      });

    return () => controller.abort();
  }, [projectId, refreshToken]);

  const refresh = useCallback(() => {
    setRefreshToken((token) => token + 1);
  }, []);

  // --- Handlers: A3 Final Approval ---

  const openApprovalDialog = useCallback(() => {
    const defaultRenderJobId =
      lastJob?.type === "RenderVideo" && lastJob.status === "Succeeded"
        ? lastJob.id
        : approvalStatus?.approval?.sourceRenderJobId ?? "";

    const defaultPlatform =
      approvalStatus?.approval?.destinationPlatform || "YouTube";

    const defaultDecision =
      approvalStatus?.approval?.decision ?? "approved";

    const defaultNote =
      approvalStatus?.approval?.decisionNote ??
      approvalStatus?.approval?.notes ??
      "";

    const defaultApprovedBy =
      approvalStatus?.approval?.approvedBy ?? "";

    setApprovalForm({
      sourceRenderJobId: defaultRenderJobId,
      destinationPlatform: defaultPlatform,
      decision: defaultDecision,
      note: defaultNote,
      approvedBy: defaultApprovedBy,
    });
    setApprovalFieldErrors({});
    setApprovalFormError(null);
    setApprovalDialogOpen(true);
  }, [approvalStatus, lastJob]);

  const closeApprovalDialog = useCallback(() => {
    if (submittingApproval) {
      return;
    }
    setApprovalDialogOpen(false);
    setApprovalFieldErrors({});
    setApprovalFormError(null);
  }, [submittingApproval]);

  const handleApprovalFormSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const normalizedRenderJobId = approvalForm.sourceRenderJobId.trim();
    const normalizedPlatform = approvalForm.destinationPlatform.trim();
    const normalizedNote = approvalForm.note.trim();
    const errors: ApprovalFormErrors = {};

    if (!normalizedRenderJobId) {
      errors.sourceRenderJobId = t("project.publish.approval.renderJobRequired");
    }

    if (!normalizedPlatform) {
      errors.destinationPlatform = t(
        "project.publish.approval.platformRequired",
      );
    }

    if (approvalForm.decision === "changes_requested" && !normalizedNote) {
      errors.note = t("project.publish.approval.noteRequired");
    }

    setApprovalFieldErrors(errors);
    setApprovalFormError(null);

    if (Object.keys(errors).length > 0) {
      if (errors.sourceRenderJobId) {
        renderJobInputRef.current?.focus();
      }
      return;
    }

    // Pass preliminary validation: open Confirmation Dialog
    setConfirmApprovalOpen(true);
  };

  const handleExecuteApproval = async () => {
    if (submittingApproval) {
      return;
    }

    setSubmittingApproval(true);
    setApprovalFormError(null);

    try {
      const normalizedRenderJobId = approvalForm.sourceRenderJobId.trim();
      const normalizedPlatform = approvalForm.destinationPlatform.trim();
      const normalizedNote = approvalForm.note.trim();
      const normalizedApprovedBy = approvalForm.approvedBy.trim();

      await recordFinalApproval(projectId, {
        sourceRenderJobId: normalizedRenderJobId,
        destinationPlatform: normalizedPlatform,
        decision: approvalForm.decision,
        note: normalizedNote || undefined,
        notes: normalizedNote || undefined,
        approvedBy: normalizedApprovedBy || undefined,
      });

      setSubmittingApproval(false);
      setConfirmApprovalOpen(false);
      setApprovalDialogOpen(false);
      showToast(t("project.publish.approval.recordSuccess"), "success");
      onProjectChanged();
      refresh();
    } catch (err: unknown) {
      setSubmittingApproval(false);
      const apiErr = toApiError(err);
      setApprovalFormError(apiErr);
    }
  };

  // --- Handlers: Transition to Ready ---

  const handleTransition = async () => {
    if (transitioning) {
      return;
    }

    setTransitioning(true);
    setTransitionReasons([]);

    try {
      const result = await transitionProjectToReady(projectId);
      setTransitioning(false);

      if (result.transitioned) {
        showToast(t("project.publish.transitionSuccess"), "success");
        setTransitionReasons([]);
        onProjectChanged();
        refresh();
      } else {
        setTransitionReasons(result.reasons);
        showToast(t("project.publish.transitionBlocked"), "error");
      }
    } catch (err: unknown) {
      setTransitioning(false);
      const apiErr = toApiError(err);
      showToast(
        apiErr.detail ?? apiErr.title ?? t("project.publish.transitionBlocked"),
        "error",
      );
    }
  };

  // --- Handlers: Publication Record ---

  const openPublicationDialog = useCallback(() => {
    setPublicationForm({
      platform: "YouTube",
      url: "",
      videoId: "",
      publishedAt: toLocalDatetimeString(),
      notes: "",
    });
    setPublicationFieldErrors({});
    setPublicationFormError(null);
    setPublicationDialogOpen(true);
  }, []);

  const closePublicationDialog = useCallback(() => {
    if (submittingPublication) {
      return;
    }
    setPublicationDialogOpen(false);
    setPublicationFieldErrors({});
    setPublicationFormError(null);
  }, [submittingPublication]);

  const handlePublicationSubmit = async (
    event: FormEvent<HTMLFormElement>,
  ) => {
    event.preventDefault();

    if (submittingPublication) {
      return;
    }

    const normalizedPlatform = publicationForm.platform.trim();
    const normalizedUrl = publicationForm.url.trim();
    const normalizedVideoId = publicationForm.videoId.trim();
    const normalizedPublishedAt = publicationForm.publishedAt.trim();
    const normalizedNotes = publicationForm.notes.trim();

    const errors: PublicationFormErrors = {};

    if (!normalizedPlatform) {
      errors.platform = t("project.publish.publication.platformRequired");
    }

    if (!normalizedUrl) {
      errors.url = t("project.publish.publication.urlRequired");
    } else if (!isValidHttpUrl(normalizedUrl)) {
      errors.url = t("project.publish.publication.urlInvalid");
    }

    if (!normalizedVideoId) {
      errors.videoId = t("project.publish.publication.videoIdRequired");
    }

    if (!normalizedPublishedAt) {
      errors.publishedAt = t(
        "project.publish.publication.publishedAtRequired",
      );
    } else if (Number.isNaN(new Date(normalizedPublishedAt).getTime())) {
      errors.publishedAt = t(
        "project.publish.publication.publishedAtInvalid",
      );
    }

    setPublicationFieldErrors(errors);
    setPublicationFormError(null);

    if (Object.keys(errors).length > 0) {
      if (errors.platform) {
        platformInputRef.current?.focus();
      }
      return;
    }

    setSubmittingPublication(true);

    try {
      await recordPublication(projectId, {
        platform: normalizedPlatform,
        url: normalizedUrl,
        videoId: normalizedVideoId,
        publishedAt: toIsoTimestamp(normalizedPublishedAt),
        notes: normalizedNotes || null,
      });

      setSubmittingPublication(false);
      setPublicationDialogOpen(false);
      showToast(t("project.publish.publication.recordSuccess"), "success");
      onProjectChanged();
      refresh();
    } catch (err: unknown) {
      setSubmittingPublication(false);
      setPublicationFormError(toApiError(err));
    }
  };

  // --- Handlers: Export Archives ---

  const handleCreateExport = async () => {
    if (creatingExport) {
      return;
    }

    setCreatingExport(true);

    try {
      await createProjectExport(projectId);
      setCreatingExport(false);
      showToast(t("project.publish.exports.createSuccess"), "success");
      refresh();
    } catch (err: unknown) {
      setCreatingExport(false);
      const apiErr = toApiError(err);
      showToast(
        apiErr.detail ?? apiErr.title ?? t("project.publish.exports.createError"),
        "error",
      );
    }
  };

  const exportColumns: readonly TableColumn<ProjectExportMetadataResponse>[] = [
    {
      key: "fileName",
      header: t("project.publish.exports.columnFileName"),
      render: (row) => <span className="mono">{row.fileName}</span>,
    },
    {
      key: "byteSize",
      header: t("project.publish.exports.columnSize"),
      align: "end",
      render: (row) =>
        t("project.publish.exports.bytes", { size: number(row.byteSize) }),
    },
    {
      key: "sha256",
      header: t("project.publish.exports.columnSha256"),
      render: (row) => (
        <span className="mono">
          {row.sha256
            ? `${row.sha256.slice(0, 12)}…`
            : t("project.publish.notRecorded")}
        </span>
      ),
    },
    {
      key: "createdAtUtc",
      header: t("project.publish.exports.columnCreatedAt"),
      render: (row) => (
        <time dateTime={row.createdAtUtc}>{dateTime(row.createdAtUtc)}</time>
      ),
    },
  ];

  return (
    <section className="stack" aria-label={t("project.publish.title")}>
      {loadState === "loading" ? (
        <div role="status" aria-busy="true">
          <span className="visually-hidden">{t("project.publish.loading")}</span>
          <div className="detail-skeleton" aria-hidden="true">
            <Skeleton width="40%" height={24} />
            <Skeleton width="100%" height={120} />
            <Skeleton width="100%" height={120} />
          </div>
        </div>
      ) : null}

      {loadState === "error" && loadError ? (
        <InlineError
          title={t("project.publish.error.title")}
          detail={describeLoadError(loadError, t)}
          code={errorCode(loadError)}
          actionLabel={t("action.retry")}
          onAction={refresh}
        />
      ) : null}

      {loadState === "ready" ? (
        <div className="detail-grid">
          {/* Card 1: Final Approval (A3) */}
          <Card
            title={t("project.publish.approval.title")}
            description={t("project.publish.approval.description")}
            actions={
              <Button variant="secondary" size="sm" onClick={openApprovalDialog}>
                {t("project.publish.approval.recordAction")}
              </Button>
            }
          >
            {approvalStatus ? (
              <div className="stack-sm">
                <div className="readiness-summary">
                  <Badge tone={approvalStatusTone(approvalStatus.status)}>
                    {approvalStatusLabel(approvalStatus.status, t)}
                  </Badge>
                  <Badge tone={approvalStatus.isValid ? "success" : "danger"}>
                    {approvalStatus.isValid
                      ? t("project.publish.approval.valid")
                      : t("project.publish.approval.invalid")}
                  </Badge>
                </div>

                {approvalStatus.reasons.length > 0 ? (
                  <ul className="readiness-list">
                    {approvalStatus.reasons.map((reason, index) => (
                      <li
                        key={index}
                        className="readiness-item readiness-item--blocking"
                      >
                        <p className="readiness-item__text">{reason}</p>
                      </li>
                    ))}
                  </ul>
                ) : null}

                {approvalStatus.approval ? (
                  <dl className="definition-list">
                    <div>
                      <dt>{t("project.publish.approval.sourceRenderJobId")}</dt>
                      <dd className="mono">
                        {approvalStatus.approval.sourceRenderJobId}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("project.publish.approval.destinationPlatform")}</dt>
                      <dd>{approvalStatus.approval.destinationPlatform}</dd>
                    </div>
                    <div>
                      <dt>{t("project.publish.approval.decision")}</dt>
                      <dd>
                        <Badge
                          tone={
                            approvalStatus.approval.decision === "approved"
                              ? "success"
                              : "warning"
                          }
                        >
                          {t(
                            `project.publish.approval.decisionValue.${approvalStatus.approval.decision}`,
                          )}
                        </Badge>
                      </dd>
                    </div>
                    <div>
                      <dt>{t("project.publish.approval.decisionNote")}</dt>
                      <dd>
                        {approvalStatus.approval.decisionNote ||
                        approvalStatus.approval.notes ? (
                          approvalStatus.approval.decisionNote ||
                          approvalStatus.approval.notes
                        ) : (
                          <span className="inline-note">
                            {t("project.publish.approval.noNotes")}
                          </span>
                        )}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("project.publish.approval.approvedBy")}</dt>
                      <dd>
                        {approvalStatus.approval.approvedBy ? (
                          approvalStatus.approval.approvedBy
                        ) : (
                          <span className="inline-note">
                            {t("project.publish.approval.noApprover")}
                          </span>
                        )}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("project.publish.approval.revision")}</dt>
                      <dd className="mono">
                        #{approvalStatus.approval.revision}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("project.publish.approval.decidedAt")}</dt>
                      <dd>
                        <time dateTime={approvalStatus.approval.decidedAt}>
                          {dateTime(approvalStatus.approval.decidedAt)}
                        </time>
                      </dd>
                    </div>
                  </dl>
                ) : (
                  <p className="inline-note">
                    {t("project.publish.approval.noApprovalRecorded")}
                  </p>
                )}
              </div>
            ) : null}
          </Card>

          {/* Card 2: Transition to Ready to Publish */}
          <Card
            title={t("project.publish.transition.title")}
            description={t("project.publish.transition.description")}
          >
            <div className="stack-sm">
              <div className="readiness-summary">
                <StatusBadge status={projectStatus} />
                {projectStatus === "ready_to_publish" ? (
                  <span className="inline-note">
                    {t("project.publish.transition.alreadyReady")}
                  </span>
                ) : null}
                {projectStatus === "published" ? (
                  <span className="inline-note">
                    {t("project.publish.transition.alreadyPublished")}
                  </span>
                ) : null}
              </div>

              <div>
                <Button
                  variant="primary"
                  loading={transitioning}
                  loadingLabel={t("project.publish.transitionSubmitting")}
                  onClick={handleTransition}
                  disabled={
                    projectStatus === "ready_to_publish" ||
                    projectStatus === "published"
                  }
                >
                  {t("project.publish.transitionAction")}
                </Button>
              </div>

              {transitionReasons.length > 0 ? (
                <ul className="readiness-list">
                  {transitionReasons.map((reason) => (
                    <li
                      key={reason.code}
                      className={
                        reason.blocking
                          ? "readiness-item readiness-item--blocking"
                          : "readiness-item"
                      }
                    >
                      <p className="readiness-item__text">{reason.message}</p>
                      <p className="readiness-item__code">{reason.code}</p>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          </Card>

          {/* Card 3: Publication Record */}
          <Card
            title={t("project.publish.publication.title")}
            description={t("project.publish.publication.description")}
            actions={
              publication ? (
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={openPublicationDialog}
                >
                  {t("project.publish.publication.recordAction")}
                </Button>
              ) : undefined
            }
          >
            {publication ? (
              <dl className="definition-list">
                <div>
                  <dt>{t("project.publish.publication.platform")}</dt>
                  <dd>{publication.platform}</dd>
                </div>
                <div>
                  <dt>{t("project.publish.publication.url")}</dt>
                  <dd>
                    <a
                      href={publication.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="table__link"
                    >
                      {publication.url}
                    </a>
                  </dd>
                </div>
                <div>
                  <dt>{t("project.publish.publication.videoId")}</dt>
                  <dd className="mono">{publication.videoId}</dd>
                </div>
                <div>
                  <dt>{t("project.publish.publication.publishedAt")}</dt>
                  <dd>
                    <time dateTime={publication.publishedAt}>
                      {dateTime(publication.publishedAt)}
                    </time>
                  </dd>
                </div>
                <div>
                  <dt>
                    {t("project.publish.publication.finalApprovalRevision")}
                  </dt>
                  <dd className="mono">
                    #{publication.finalApprovalRevision}
                  </dd>
                </div>
                <div>
                  <dt>
                    {t("project.publish.publication.artifactContentHash")}
                  </dt>
                  <dd className="mono">{publication.artifactContentHash}</dd>
                </div>
                <div>
                  <dt>{t("project.publish.publication.notes")}</dt>
                  <dd>
                    {publication.notes ? (
                      publication.notes
                    ) : (
                      <span className="inline-note">
                        {t("project.publish.publication.noNotes")}
                      </span>
                    )}
                  </dd>
                </div>
              </dl>
            ) : (
              <EmptyState
                title={t("project.publish.publication.emptyTitle")}
                description={t("project.publish.publication.empty")}
                action={
                  <Button
                    variant="secondary"
                    onClick={openPublicationDialog}
                  >
                    {t("project.publish.publication.recordAction")}
                  </Button>
                }
              />
            )}
          </Card>

          {/* Card 4: Project Export Archive */}
          <Card
            title={t("project.publish.exports.title")}
            description={t("project.publish.exports.description")}
            actions={
              <Button
                variant="secondary"
                size="sm"
                loading={creatingExport}
                loadingLabel={t("project.publish.exports.creating")}
                onClick={handleCreateExport}
              >
                {t("project.publish.exports.create")}
              </Button>
            }
          >
            {exportsList.length > 0 ? (
              <Table
                columns={exportColumns}
                rows={exportsList}
                rowKey={(row) => row.fileName}
                caption={t("project.publish.exports.tableCaption")}
              />
            ) : (
              <p className="inline-note">
                {t("project.publish.exports.empty")}
              </p>
            )}
          </Card>
        </div>
      ) : null}

      {/* Dialog 1: Record A3 Decision Form */}
      <Dialog
        open={approvalDialogOpen}
        title={t("project.publish.approval.dialogTitle")}
        description={t("project.publish.approval.dialogDescription")}
        onClose={closeApprovalDialog}
        dismissible={!submittingApproval}
      >
        <form
          className="form-stack"
          onSubmit={handleApprovalFormSubmit}
          noValidate
        >
          {approvalFormError ? (
            <InlineError
              title={t("project.publish.approval.submitErrorTitle")}
              detail={approvalFormError.detail ?? approvalFormError.title}
              code={errorCode(approvalFormError)}
            />
          ) : null}

          <div className="field">
            <label
              className="field__label"
              htmlFor="approval-source-render-job-id"
            >
              {t("project.publish.approval.sourceRenderJobId")}
            </label>
            <input
              ref={renderJobInputRef}
              id="approval-source-render-job-id"
              data-autofocus
              className="field__input mono"
              type="text"
              value={approvalForm.sourceRenderJobId}
              autoComplete="off"
              aria-invalid={
                approvalFieldErrors.sourceRenderJobId ? true : undefined
              }
              aria-describedby={
                approvalFieldErrors.sourceRenderJobId
                  ? "approval-source-render-job-id-error"
                  : "approval-source-render-job-id-hint"
              }
              onChange={(e) =>
                setApprovalForm((prev) => ({
                  ...prev,
                  sourceRenderJobId: e.target.value,
                }))
              }
            />
            {approvalFieldErrors.sourceRenderJobId ? (
              <p
                className="field__error"
                id="approval-source-render-job-id-error"
              >
                {approvalFieldErrors.sourceRenderJobId}
              </p>
            ) : (
              <p className="field__hint" id="approval-source-render-job-id-hint">
                {t("project.publish.approval.renderJobIdHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label
              className="field__label"
              htmlFor="approval-destination-platform"
            >
              {t("project.publish.approval.destinationPlatform")}
            </label>
            <input
              id="approval-destination-platform"
              className="field__input"
              type="text"
              value={approvalForm.destinationPlatform}
              placeholder={t(
                "project.publish.approval.destinationPlatformPlaceholder",
              )}
              autoComplete="off"
              aria-invalid={
                approvalFieldErrors.destinationPlatform ? true : undefined
              }
              aria-describedby={
                approvalFieldErrors.destinationPlatform
                  ? "approval-destination-platform-error"
                  : "approval-destination-platform-hint"
              }
              onChange={(e) =>
                setApprovalForm((prev) => ({
                  ...prev,
                  destinationPlatform: e.target.value,
                }))
              }
            />
            {approvalFieldErrors.destinationPlatform ? (
              <p
                className="field__error"
                id="approval-destination-platform-error"
              >
                {approvalFieldErrors.destinationPlatform}
              </p>
            ) : (
              <p
                className="field__hint"
                id="approval-destination-platform-hint"
              >
                {t("project.publish.approval.destinationPlatformHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="approval-decision">
              {t("project.publish.approval.decision")}
            </label>
            <select
              id="approval-decision"
              className="select"
              value={approvalForm.decision}
              onChange={(e) =>
                setApprovalForm((prev) => ({
                  ...prev,
                  decision: e.target.value as FinalApprovalDecision,
                }))
              }
            >
              <option value="approved">
                {t("project.publish.approval.decisionValue.approved")}
              </option>
              <option value="changes_requested">
                {t("project.publish.approval.decisionValue.changes_requested")}
              </option>
            </select>
          </div>

          <div className="field">
            <label className="field__label" htmlFor="approval-note">
              {t("project.publish.approval.note")}
              {approvalForm.decision === "changes_requested" ? null : (
                <span className="field__optional">
                  {t("project.publish.approval.noteOptional")}
                </span>
              )}
            </label>
            <textarea
              id="approval-note"
              className="field__textarea"
              rows={3}
              value={approvalForm.note}
              aria-invalid={approvalFieldErrors.note ? true : undefined}
              aria-describedby={
                approvalFieldErrors.note
                  ? "approval-note-error"
                  : "approval-note-hint"
              }
              onChange={(e) =>
                setApprovalForm((prev) => ({ ...prev, note: e.target.value }))
              }
            />
            {approvalFieldErrors.note ? (
              <p className="field__error" id="approval-note-error">
                {approvalFieldErrors.note}
              </p>
            ) : (
              <p className="field__hint" id="approval-note-hint">
                {t("project.publish.approval.noteHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="approval-approved-by">
              {t("project.publish.approval.approvedBy")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="approval-approved-by"
              className="field__input"
              type="text"
              value={approvalForm.approvedBy}
              placeholder={t("project.publish.approval.approvedByPlaceholder")}
              autoComplete="off"
              aria-describedby="approval-approved-by-hint"
              onChange={(e) =>
                setApprovalForm((prev) => ({
                  ...prev,
                  approvedBy: e.target.value,
                }))
              }
            />
            <p className="field__hint" id="approval-approved-by-hint">
              {t("project.publish.approval.approvedByHint")}
            </p>
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeApprovalDialog}
              disabled={submittingApproval}
            >
              {t("action.cancel")}
            </Button>
            <Button type="submit" variant="primary">
              {t("project.publish.approval.submitAction")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Dialog 2: Confirmation Dialog Before Submitting A3 Approval */}
      <Dialog
        open={confirmApprovalOpen}
        title={t("project.publish.approval.confirmTitle")}
        description={t("project.publish.approval.confirmDescription")}
        onClose={() => {
          if (!submittingApproval) {
            setConfirmApprovalOpen(false);
          }
        }}
        dismissible={!submittingApproval}
      >
        <div className="stack">
          <dl className="definition-list">
            <div>
              <dt>{t("project.publish.approval.sourceRenderJobId")}</dt>
              <dd className="mono">{approvalForm.sourceRenderJobId}</dd>
            </div>
            <div>
              <dt>{t("project.publish.approval.destinationPlatform")}</dt>
              <dd>{approvalForm.destinationPlatform}</dd>
            </div>
            <div>
              <dt>{t("project.publish.approval.decision")}</dt>
              <dd>
                <Badge
                  tone={
                    approvalForm.decision === "approved" ? "success" : "warning"
                  }
                >
                  {t(
                    `project.publish.approval.decisionValue.${approvalForm.decision}`,
                  )}
                </Badge>
              </dd>
            </div>
            {approvalForm.note.trim() ? (
              <div>
                <dt>{t("project.publish.approval.note")}</dt>
                <dd>{approvalForm.note}</dd>
              </div>
            ) : null}
            {approvalForm.approvedBy.trim() ? (
              <div>
                <dt>{t("project.publish.approval.approvedBy")}</dt>
                <dd>{approvalForm.approvedBy}</dd>
              </div>
            ) : null}
          </dl>

          {approvalFormError ? (
            <InlineError
              title={t("project.publish.approval.submitErrorTitle")}
              detail={approvalFormError.detail ?? approvalFormError.title}
              code={errorCode(approvalFormError)}
            />
          ) : null}

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={() => setConfirmApprovalOpen(false)}
              disabled={submittingApproval}
            >
              {t("action.cancel")}
            </Button>
            <Button
              variant={
                approvalForm.decision === "approved" ? "primary" : "danger"
              }
              loading={submittingApproval}
              loadingLabel={t("project.publish.approval.submitting")}
              onClick={handleExecuteApproval}
            >
              {t("project.publish.approval.confirmSubmit")}
            </Button>
          </div>
        </div>
      </Dialog>

      {/* Dialog 3: Record Publication */}
      <Dialog
        open={publicationDialogOpen}
        title={t("project.publish.publication.dialogTitle")}
        description={t("project.publish.publication.dialogDescription")}
        onClose={closePublicationDialog}
        dismissible={!submittingPublication}
      >
        <form
          className="form-stack"
          onSubmit={handlePublicationSubmit}
          noValidate
        >
          {publicationFormError ? (
            <InlineError
              title={t("project.publish.publication.submitErrorTitle")}
              detail={publicationFormError.detail ?? publicationFormError.title}
              code={errorCode(publicationFormError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="publication-platform">
              {t("project.publish.publication.platform")}
            </label>
            <input
              ref={platformInputRef}
              id="publication-platform"
              data-autofocus
              className="field__input"
              type="text"
              value={publicationForm.platform}
              placeholder={t(
                "project.publish.publication.platformPlaceholder",
              )}
              autoComplete="off"
              aria-invalid={
                publicationFieldErrors.platform ? true : undefined
              }
              aria-describedby={
                publicationFieldErrors.platform
                  ? "publication-platform-error"
                  : "publication-platform-hint"
              }
              onChange={(e) =>
                setPublicationForm((prev) => ({
                  ...prev,
                  platform: e.target.value,
                }))
              }
            />
            {publicationFieldErrors.platform ? (
              <p className="field__error" id="publication-platform-error">
                {publicationFieldErrors.platform}
              </p>
            ) : (
              <p className="field__hint" id="publication-platform-hint">
                {t("project.publish.publication.platformHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="publication-url">
              {t("project.publish.publication.url")}
            </label>
            <input
              id="publication-url"
              className="field__input"
              type="url"
              value={publicationForm.url}
              placeholder={t("project.publish.publication.urlPlaceholder")}
              autoComplete="off"
              aria-invalid={publicationFieldErrors.url ? true : undefined}
              aria-describedby={
                publicationFieldErrors.url
                  ? "publication-url-error"
                  : "publication-url-hint"
              }
              onChange={(e) =>
                setPublicationForm((prev) => ({
                  ...prev,
                  url: e.target.value,
                }))
              }
            />
            {publicationFieldErrors.url ? (
              <p className="field__error" id="publication-url-error">
                {publicationFieldErrors.url}
              </p>
            ) : (
              <p className="field__hint" id="publication-url-hint">
                {t("project.publish.publication.urlHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="publication-video-id">
              {t("project.publish.publication.videoId")}
            </label>
            <input
              id="publication-video-id"
              className="field__input mono"
              type="text"
              value={publicationForm.videoId}
              placeholder={t(
                "project.publish.publication.videoIdPlaceholder",
              )}
              autoComplete="off"
              aria-invalid={
                publicationFieldErrors.videoId ? true : undefined
              }
              aria-describedby={
                publicationFieldErrors.videoId
                  ? "publication-video-id-error"
                  : "publication-video-id-hint"
              }
              onChange={(e) =>
                setPublicationForm((prev) => ({
                  ...prev,
                  videoId: e.target.value,
                }))
              }
            />
            {publicationFieldErrors.videoId ? (
              <p className="field__error" id="publication-video-id-error">
                {publicationFieldErrors.videoId}
              </p>
            ) : (
              <p className="field__hint" id="publication-video-id-hint">
                {t("project.publish.publication.videoIdHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="publication-published-at">
              {t("project.publish.publication.publishedAt")}
            </label>
            <input
              id="publication-published-at"
              className="field__input"
              type="datetime-local"
              value={publicationForm.publishedAt}
              aria-invalid={
                publicationFieldErrors.publishedAt ? true : undefined
              }
              aria-describedby={
                publicationFieldErrors.publishedAt
                  ? "publication-published-at-error"
                  : "publication-published-at-hint"
              }
              onChange={(e) =>
                setPublicationForm((prev) => ({
                  ...prev,
                  publishedAt: e.target.value,
                }))
              }
            />
            {publicationFieldErrors.publishedAt ? (
              <p className="field__error" id="publication-published-at-error">
                {publicationFieldErrors.publishedAt}
              </p>
            ) : (
              <p className="field__hint" id="publication-published-at-hint">
                {t("project.publish.publication.publishedAtHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="publication-notes">
              {t("project.publish.publication.notes")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <textarea
              id="publication-notes"
              className="field__textarea"
              rows={3}
              value={publicationForm.notes}
              aria-describedby="publication-notes-hint"
              onChange={(e) =>
                setPublicationForm((prev) => ({
                  ...prev,
                  notes: e.target.value,
                }))
              }
            />
            <p className="field__hint" id="publication-notes-hint">
              {t("project.publish.publication.notesHint")}
            </p>
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closePublicationDialog}
              disabled={submittingPublication}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingPublication}
              loadingLabel={t("project.publish.publication.submitting")}
            >
              {t("project.publish.publication.submitAction")}
            </Button>
          </div>
        </form>
      </Dialog>
    </section>
  );
}
