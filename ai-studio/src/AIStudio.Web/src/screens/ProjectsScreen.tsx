import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FormEvent,
} from "react";
import { API_BASE_URL_LABEL, isApiRequestError, type ApiError } from "../api/client";
import { createProject, listProjects, type ProjectSummary } from "../api/projects";
import { StatusBadge } from "../components/Badge";
import { Button } from "../components/Button";
import { Dialog } from "../components/Dialog";
import { EmptyState } from "../components/EmptyState";
import { InlineError } from "../components/InlineError";
import { Skeleton } from "../components/Skeleton";
import { Table, type TableColumn } from "../components/Table";
import { useToast } from "../components/Toast";
import { IconPlus } from "../components/icons";
import { useFormatters } from "../i18n/format";
import { useI18n, type Translate } from "../i18n/i18n";
import { routeToHash } from "../routing/useHashRoute";

const MAX_TITLE = 200;
const MAX_BRIEF = 4_000;
const TAKE = 100;
const SKELETON_ROWS = 5;

type LoadState = "loading" | "ready" | "error";
type DialogMode = "closed" | "form" | "discard";

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
    return t("projects.error.network", { url: API_BASE_URL_LABEL });
  }

  if (error.code === "request_timeout") {
    return t("projects.error.timeout");
  }

  return t("projects.error.http");
}

function errorCode(error: ApiError): string | undefined {
  if (error.code) {
    return error.code;
  }

  return error.status > 0 ? `HTTP ${error.status}` : undefined;
}

interface ProjectsScreenProps {
  onOpenProject: (projectId: string) => void;
}

