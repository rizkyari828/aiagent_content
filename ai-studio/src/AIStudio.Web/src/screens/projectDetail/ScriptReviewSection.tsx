import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
} from "react";
import { isApiRequestError, type ApiError } from "../../api/client";
import type { ProjectLastJob } from "../../api/projects";
import {
  approveReviewedScript,
  getReviewedScript,
  startScriptReview,
  updateReviewedScript,
  type ReviewedScriptResponse,
  type ScriptContentDto,
  type ScriptSectionDto,
} from "../../api/scriptReview";
import { Badge } from "../../components/Badge";
import { Button } from "../../components/Button";
import { Card } from "../../components/Card";
import { Dialog } from "../../components/Dialog";
import { InlineError } from "../../components/InlineError";
import { Skeleton } from "../../components/Skeleton";
import { useToast } from "../../components/Toast";
import { useFormatters } from "../../i18n/format";
import { useI18n } from "../../i18n/i18n";

export interface ScriptReviewSectionProps {
  projectId: string;
  lastJob: ProjectLastJob | null;
  onProjectChanged: () => void;
}

type LoadState = "loading" | "ready" | "error";

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

function errorCode(error: ApiError): string | undefined {
  if (error.code) {
    return error.code;
  }

  return error.status > 0 ? `HTTP ${error.status}` : undefined;
}

