import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FormEvent,
} from "react";
import { isApiRequestError, type ApiError } from "../api/client";
import {
  getProject,
  getProjectReadiness,
  updateProject,
  type ProjectDetail,
  type ProjectReadiness,
} from "../api/projects";
import {
  getProjectSources,
  type ProjectSourcesAndClaimsDto,
} from "../api/research";
import { Badge, StatusBadge, type BadgeTone } from "../components/Badge";
import { Button } from "../components/Button";
import { Card } from "../components/Card";
import { Dialog } from "../components/Dialog";
import { InlineError } from "../components/InlineError";
import { Skeleton } from "../components/Skeleton";
import { useToast } from "../components/Toast";
import { useFormatters } from "../i18n/format";
import { useI18n, type Translate } from "../i18n/i18n";
import { AssetsAudioSection } from "./projectDetail/AssetsAudioSection";
import { MetricsSection } from "./projectDetail/MetricsSection";
import { ProductionSection } from "./projectDetail/ProductionSection";
import { PublishSection } from "./projectDetail/PublishSection";
import { ScriptReviewSection } from "./projectDetail/ScriptReviewSection";

const MAX_TITLE = 200;
const MAX_BRIEF = 4_000;
const READINESS_CODES = new Set([
  "readiness_render_missing",
  "readiness_qa_failed",
  "readiness_final_approval_missing",
  "readiness_final_approval_invalid",
  "readiness_final_approval_changes_requested",
  "readiness_publication_missing",
]);
type SectionTab = "script" | "assets" | "production" | "publish" | "metrics";
const SECTION_TABS: readonly SectionTab[] = [
  "script",
  "assets",
  "production",
  "publish",
  "metrics",
] as const;

type LoadState = "loading" | "ready" | "error";

interface FieldErrors {
  title?: string;
  brief?: string;
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

function readinessTone(status: string): BadgeTone {
  if (status === "ready_to_publish") {
    return "success";
  }

  if (status === "blocked") {
    return "danger";
  }

  if (status === "qa_passed") {
    return "accent";
  }

  return "neutral";
}

function readinessLabel(status: string, t: Translate): string {
  if (
    status === "not_started" ||
    status === "in_progress" ||
    status === "qa_passed" ||
    status === "ready_to_publish" ||
    status === "blocked"
  ) {
    return t(`project.readiness.status.${status}`);
  }

  return t("project.readiness.status.unknown");
}

function reasonText(code: string, message: string, t: Translate): string {
  return READINESS_CODES.has(code) ? t(`project.readiness.reason.${code}`) : message;
}

interface ProjectDetailScreenProps {
  projectId: string;
  onBack: () => void;
}

export function ProjectDetailScreen({ projectId, onBack }: ProjectDetailScreenProps) {
  const { t } = useI18n();
  const { dateTime } = useFormatters();
  const { showToast } = useToast();

  const [detail, setDetail] = useState<ProjectDetail | null>(null);
  const [readiness, setReadiness] = useState<ProjectReadiness | null>(null);
  const [sources, setSources] = useState<ProjectSourcesAndClaimsDto | null>(null);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [activeSection, setActiveSection] = useState<SectionTab>("script");

  const detailRef = useRef<ProjectDetail | null>(null);
  detailRef.current = detail;
  const prevProjectIdRef = useRef(projectId);

  const [editOpen, setEditOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [brief, setBrief] = useState("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<ApiError | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const titleInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const controller = new AbortController();
    const isNewProject = prevProjectIdRef.current !== projectId;
    prevProjectIdRef.current = projectId;

    if (isNewProject || detailRef.current === null) {
      setLoadState("loading");
      setLoadError(null);
      if (isNewProject) {
        setDetail(null);
        setReadiness(null);
      }
    }

    Promise.all([
      getProject(projectId, controller.signal),
      getProjectReadiness(projectId, controller.signal),
      getProjectSources(projectId, controller.signal).catch(() => null),
    ])
      .then(([nextDetail, nextReadiness, nextSources]) => {
        if (controller.signal.aborted) {
          return;
        }

        setDetail(nextDetail);
        setReadiness(nextReadiness);
        setSources(nextSources);
        setLoadState("ready");
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        if (detailRef.current === null) {
          setLoadError(toApiError(error));
          setLoadState("error");
        }
      });

    return () => controller.abort();
  }, [projectId, reloadToken]);

  const refresh = useCallback(() => {
    setReloadToken((token) => token + 1);
  }, []);

  const openEdit = useCallback(() => {
    if (!detail) {
      return;
    }

    setTitle(detail.title);
    setBrief(detail.brief ?? "");
    setFieldErrors({});
    setFormError(null);
    setSubmitting(false);
    setEditOpen(true);
  }, [detail]);

  const closeEdit = useCallback(() => {
    if (submitting) {
      return;
    }

    setEditOpen(false);
    setFieldErrors({});
    setFormError(null);
  }, [submitting]);

  const handleSave = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (submitting) {
      return;
    }

    const normalizedTitle = title.trim();
    const normalizedBrief = brief.trim();
    const nextErrors: FieldErrors = {};

    if (!normalizedTitle) {
      nextErrors.title = t("projects.form.titleRequired");
    } else if (normalizedTitle.length > MAX_TITLE) {
      nextErrors.title = t("projects.form.titleTooLong", { max: MAX_TITLE });
    }

    if (normalizedBrief.length > MAX_BRIEF) {
      nextErrors.brief = t("projects.form.briefTooLong", { max: MAX_BRIEF });
    }

    setFieldErrors(nextErrors);
    setFormError(null);

    if (Object.keys(nextErrors).length > 0) {
      titleInputRef.current?.focus();
      return;
    }

    setSubmitting(true);

    try {
      const updated = await updateProject(projectId, {
        title: normalizedTitle,
        brief: normalizedBrief ? normalizedBrief : null,
      });

      setDetail(updated);
      setSubmitting(false);
      setEditOpen(false);
      showToast(t("project.toast.saved"), "success");

      getProjectReadiness(projectId)
        .then(setReadiness)
        .catch(() => undefined);
    } catch (error: unknown) {
      setSubmitting(false);
      const normalized = toApiError(error);
      const serverErrors = normalized.fieldErrors ?? {};
      const titleServerError =
        serverErrors.title?.[0] ?? serverErrors.Title?.[0];
      const briefServerError =
        serverErrors.brief?.[0] ?? serverErrors.Brief?.[0];

      if (titleServerError || briefServerError) {
        setFieldErrors({
          ...(titleServerError ? { title: t("projects.form.titleInvalid") } : {}),
          ...(briefServerError ? { brief: t("projects.form.briefInvalid") } : {}),
        });

        if (titleServerError) {
          titleInputRef.current?.focus();
        }

        return;
      }

      setFormError(normalized);
    }
  };