export function ProjectsScreen({ onOpenProject }: ProjectsScreenProps) {
  const { t } = useI18n();
  const { dateTime } = useFormatters();
  const { showToast } = useToast();

  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const [dialogMode, setDialogMode] = useState<DialogMode>("closed");
  const [title, setTitle] = useState("");
  const [brief, setBrief] = useState("");
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<ApiError | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const titleInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const controller = new AbortController();

    setLoadState("loading");
    setLoadError(null);

    listProjects(TAKE, controller.signal)
      .then((items) => {
        if (controller.signal.aborted) {
          return;
        }

        setProjects(items);
        setLoadState("ready");
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setLoadError(toApiError(error));
        setLoadState("error");
      });

    return () => controller.abort();
  }, [reloadToken]);

  const refresh = useCallback(() => {
    setReloadToken((token) => token + 1);
  }, []);

  const openCreate = useCallback(() => {
    setTitle("");
    setBrief("");
    setFieldErrors({});
    setFormError(null);
    setSubmitting(false);
    setDialogMode("form");
  }, []);

  const closeDialog = useCallback(() => {
    setDialogMode("closed");
    setFieldErrors({});
    setFormError(null);
    setSubmitting(false);
  }, []);

  const requestClose = useCallback(() => {
    if (submitting) {
      return;
    }

    if (title.trim() !== "" || brief.trim() !== "") {
      setDialogMode("discard");
      return;
    }

    closeDialog();
  }, [brief, closeDialog, submitting, title]);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
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
      const created = await createProject({
        title: normalizedTitle,
        ...(normalizedBrief ? { brief: normalizedBrief } : {}),
      });

      setSubmitting(false);
      setDialogMode("closed");
      setTitle("");
      setBrief("");
      showToast(t("projects.toast.created", { title: created.title }), "success");
      refresh();
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

  const columns: readonly TableColumn<ProjectSummary>[] = [
    {
      key: "title",
      header: t("projects.table.title"),
      className: "table__title",
      render: (project) => project.title,
    },
    {
      key: "status",
      header: t("projects.table.status"),
      render: (project) => <StatusBadge status={project.status} />,
    },
    {
      key: "updated",
      header: t("projects.table.updated"),
      render: (project) => (
        <time className="table__updated" dateTime={project.updatedAt}>
          {dateTime(project.updatedAt)}
        </time>
      ),
    },
    {
      key: "open",
      header: <span className="visually-hidden">{t("projects.table.actions")}</span>,
      align: "end",
      render: (project) => (
        <a
          className="table__link"
          href={routeToHash({ screen: "project", projectId: project.id })}
          onClick={(event) => {
            event.preventDefault();
            onOpenProject(project.id);
          }}
        >
          {t("action.open")}
        </a>
      ),
    },
  ];

  return (
    <>
      <div className="page-header">
        <div className="page-header__text">
          <h1 className="page-header__title">{t("projects.title")}</h1>
          <p className="page-header__subtitle">{t("projects.subtitle")}</p>
        </div>
        <div className="page-header__actions">
          <Button variant="primary" onClick={openCreate}>
            <IconPlus size={16} />
            {t("action.newProject")}
          </Button>
        </div>
      </div>

      <div className="stack">
        {loadState === "loading" ? <ProjectsSkeleton /> : null}

        {loadState === "error" && loadError ? (
          <InlineError
            title={t("projects.error.title")}
            detail={describeLoadError(loadError, t)}
            code={errorCode(loadError)}
            actionLabel={t("action.retry")}
            onAction={refresh}
          />
        ) : null}

        {loadState === "ready" && projects.length === 0 ? (
          <EmptyState
            title={t("projects.empty.title")}
            description={t("projects.empty.description")}
            action={
              <Button variant="primary" onClick={openCreate}>
                {t("projects.empty.action")}
              </Button>
            }
          />
        ) : null}

        {loadState === "ready" && projects.length > 0 ? (
          <Table
            caption={t("projects.table.caption")}
            columns={columns}
            rows={projects}
            rowKey={(project) => project.id}
          />
        ) : null}
      </div>

      <Dialog
        key="create"
        open={dialogMode === "form"}
        title={t("projects.dialog.createTitle")}
        description={t("projects.dialog.createDescription")}
        onClose={requestClose}
        dismissible={!submitting}
      >
        <form className="form-stack" onSubmit={handleSubmit} noValidate>
          {formError ? (
            <InlineError
              title={t("projects.form.errorTitle")}
              detail={t("projects.form.errorDescription")}
              code={errorCode(formError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="project-title">
              {t("projects.form.title")}
            </label>
            <input
              ref={titleInputRef}
              id="project-title"
              data-autofocus
              className="field__input"
              type="text"
              value={title}
              placeholder={t("projects.form.titlePlaceholder")}
              autoComplete="off"
              aria-invalid={fieldErrors.title ? true : undefined}
              aria-describedby={
                fieldErrors.title ? "project-title-error" : "project-title-hint"
              }
              onChange={(event) => setTitle(event.target.value)}
            />
            {fieldErrors.title ? (
              <p className="field__error" id="project-title-error" role="alert">
                {fieldErrors.title}
              </p>
            ) : (
              <p className="field__hint" id="project-title-hint">
                {t("projects.form.titleHint", { max: MAX_TITLE })}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="project-brief">
              {t("projects.form.brief")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <textarea
              id="project-brief"
              className="field__textarea"
              value={brief}
              rows={4}
              placeholder={t("projects.form.briefPlaceholder")}
              aria-invalid={fieldErrors.brief ? true : undefined}
              aria-describedby={
                fieldErrors.brief ? "project-brief-error" : "project-brief-hint"
              }
              onChange={(event) => setBrief(event.target.value)}
            />
            {fieldErrors.brief ? (
              <p className="field__error" id="project-brief-error" role="alert">
                {fieldErrors.brief}
              </p>
            ) : (
              <p className="field__hint" id="project-brief-hint">
                {t("projects.form.briefHint")}
              </p>
            )}
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={requestClose}
              disabled={submitting}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submitting}
              loadingLabel={t("projects.form.submitting")}
            >
              {t("projects.form.submit")}
            </Button>
          </div>
        </form>
      </Dialog>

      <Dialog
        key="discard"
        open={dialogMode === "discard"}
        title={t("projects.form.discardTitle")}
        description={t("projects.form.discardDescription")}
        onClose={() => setDialogMode("form")}
      >
        <div className="dialog-form__actions">
          <Button
            variant="secondary"
            onClick={() => setDialogMode("form")}
            data-autofocus
          >
            {t("projects.form.discardKeep")}
          </Button>
          <Button variant="danger" onClick={closeDialog}>
            {t("projects.form.discardConfirm")}
          </Button>
        </div>
      </Dialog>
    </>
  );
}

function ProjectsSkeleton() {
  const { t } = useI18n();

  return (
    <div role="status" aria-busy="true">
      <span className="visually-hidden">{t("projects.loading")}</span>
      <div className="skeleton-table" aria-hidden="true">
        <div className="skeleton-table__head">
          <Skeleton width="32%" height={10} />
          <Skeleton width="38%" height={10} />
          <Skeleton width="48%" height={10} />
        </div>
        {Array.from({ length: SKELETON_ROWS }, (_, index) => (
          <div className="skeleton-table__row" key={index}>
            <Skeleton width={`${58 + (index % 3) * 8}%`} height={12} />
            <Skeleton width={74} height={20} radius="999px" />
            <Skeleton width={`${42 + (index % 2) * 10}%`} height={10} />
          </div>
        ))}
      </div>
    </div>
  );
}