export function ScriptReviewSection({
  projectId,
  lastJob,
  onProjectChanged,
}: ScriptReviewSectionProps) {
  const { t } = useI18n();
  const { dateTime } = useFormatters();
  const { showToast } = useToast();

  const [review, setReview] = useState<ReviewedScriptResponse | null>(null);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  // Empty state: start review form
  const [sourceJobId, setSourceJobId] = useState("");
  const [sourceJobIdError, setSourceJobIdError] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<ApiError | null>(null);

  // Editable draft state
  const [draftTitle, setDraftTitle] = useState("");
  const [draftOpeningHook, setDraftOpeningHook] = useState("");
  const [draftSections, setDraftSections] = useState<ScriptSectionDto[]>([
    { heading: "", narration: "" },
  ]);
  const [draftClosing, setDraftClosing] = useState("");
  const [correctionReason, setCorrectionReason] = useState("");
  const [draftErrors, setDraftErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<ApiError | null>(null);

  // Approve dialog state
  const [approveDialogOpen, setApproveDialogOpen] = useState(false);
  const [approving, setApproving] = useState(false);
  const [approveError, setApproveError] = useState<ApiError | null>(null);

  const canUseLastJob =
    lastJob !== null &&
    lastJob.type === "GenerateScript" &&
    lastJob.status === "Succeeded";

  // Prefill sourceJobId if empty and lastJob is a successful GenerateScript job
  useEffect(() => {
    if (canUseLastJob && lastJob) {
      setSourceJobId((prev) => (prev ? prev : lastJob.id));
    }
  }, [canUseLastJob, lastJob]);

  // Load review on mount and reloadToken / projectId changes
  useEffect(() => {
    const controller = new AbortController();

    setLoadState("loading");
    setLoadError(null);

    getReviewedScript(projectId, controller.signal)
      .then((data) => {
        setReview(data);
        setLoadState("ready");
      })
      .catch((error) => {
        if (controller.signal.aborted) {
          return;
        }

        // 404 or script_review_not_found is a normal empty state, not an error
        if (
          isApiRequestError(error) &&
          (error.status === 404 || error.code === "script_review_not_found")
        ) {
          setReview(null);
          setLoadState("ready");
          return;
        }

        setLoadError(toApiError(error));
        setLoadState("error");
      });

    return () => controller.abort();
  }, [projectId, reloadToken]);

  // Sync draft form state whenever review data arrives or changes
  const populateDraftState = useCallback((script: ScriptContentDto) => {
    setDraftTitle(script.title ?? "");
    setDraftOpeningHook(script.openingHook ?? "");
    setDraftSections(
      script.sections && script.sections.length > 0
        ? script.sections.map((sec) => ({
            heading: sec.heading ?? "",
            narration: sec.narration ?? "",
          }))
        : [{ heading: "", narration: "" }],
    );
    setDraftClosing(script.closing ?? "");
    setDraftErrors({});
    setSaveError(null);
  }, []);

  useEffect(() => {
    if (review?.script) {
      populateDraftState(review.script);
    }
  }, [review, populateDraftState]);

  const handleRetry = () => {
    setReloadToken((prev) => prev + 1);
  };

  // Start script review from empty state
  const handleStartReview = async (event: FormEvent) => {
    event.preventDefault();

    const trimmedJobId = sourceJobId.trim();
    if (!trimmedJobId) {
      setSourceJobIdError(t("project.script.sourceJobIdRequired"));
      return;
    }

    setStarting(true);
    setStartError(null);
    setSourceJobIdError(null);

    try {
      const created = await startScriptReview(projectId, trimmedJobId);
      setReview(created);
      populateDraftState(created.script);
      showToast(t("project.script.toast.started"));
      onProjectChanged();
    } catch (error) {
      setStartError(toApiError(error));
    } finally {
      setStarting(false);
    }
  };

  // Section list manipulation
  const handleAddSection = () => {
    setDraftSections((prev) => [...prev, { heading: "", narration: "" }]);
  };

  const handleRemoveSection = (index: number) => {
    if (draftSections.length <= 1) {
      return;
    }
    setDraftSections((prev) => prev.filter((_, i) => i !== index));
    setDraftErrors((prev) => {
      const next: Record<string, string> = {};
      for (const [key, val] of Object.entries(prev)) {
        if (!key.startsWith(`section_${index}_`)) {
          next[key] = val;
        }
      }
      return next;
    });
  };

  const handleSectionFieldChange = (
    index: number,
    field: "heading" | "narration",
    value: string,
  ) => {
    setDraftSections((prev) =>
      prev.map((sec, i) => (i === index ? { ...sec, [field]: value } : sec)),
    );
    const errorKey = `section_${index}_${field}`;
    if (draftErrors[errorKey]) {
      setDraftErrors((prev) => {
        const next = { ...prev };
        delete next[errorKey];
        return next;
      });
    }
  };

  // Save draft
  const handleSaveDraft = async (event: FormEvent) => {
    event.preventDefault();

    const errors: Record<string, string> = {};

    if (!draftTitle.trim()) {
      errors.title = t("project.script.validation.required");
    }
    if (!draftOpeningHook.trim()) {
      errors.openingHook = t("project.script.validation.required");
    }
    if (!draftClosing.trim()) {
      errors.closing = t("project.script.validation.required");
    }

    if (draftSections.length === 0) {
      errors.sections = t("project.script.validation.required");
    } else {
      draftSections.forEach((sec, idx) => {
        if (!sec.heading.trim()) {
          errors[`section_${idx}_heading`] = t(
            "project.script.validation.required",
          );
        }
        if (!sec.narration.trim()) {
          errors[`section_${idx}_narration`] = t(
            "project.script.validation.required",
          );
        }
      });
    }

    if (Object.keys(errors).length > 0) {
      setDraftErrors(errors);
      return;
    }

    setSaving(true);
    setSaveError(null);

    const scriptContent: ScriptContentDto = {
      title: draftTitle.trim(),
      openingHook: draftOpeningHook.trim(),
      sections: draftSections.map((sec) => ({
        heading: sec.heading.trim(),
        narration: sec.narration.trim(),
      })),
      closing: draftClosing.trim(),
    };

    try {
      const updated = await updateReviewedScript(projectId, {
        script: scriptContent,
        correctionReason: correctionReason || null,
      });
      setReview(updated);
      setDraftErrors({});
      showToast(t("project.script.toast.saved"));
      onProjectChanged();
    } catch (error) {
      setSaveError(toApiError(error));
    } finally {
      setSaving(false);
    }
  };

  // Confirm approval in dialog
  const handleApproveConfirm = async () => {
    setApproving(true);
    setApproveError(null);

    try {
      const approved = await approveReviewedScript(projectId);
      setReview(approved);
      setApproveDialogOpen(false);
      showToast(t("project.script.toast.approved"));
      onProjectChanged();
    } catch (error) {
      setApproveError(toApiError(error));
    } finally {
      setApproving(false);
    }
  };

  if (loadState === "loading") {
    return (
      <Card
        title={t("project.script.title")}
        description={t("project.script.description")}
      >
        <div role="status" aria-busy="true">
          <span className="visually-hidden">{t("project.script.loading")}</span>
          <div className="detail-skeleton" aria-hidden="true">
            <Skeleton width="40%" height={24} />
            <Skeleton width="60%" height={16} />
            <Skeleton width="100%" height={80} />
            <Skeleton width="100%" height={80} />
          </div>
        </div>
      </Card>
    );
  }

  if (loadState === "error" || (loadError && review === null)) {
    return (
      <Card
        title={t("project.script.title")}
        description={t("project.script.description")}
      >
        <InlineError
          title={t("project.script.error.title")}
          detail={loadError ? (loadError.detail ?? loadError.title) : undefined}
          code={loadError ? errorCode(loadError) : undefined}
          actionLabel={t("action.retry")}
          onAction={handleRetry}
        />
      </Card>
    );
  }

  // Empty state: review has not started yet
  if (review === null) {
    return (
      <Card
        title={t("project.script.title")}
        description={t("project.script.description")}
      >
        <div className="stack">
          <p className="inline-note">{t("project.script.empty")}</p>

          <form className="stack-sm" onSubmit={handleStartReview} noValidate>
            {startError ? (
              <InlineError
                title={t("project.script.startErrorTitle")}
                detail={startError.detail ?? startError.title}
                code={errorCode(startError)}
              />
            ) : null}

            <div className="field">
              <div
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                }}
              >
                <label
                  className="field__label"
                  htmlFor="script-source-job-id"
                  style={{ marginBottom: 0 }}
                >
                  {t("project.script.sourceJobIdLabel")}
                </label>
                {canUseLastJob && lastJob ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() => {
                      setSourceJobId(lastJob.id);
                      setSourceJobIdError(null);
                    }}
                  >
                    {t("project.script.useLastJob")}
                  </Button>
                ) : null}
              </div>

              <input
                id="script-source-job-id"
                className="field__input mono"
                type="text"
                value={sourceJobId}
                placeholder={t("project.script.sourceJobIdPlaceholder")}
                onChange={(event) => {
                  setSourceJobId(event.target.value);
                  if (sourceJobIdError) {
                    setSourceJobIdError(null);
                  }
                }}
                aria-invalid={sourceJobIdError ? true : undefined}
                aria-describedby={
                  sourceJobIdError
                    ? "source-job-id-error"
                    : "source-job-id-hint"
                }
              />

              {sourceJobIdError ? (
                <p className="field__error" id="source-job-id-error">
                  {sourceJobIdError}
                </p>
              ) : (
                <p className="field__hint" id="source-job-id-hint">
                  {t("project.script.sourceJobIdHint")}
                </p>
              )}
            </div>

            <div className="page-header__actions">
              <Button
                type="submit"
                variant="primary"
                loading={starting}
                loadingLabel={t("project.script.startSubmitting")}
              >
                {t("project.script.startSubmit")}
              </Button>
            </div>
          </form>
        </div>
      </Card>
    );
  }

  const isApproved = review.status === "approved";

  return (
    <>
      <Card
        title={t("project.script.title")}
        description={t("project.script.description")}
        actions={
          <div className="page-header__actions">
            {isApproved ? (
              <Badge tone="success">
                {t("project.script.status.approved")}
              </Badge>
            ) : (
              <Badge tone="accent">{t("project.script.status.draft")}</Badge>
            )}
            <Badge tone="neutral">
              {t("project.script.revision", { revision: review.revision })}
            </Badge>
          </div>
        }
      >
        <div className="stack">
          {/* Metadata definition list */}
          <dl className="definition-list">
            <div>
              <dt>{t("project.script.meta.status")}</dt>
              <dd>
                {isApproved ? (
                  <Badge tone="success">
                    {t("project.script.status.approved")}
                  </Badge>
                ) : (
                  <Badge tone="accent">
                    {t("project.script.status.draft")}
                  </Badge>
                )}
              </dd>
            </div>
            <div>
              <dt>{t("project.script.meta.revision")}</dt>
              <dd>
                <Badge tone="neutral">
                  {t("project.script.revision", { revision: review.revision })}
                </Badge>
              </dd>
            </div>
            <div>
              <dt>{t("project.script.meta.sourceJobId")}</dt>
              <dd className="mono">{review.sourceJobId}</dd>
            </div>
            <div>
              <dt>{t("project.script.meta.updated")}</dt>
              <dd>
                <time dateTime={review.updatedAt}>
                  {dateTime(review.updatedAt)}
                </time>
              </dd>
            </div>
            {review.approvedAt ? (
              <div>
                <dt>{t("project.script.meta.approvedAt")}</dt>
                <dd>
                  <time dateTime={review.approvedAt}>
                    {dateTime(review.approvedAt)}
                  </time>
                </dd>
              </div>
            ) : null}
          </dl>

          {isApproved ? (
            /* Immutable Approved View */
            <div className="stack">
              <p className="inline-note">
                {t("project.script.immutableNotice")}
              </p>

              <div className="stack-sm">
                <span className="field__label">
                  {t("project.script.field.title")}
                </span>
                <p className="card__title">{review.script.title}</p>
              </div>

              <div className="stack-sm">
                <span className="field__label">
                  {t("project.script.field.openingHook")}
                </span>
                <p className="detail-prose">{review.script.openingHook}</p>
              </div>

              <div className="stack-sm">
                <span className="field__label">
                  {t("project.script.field.sections")}
                </span>
                <ol
                  className="next-list"
                  style={{
                    listStyleType: "decimal",
                    paddingLeft: "var(--space-5)",
                  }}
                >
                  {review.script.sections.map((section, idx) => (
                    <li
                      key={idx}
                      className="stack-sm"
                      style={{ marginBottom: "var(--space-3)" }}
                    >
                      <strong style={{ display: "block" }}>
                        {section.heading}
                      </strong>
                      <p className="detail-prose">{section.narration}</p>
                    </li>
                  ))}
                </ol>
              </div>

              <div className="stack-sm">
                <span className="field__label">
                  {t("project.script.field.closing")}
                </span>
                <p className="detail-prose">{review.script.closing}</p>
              </div>
            </div>
          ) : (
            /* Editable Draft View */
            <form className="stack" onSubmit={handleSaveDraft} noValidate>
              {saveError ? (
                <InlineError
                  title={t("project.script.saveErrorTitle")}
                  detail={saveError.detail ?? saveError.title}
                  code={errorCode(saveError)}
                />
              ) : null}

              <div className="field">
                <label className="field__label" htmlFor="script-draft-title">
                  {t("project.script.field.title")}
                </label>
                <input
                  id="script-draft-title"
                  className="field__input"
                  type="text"
                  value={draftTitle}
                  onChange={(event) => {
                    setDraftTitle(event.target.value);
                    if (draftErrors.title) {
                      setDraftErrors((prev) => {
                        const next = { ...prev };
                        delete next.title;
                        return next;
                      });
                    }
                  }}
                  aria-invalid={draftErrors.title ? true : undefined}
                  aria-describedby={
                    draftErrors.title
                      ? "script-draft-title-error"
                      : undefined
                  }
                />
                {draftErrors.title ? (
                  <p className="field__error" id="script-draft-title-error">
                    {draftErrors.title}
                  </p>
                ) : null}
              </div>

              <div className="field">
                <label
                  className="field__label"
                  htmlFor="script-draft-openingHook"
                >
                  {t("project.script.field.openingHook")}
                </label>
                <textarea
                  id="script-draft-openingHook"
                  className="field__textarea"
                  rows={3}
                  value={draftOpeningHook}
                  onChange={(event) => {
                    setDraftOpeningHook(event.target.value);
                    if (draftErrors.openingHook) {
                      setDraftErrors((prev) => {
                        const next = { ...prev };
                        delete next.openingHook;
                        return next;
                      });
                    }
                  }}
                  aria-invalid={draftErrors.openingHook ? true : undefined}
                  aria-describedby={
                    draftErrors.openingHook
                      ? "script-draft-openingHook-error"
                      : undefined
                  }
                />
                {draftErrors.openingHook ? (
                  <p
                    className="field__error"
                    id="script-draft-openingHook-error"
                  >
                    {draftErrors.openingHook}
                  </p>
                ) : null}
              </div>

              <div className="stack-sm">
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                  }}
                >
                  <span className="field__label" style={{ marginBottom: 0 }}>
                    {t("project.script.field.sections")}
                  </span>
                  <Button
                    type="button"
                    size="sm"
                    variant="secondary"
                    onClick={handleAddSection}
                  >
                    {t("project.script.section.add")}
                  </Button>
                </div>

                {draftErrors.sections ? (
                  <p className="field__error">{draftErrors.sections}</p>
                ) : null}

                <div className="stack-sm">
                  {draftSections.map((sec, idx) => (
                    <div
                      key={idx}
                      className="card"
                      style={{
                        padding: "var(--space-4)",
                        background: "var(--color-surface-subtle)",
                      }}
                    >
                      <div
                        style={{
                          display: "flex",
                          justifyContent: "space-between",
                          alignItems: "center",
                          marginBottom: "var(--space-3)",
                        }}
                      >
                        <span
                          style={{
                            fontSize: "var(--text-xs)",
                            fontWeight: "var(--weight-semibold)",
                            color: "var(--color-text-muted)",
                            textTransform: "uppercase",
                            letterSpacing: "0.08em",
                          }}
                        >
                          {t("project.script.section.index", {
                            index: idx + 1,
                          })}
                        </span>
                        {draftSections.length > 1 ? (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => handleRemoveSection(idx)}
                          >
                            {t("project.script.section.remove")}
                          </Button>
                        ) : null}
                      </div>

                      <div className="stack-sm">
                        <div className="field">
                          <label
                            className="field__label"
                            htmlFor={`script-section-${idx}-heading`}
                          >
                            {t("project.script.section.headingLabel")}
                          </label>
                          <input
                            id={`script-section-${idx}-heading`}
                            className="field__input"
                            type="text"
                            value={sec.heading}
                            onChange={(event) =>
                              handleSectionFieldChange(
                                idx,
                                "heading",
                                event.target.value,
                              )
                            }
                            aria-invalid={
                              draftErrors[`section_${idx}_heading`]
                                ? true
                                : undefined
                            }
                            aria-describedby={
                              draftErrors[`section_${idx}_heading`]
                                ? `script-section-${idx}-heading-error`
                                : undefined
                            }
                          />
                          {draftErrors[`section_${idx}_heading`] ? (
                            <p
                              className="field__error"
                              id={`script-section-${idx}-heading-error`}
                            >
                              {draftErrors[`section_${idx}_heading`]}
                            </p>
                          ) : null}
                        </div>

                        <div className="field">
                          <label
                            className="field__label"
                            htmlFor={`script-section-${idx}-narration`}
                          >
                            {t("project.script.section.narrationLabel")}
                          </label>
                          <textarea
                            id={`script-section-${idx}-narration`}
                            className="field__textarea"
                            rows={3}
                            value={sec.narration}
                            onChange={(event) =>
                              handleSectionFieldChange(
                                idx,
                                "narration",
                                event.target.value,
                              )
                            }
                            aria-invalid={
                              draftErrors[`section_${idx}_narration`]
                                ? true
                                : undefined
                            }
                            aria-describedby={
                              draftErrors[`section_${idx}_narration`]
                                ? `script-section-${idx}-narration-error`
                                : undefined
                            }
                          />
                          {draftErrors[`section_${idx}_narration`] ? (
                            <p
                              className="field__error"
                              id={`script-section-${idx}-narration-error`}
                            >
                              {draftErrors[`section_${idx}_narration`]}
                            </p>
                          ) : null}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="field">
                <label
                  className="field__label"
                  htmlFor="script-draft-closing"
                >
                  {t("project.script.field.closing")}
                </label>
                <textarea
                  id="script-draft-closing"
                  className="field__textarea"
                  rows={3}
                  value={draftClosing}
                  onChange={(event) => {
                    setDraftClosing(event.target.value);
                    if (draftErrors.closing) {
                      setDraftErrors((prev) => {
                        const next = { ...prev };
                        delete next.closing;
                        return next;
                      });
                    }
                  }}
                  aria-invalid={draftErrors.closing ? true : undefined}
                  aria-describedby={
                    draftErrors.closing
                      ? "script-draft-closing-error"
                      : undefined
                  }
                />
                {draftErrors.closing ? (
                  <p
                    className="field__error"
                    id="script-draft-closing-error"
                  >
                    {draftErrors.closing}
                  </p>
                ) : null}
              </div>

              <div className="field">
                <label
                  className="field__label"
                  htmlFor="script-correction-reason"
                >
                  {t("project.scriptReview.correctionReason.label")}
                </label>
                <select
                  id="script-correction-reason"
                  className="select"
                  value={correctionReason}
                  onChange={(event) => setCorrectionReason(event.target.value)}
                >
                  <option value="">
                    {t("project.scriptReview.correctionReason.none")}
                  </option>
                  <option value="hook_weak">
                    {t("project.scriptReview.correctionReason.hook_weak")}
                  </option>
                  <option value="too_long">
                    {t("project.scriptReview.correctionReason.too_long")}
                  </option>
                  <option value="too_short">
                    {t("project.scriptReview.correctionReason.too_short")}
                  </option>
                  <option value="tone_mismatch">
                    {t("project.scriptReview.correctionReason.tone_mismatch")}
                  </option>
                  <option value="fact_fix">
                    {t("project.scriptReview.correctionReason.fact_fix")}
                  </option>
                  <option value="visual_unclear">
                    {t("project.scriptReview.correctionReason.visual_unclear")}
                  </option>
                  <option value="tts_awkward">
                    {t("project.scriptReview.correctionReason.tts_awkward")}
                  </option>
                  <option value="other">
                    {t("project.scriptReview.correctionReason.other")}
                  </option>
                </select>
              </div>

              <div className="dialog-form__actions">
                <Button
                  type="submit"
                  variant="secondary"
                  loading={saving}
                  loadingLabel={t("project.script.saveSubmitting")}
                  disabled={approving}
                >
                  {t("project.script.saveSubmit")}
                </Button>
                <Button
                  type="button"
                  variant="primary"
                  onClick={() => setApproveDialogOpen(true)}
                  disabled={saving || approving}
                >
                  {t("project.script.approveOpen")}
                </Button>
              </div>
            </form>
          )}
        </div>
      </Card>

      <Dialog
        open={approveDialogOpen}
        title={t("project.script.approveDialog.title")}
        description={t("project.script.approveDialog.description")}
        onClose={() => {
          if (!approving) {
            setApproveDialogOpen(false);
            setApproveError(null);
          }
        }}
        dismissible={!approving}
      >
        <div className="stack">
          {approveError ? (
            <InlineError
              title={t("project.script.approveDialog.errorTitle")}
              detail={approveError.detail ?? approveError.title}
              code={errorCode(approveError)}
            />
          ) : null}

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={() => {
                setApproveDialogOpen(false);
                setApproveError(null);
              }}
              disabled={approving}
            >
              {t("action.cancel")}
            </Button>
            <Button
              variant="primary"
              loading={approving}
              loadingLabel={t("project.script.approveDialog.submitting")}
              onClick={handleApproveConfirm}
            >
              {t("project.script.approveDialog.confirm")}
            </Button>
          </div>
        </div>
      </Dialog>
    </>
  );
}