  return (
    <>
      <div className="detail-topbar">
        <button type="button" className="link-button" onClick={onBack}>
          {t("project.back")}
        </button>
      </div>

      {loadState === "loading" && detail === null ? <DetailSkeleton /> : null}

      {loadState === "error" && detail === null && loadError ? (
        <InlineError
          title={t("project.error.title")}
          detail={describeLoadError(loadError, t)}
          code={errorCode(loadError)}
          actionLabel={t("action.retry")}
          onAction={refresh}
        />
      ) : null}

      {detail ? (
        <div className="stack">
          <div className="detail-header">
            <div className="detail-header__main">
              <h1 className="page-header__title">{detail.title}</h1>
              <div className="detail-header__meta">
                <StatusBadge status={detail.status} />
                <span>
                  {t("project.updated")}:{" "}
                  <time dateTime={detail.updatedAt}>{dateTime(detail.updatedAt)}</time>
                </span>
              </div>
            </div>
            <div className="page-header__actions">
              <Button variant="secondary" onClick={openEdit}>
                {t("action.edit")}
              </Button>
            </div>
          </div>

          <div className="detail-grid">
            <Card
              title={t("project.brief.title")}
              description={t("project.brief.description")}
            >
              {detail.brief ? (
                <p className="detail-prose">{detail.brief}</p>
              ) : (
                <p className="inline-note">{t("project.brief.empty")}</p>
              )}
            </Card>

            <Card
              title={t("project.readiness.title")}
              description={t("project.readiness.description")}
            >
              {readiness ? (
                <div className="stack-sm">
                  <div className="readiness-summary">
                    <Badge tone={readinessTone(readiness.status)}>
                      {readinessLabel(readiness.status, t)}
                    </Badge>
                  </div>
                  {readiness.reasons.length > 0 ? (
                    <ul className="readiness-list">
                      {readiness.reasons.map((reason) => (
                        <li
                          key={reason.code}
                          className={
                            reason.blocking
                              ? "readiness-item readiness-item--blocking"
                              : "readiness-item"
                          }
                        >
                          <p className="readiness-item__text">
                            {reasonText(reason.code, reason.message, t)}
                          </p>
                          <p className="readiness-item__code">{reason.code}</p>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p className="inline-note">{t("project.readiness.clear")}</p>
                  )}
                </div>
              ) : null}
            </Card>

            {detail.lastJob ? (
              <Card title={t("project.lastJob.title")}>
                <dl className="definition-list">
                  <div>
                    <dt>{t("project.lastJob.type")}</dt>
                    <dd className="mono">{detail.lastJob.type}</dd>
                  </div>
                  <div>
                    <dt>{t("project.lastJob.status")}</dt>
                    <dd>
                      <StatusBadge status={detail.lastJob.status} />
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.lastJob.updated")}</dt>
                    <dd>
                      <time dateTime={detail.lastJob.updatedAt}>
                        {dateTime(detail.lastJob.updatedAt)}
                      </time>
                    </dd>
                  </div>
                </dl>
              </Card>
            ) : null}

            {sources && sources.sources.length > 0 ? (
              <Card title={t("project.sources.title")}>
                <div className="stack-sm">
                  <p className="inline-note">
                    {t("project.sources.summary", {
                      sources: sources.sources.length,
                      claims: sources.claims.length,
                    })}
                  </p>
                  <ul className="stack-sm" style={{ listStyle: "none", padding: 0, margin: 0 }}>
                    {sources.sources.slice(0, 4).map((src) => (
                      <li key={src.id} className="kv-card">
                        <a
                          href={src.url}
                          target="_blank"
                          rel="noreferrer"
                          className="link"
                        >
                          {src.title}
                        </a>
                        <div className="row-actions">
                          <Badge tone="neutral">{src.sourceType}</Badge>
                          <Badge tone="accent">{dateTime(src.retrievedAt)}</Badge>
                        </div>
                      </li>
                    ))}
                  </ul>
                </div>
              </Card>
            ) : null}
          </div>

          <div
            className="section-tabs"
            role="tablist"
            aria-label={t("project.sections.ariaLabel")}
          >
            {SECTION_TABS.map((tab) => {
              const isSelected = activeSection === tab;
              return (
                <button
                  key={tab}
                  type="button"
                  role="tab"
                  id={`tab-${tab}`}
                  aria-selected={isSelected}
                  aria-controls={`tabpanel-${tab}`}
                  className={
                    isSelected
                      ? "section-tabs__tab section-tabs__tab--active"
                      : "section-tabs__tab"
                  }
                  onClick={() => setActiveSection(tab)}
                >
                  {t(`project.next.${tab}`)}
                </button>
              );
            })}
          </div>

          <div
            role="tabpanel"
            id={`tabpanel-${activeSection}`}
            aria-labelledby={`tab-${activeSection}`}
            className="section-tabs__panel"
          >
            {activeSection === "script" && (
              <ScriptReviewSection
                projectId={projectId}
                lastJob={detail.lastJob}
                onProjectChanged={refresh}
              />
            )}
            {activeSection === "assets" && (
              <AssetsAudioSection
                projectId={projectId}
                onProjectChanged={refresh}
              />
            )}
            {activeSection === "production" && (
              <ProductionSection
                projectId={projectId}
                lastJob={detail.lastJob}
                onProjectChanged={refresh}
              />
            )}
            {activeSection === "publish" && (
              <PublishSection
                projectId={projectId}
                projectStatus={detail.status}
                lastJob={detail.lastJob}
                onProjectChanged={refresh}
              />
            )}
            {activeSection === "metrics" && (
              <MetricsSection
                projectId={projectId}
                onProjectChanged={refresh}
              />
            )}
          </div>
        </div>
      ) : null}

      <Dialog
        open={editOpen}
        title={t("project.edit.title")}
        description={t("project.edit.description")}
        onClose={closeEdit}
        dismissible={!submitting}
      >
        <form className="form-stack" onSubmit={handleSave} noValidate>
          {formError ? (
            <InlineError
              title={t("project.edit.errorTitle")}
              detail={t("project.edit.errorDescription")}
              code={errorCode(formError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="project-edit-title">
              {t("projects.form.title")}
            </label>
            <input
              ref={titleInputRef}
              id="project-edit-title"
              data-autofocus
              className="field__input"
              type="text"
              value={title}
              autoComplete="off"
              aria-invalid={fieldErrors.title ? true : undefined}
              aria-describedby={
                fieldErrors.title ? "project-edit-title-error" : "project-edit-title-hint"
              }
              onChange={(event) => setTitle(event.target.value)}
            />
            {fieldErrors.title ? (
              <p className="field__error" id="project-edit-title-error" role="alert">
                {fieldErrors.title}
              </p>
            ) : (
              <p className="field__hint" id="project-edit-title-hint">
                {t("projects.form.titleHint", { max: MAX_TITLE })}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="project-edit-brief">
              {t("projects.form.brief")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <textarea
              id="project-edit-brief"
              className="field__textarea"
              value={brief}
              rows={4}
              aria-invalid={fieldErrors.brief ? true : undefined}
              aria-describedby={
                fieldErrors.brief ? "project-edit-brief-error" : "project-edit-brief-hint"
              }
              onChange={(event) => setBrief(event.target.value)}
            />
            {fieldErrors.brief ? (
              <p className="field__error" id="project-edit-brief-error" role="alert">
                {fieldErrors.brief}
              </p>
            ) : (
              <p className="field__hint" id="project-edit-brief-hint">
                {t("projects.form.briefHint")}
              </p>
            )}
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeEdit}
              disabled={submitting}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submitting}
              loadingLabel={t("project.edit.submitting")}
            >
              {t("project.edit.submit")}
            </Button>
          </div>
        </form>
      </Dialog>
    </>
  );
}

function DetailSkeleton() {
  const { t } = useI18n();

  return (
    <div role="status" aria-busy="true">
      <span className="visually-hidden">{t("project.loading")}</span>
      <div className="detail-skeleton" aria-hidden="true">
        <Skeleton width="44%" height={28} />
        <Skeleton width="30%" height={14} />
        <Skeleton width="100%" height={120} />
        <Skeleton width="100%" height={120} />
      </div>
    </div>
  );
}
