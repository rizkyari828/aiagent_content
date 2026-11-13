import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type FormEvent,
} from "react";
import {
  getCapabilities,
  loadPreferredProviders,
  savePreferredProvider,
  type CapabilityView,
  type FeatureCapabilityKey,
  type PreferredProvidersMap,
} from "../../api/capabilities";
import { isApiRequestError, type ApiError } from "../../api/client";
import {
  cancelJob,
  enqueueAudioJob,
  enqueueFinalVideoQaJob,
  enqueueGenerateIdeaJob,
  enqueueGenerateScriptJob,
  enqueueRenderJob,
  enqueueStoryboardJob,
  enqueueVisualJob,
  getJob,
  type JobResponse,
} from "../../api/jobs";
import type { ProjectLastJob } from "../../api/projects";
import { Badge, type BadgeTone } from "../../components/Badge";
import { Button } from "../../components/Button";
import { Card } from "../../components/Card";
import { Dialog } from "../../components/Dialog";
import { InlineError } from "../../components/InlineError";
import { Skeleton } from "../../components/Skeleton";
import { useToast } from "../../components/Toast";
import { useFormatters } from "../../i18n/format";
import { useI18n, type Translate } from "../../i18n/i18n";

export interface ProductionSectionProps {
  projectId: string;
  lastJob: ProjectLastJob | null;
  onProjectChanged: () => void;
}

type PipelineJobType =
  | "GenerateIdea"
  | "GenerateScript"
  | "GenerateStoryboard"
  | "GenerateSceneVisuals"
  | "GenerateAudio"
  | "RenderVideo"
  | "FinalVideoQa";

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

function jobStatusTone(status: string): BadgeTone {
  const normalized = status.toLowerCase();
  if (normalized === "completed" || normalized === "succeeded") {
    return "success";
  }
  if (normalized === "running") {
    return "accent";
  }
  if (normalized === "pending" || normalized === "queued") {
    return "warning";
  }
  if (
    normalized === "failed" ||
    normalized === "cancelled" ||
    normalized === "canceled"
  ) {
    return "danger";
  }
  return "neutral";
}

function isJobActive(status: string): boolean {
  const normalized = status.toLowerCase();
  return (
    normalized === "pending" ||
    normalized === "running" ||
    normalized === "queued"
  );
}

const PROVIDER_I18N_KEYS: Record<string, string> = {
  ollama: "project.production.provider.ollama",
  "gemini-text": "project.production.provider.geminiText",
  deepseek: "project.production.provider.deepseek",
  openai: "project.production.provider.openai",
  "comfyui-flux": "project.production.provider.comfyuiFlux",
  "gemini-nanobanana": "project.production.provider.geminiNanobanana",
  "comfyui-i2v": "project.production.provider.comfyuiI2v",
  "gemini-veo": "project.production.provider.geminiVeo",
  voxcpm2: "project.production.provider.voxcpm2",
  "gemini-tts": "project.production.provider.geminiTts",
  "ace-step": "project.production.provider.aceStep",
};

function getProviderLabel(providerId: string, t: Translate): string {
  const key = PROVIDER_I18N_KEYS[providerId];
  return key ? t(key) : providerId;
}

export function ProductionSection({
  projectId,
  lastJob,
  onProjectChanged,
}: ProductionSectionProps) {
  const { t } = useI18n();
  const { dateTime, number } = useFormatters();
  const { showToast } = useToast();

  // Preferred Providers from Settings
  const [preferredProviders, setPreferredProviders] =
    useState<PreferredProvidersMap>(() => loadPreferredProviders());

  // Capabilities from API
  const [capabilities, setCapabilities] = useState<CapabilityView[]>([]);

  useEffect(() => {
    const controller = new AbortController();
    getCapabilities(controller.signal)
      .then((res) => {
        setCapabilities(res.capabilities);
      })
      .catch(() => {
        // Fallback to static lists when API is unavailable
      });
    return () => controller.abort();
  }, []);

  const visualImageProviders = useMemo(() => {
    const cap = capabilities.find((c) => c.id === "visual.ai_image");
    if (cap && cap.providers.length > 0) {
      return cap.providers.map((p) => p.providerId);
    }
    return ["comfyui-flux", "gemini-nanobanana"];
  }, [capabilities]);

  const visualVideoProviders = useMemo(() => {
    const cap = capabilities.find((c) => c.id === "visual.character_animation");
    if (cap && cap.providers.length > 0) {
      return cap.providers.map((p) => p.providerId);
    }
    return ["comfyui-i2v", "gemini-veo"];
  }, [capabilities]);

  const audioSpeechProviders = useMemo(() => {
    const cap = capabilities.find((c) => c.id === "speech.narration");
    if (cap && cap.providers.length > 0) {
      return cap.providers.map((p) => p.providerId);
    }
    return ["voxcpm2", "gemini-tts"];
  }, [capabilities]);

  const textGenerationProviders = useMemo(() => {
    const cap = capabilities.find((c) => c.id === "text.generation");
    if (cap && cap.providers.length > 0) {
      return cap.providers.map((p) => p.providerId);
    }
    return ["ollama", "gemini-text", "deepseek", "openai"];
  }, [capabilities]);

  // Job Inspector State
  const [jobIdInput, setJobIdInput] = useState(() => lastJob?.id ?? "");
  const [inspectedJob, setInspectedJob] = useState<JobResponse | null>(null);
  const [inspectLoading, setInspectLoading] = useState(false);
  const [inspectError, setInspectError] = useState<ApiError | null>(null);

  // Cancel Job State
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [cancelError, setCancelError] = useState<ApiError | null>(null);

  // Pipeline Job Form State
  const [selectedJobType, setSelectedJobType] =
    useState<PipelineJobType>("GenerateIdea");
  const [enqueuing, setEnqueuing] = useState(false);
  const [enqueueApiError, setEnqueueApiError] = useState<ApiError | null>(null);
  const [enqueueFormErrors, setEnqueueFormErrors] = useState<
    Record<string, string>
  >({});

  // 1. GenerateIdea fields
  const [ideaProvider, setIdeaProvider] = useState<string>(
    () => preferredProviders["text.generation"] ?? "auto",
  );
  const [ideaModel, setIdeaModel] = useState("");
  const [ideaTopic, setIdeaTopic] = useState("");
  const [ideaAudience, setIdeaAudience] = useState("");
  const [ideaLanguage, setIdeaLanguage] = useState("");

  // 2. GenerateScript fields
  const [scriptProvider, setScriptProvider] = useState<string>(
    () => preferredProviders["text.generation"] ?? "auto",
  );
  const [scriptModel, setScriptModel] = useState("");
  const [scriptTitle, setScriptTitle] = useState("");
  const [scriptHook, setScriptHook] = useState("");
  const [scriptSummary, setScriptSummary] = useState("");
  const [scriptAngle, setScriptAngle] = useState("");
  const [scriptAudience, setScriptAudience] = useState("");
  const [scriptFormat, setScriptFormat] = useState("");
  const [scriptLanguage, setScriptLanguage] = useState("");

  // 3. GenerateStoryboard fields
  const [storyboardProvider, setStoryboardProvider] = useState<string>(
    () => preferredProviders["text.generation"] ?? "auto",
  );
  const [storyboardNotes, setStoryboardNotes] = useState("");

  // 4. GenerateSceneVisuals fields
  const [visualsImageProvider, setVisualsImageProvider] = useState<string>(
    () => preferredProviders["visual.ai_image"] ?? "auto",
  );
  const [visualsVideoProvider, setVisualsVideoProvider] = useState<string>(
    () => preferredProviders["visual.character_animation"] ?? "auto",
  );
  const [visualsStoryboardJobId, setVisualsStoryboardJobId] = useState("");
  const [visualsRecipe, setVisualsRecipe] = useState<string>("auto");
  const [visualsForce, setVisualsForce] = useState(false);
  const [visualsArtDirection, setVisualsArtDirection] = useState("");

  // 5. GenerateAudio fields
  const [audioSpeechProvider, setAudioSpeechProvider] = useState<string>(
    () => preferredProviders["speech.narration"] ?? "auto",
  );
  const [audioMusicProvider, setAudioMusicProvider] = useState<string>(
    () => preferredProviders["music.instrumental"] ?? "auto",
  );
  const [audioStoryboardJobId, setAudioStoryboardJobId] = useState("");

  // 6. RenderVideo fields
  const [renderStoryboardJobId, setRenderStoryboardJobId] = useState("");
  const [renderVariant, setRenderVariant] = useState("");

  // 7. FinalVideoQa fields
  const [qaRenderJobId, setQaRenderJobId] = useState("");

  const initialFetchAttempted = useRef(false);

  const handleProviderSelect = (
    featureKey: FeatureCapabilityKey,
    value: string,
    setter: (val: string) => void,
  ) => {
    setter(value);
    const updated = savePreferredProvider(featureKey, value);
    setPreferredProviders(updated);
  };

  const getInspectedStoryboardScenes = useCallback(() => {
    if (!inspectedJob?.result || typeof inspectedJob.result !== "object") {
      return null;
    }
    const rec = inspectedJob.result as Record<string, unknown>;
    if (Array.isArray(rec.scenes) && rec.scenes.length > 0) {
      return rec.scenes;
    }
    return null;
  }, [inspectedJob]);

  const handleCopyScenePrompts = useCallback(async () => {
    const scenes = getInspectedStoryboardScenes();
    if (!scenes || scenes.length === 0) {
      showToast(t("project.production.noScenesToCopy"), "info");
      return;
    }

    const rec = inspectedJob?.result as Record<string, unknown> | undefined;
    const title = typeof rec?.title === "string" ? rec.title : null;

    const lines: string[] = [];
    if (title) {
      lines.push(`# ${title}`);
      lines.push("");
    }

    scenes.forEach((item, index) => {
      if (typeof item === "object" && item !== null) {
        const s = item as {
          heading?: string;
          visual?: string;
          sceneIndex?: number;
        };
        const heading = s.heading ?? `Scene ${index + 1}`;
        const visual = s.visual ?? "";
        lines.push(`## Scene ${index + 1}: ${heading}`);
        lines.push(`Prompt: ${visual}`);
        lines.push("");
      }
    });

    const promptText = lines.join("\n").trim();

    try {
      await navigator.clipboard.writeText(promptText);
      showToast(t("project.production.toast.scenePromptCopied"), "success");
    } catch {
      showToast(t("project.production.toast.copyFailed"), "error");
    }
  }, [getInspectedStoryboardScenes, inspectedJob, showToast, t]);

  const fetchJob = useCallback(
    async (id: string, signal?: AbortSignal) => {
      const trimmed = id.trim();
      if (!trimmed) {
        setInspectedJob(null);
        setInspectError(null);
        return;
      }

      setInspectLoading(true);
      setInspectError(null);

      try {
        const job = await getJob(trimmed, signal);
        if (signal?.aborted) {
          return;
        }
        setInspectedJob(job);
        setInspectLoading(false);
      } catch (err: unknown) {
        if (signal?.aborted) {
          return;
        }
        setInspectLoading(false);
        setInspectError(toApiError(err));
      }
    },
    [],
  );

  // Automatically fetch lastJob.id on mount if available
  useEffect(() => {
    if (!initialFetchAttempted.current && lastJob?.id) {
      initialFetchAttempted.current = true;
      const controller = new AbortController();
      fetchJob(lastJob.id, controller.signal);
      return () => controller.abort();
    }
  }, [lastJob?.id, fetchJob]);

  // Periodic poll while inspected job is pending or running
  useEffect(() => {
    if (!inspectedJob || !isJobActive(inspectedJob.status)) {
      return;
    }

    const timer = setInterval(() => {
      fetchJob(inspectedJob.id);
    }, 4000);

    return () => clearInterval(timer);
  }, [inspectedJob, fetchJob]);

  const handleInspectSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    fetchJob(jobIdInput);
  };

  const handleRefreshJob = () => {
    if (inspectedJob) {
      fetchJob(inspectedJob.id);
    }
  };

  const handleCancelJob = async () => {
    if (!inspectedJob || cancelling) {
      return;
    }

    setCancelling(true);
    setCancelError(null);

    try {
      await cancelJob(inspectedJob.id);
      setCancelling(false);
      setCancelDialogOpen(false);
      showToast(t("project.production.toast.cancelled"), "success");
      fetchJob(inspectedJob.id);
      onProjectChanged();
    } catch (err: unknown) {
      setCancelling(false);
      setCancelError(toApiError(err));
    }
  };

  const handleEnqueueSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (enqueuing) {
      return;
    }

    const errors: Record<string, string> = {};

    if (selectedJobType === "GenerateIdea") {
      if (!ideaTopic.trim()) {
        errors.ideaTopic = t("project.production.validation.topicRequired");
      }
    } else if (selectedJobType === "GenerateScript") {
      if (!scriptTitle.trim()) {
        errors.scriptTitle = t("project.production.validation.titleRequired");
      }
      if (!scriptHook.trim()) {
        errors.scriptHook = t("project.production.validation.hookRequired");
      }
      if (!scriptSummary.trim()) {
        errors.scriptSummary = t(
          "project.production.validation.summaryRequired",
        );
      }
      if (!scriptAngle.trim()) {
        errors.scriptAngle = t("project.production.validation.angleRequired");
      }
      if (!scriptAudience.trim()) {
        errors.scriptAudience = t(
          "project.production.validation.targetAudienceRequired",
        );
      }
      if (!scriptFormat.trim()) {
        errors.scriptFormat = t(
          "project.production.validation.suggestedFormatRequired",
        );
      }
    } else if (selectedJobType === "GenerateSceneVisuals") {
      if (!visualsStoryboardJobId.trim()) {
        errors.visualsStoryboardJobId = t(
          "project.production.validation.storyboardJobIdRequired",
        );
      }
    } else if (selectedJobType === "GenerateAudio") {
      if (!audioStoryboardJobId.trim()) {
        errors.audioStoryboardJobId = t(
          "project.production.validation.storyboardJobIdRequired",
        );
      }
    } else if (selectedJobType === "RenderVideo") {
      if (!renderStoryboardJobId.trim()) {
        errors.renderStoryboardJobId = t(
          "project.production.validation.storyboardJobIdRequired",
        );
      }
    } else if (selectedJobType === "FinalVideoQa") {
      if (!qaRenderJobId.trim()) {
        errors.qaRenderJobId = t(
          "project.production.validation.renderJobIdRequired",
        );
      }
    }

    setEnqueueFormErrors(errors);
    setEnqueueApiError(null);

    if (Object.keys(errors).length > 0) {
      return;
    }

    setEnqueuing(true);

    try {
      let resultJobId = "";

      if (selectedJobType === "GenerateIdea") {
        const response = await enqueueGenerateIdeaJob(projectId, {
          topic: ideaTopic.trim(),
          targetAudience: ideaAudience.trim() || undefined,
          language: ideaLanguage.trim() || undefined,
          model: ideaModel.trim() || undefined,
          provider: ideaProvider === "auto" ? undefined : ideaProvider,
        });
        resultJobId = response.jobId;
      } else if (selectedJobType === "GenerateScript") {
        const response = await enqueueGenerateScriptJob(projectId, {
          selectedIdea: {
            title: scriptTitle.trim(),
            hook: scriptHook.trim(),
            summary: scriptSummary.trim(),
            angle: scriptAngle.trim(),
            targetAudience: scriptAudience.trim(),
            suggestedFormat: scriptFormat.trim(),
          },
          language: scriptLanguage.trim() || undefined,
          model: scriptModel.trim() || undefined,
          provider: scriptProvider === "auto" ? undefined : scriptProvider,
        });
        resultJobId = response.jobId;
      } else if (selectedJobType === "GenerateStoryboard") {
        const response = await enqueueStoryboardJob(
          projectId,
          storyboardNotes.trim()
            ? { notes: storyboardNotes.trim() }
            : undefined,
        );
        resultJobId = response.jobId;
      } else if (selectedJobType === "GenerateSceneVisuals") {
        const recipeParts =
          visualsRecipe !== "auto" ? visualsRecipe.split(":") : null;
        const productionRecipe =
          recipeParts && recipeParts.length === 2
            ? {
                id: recipeParts[0],
                version: Number.parseInt(recipeParts[1], 10),
              }
            : undefined;

        const response = await enqueueVisualJob(
          projectId,
          visualsStoryboardJobId.trim(),
          {
            force: visualsForce,
            artDirection: visualsArtDirection.trim() || null,
            imageProvider:
              visualsImageProvider === "auto" ? undefined : visualsImageProvider,
            videoProvider:
              visualsVideoProvider === "auto" ? undefined : visualsVideoProvider,
            productionRecipe,
          },
        );
        resultJobId = response.jobId;
      } else if (selectedJobType === "GenerateAudio") {
        const response = await enqueueAudioJob(
          projectId,
          audioStoryboardJobId.trim(),
          {
            speechProvider:
              audioSpeechProvider === "auto" ? undefined : audioSpeechProvider,
          },
        );
        resultJobId = response.jobId;
      } else if (selectedJobType === "RenderVideo") {
        const response = await enqueueRenderJob(
          projectId,
          renderStoryboardJobId.trim(),
          renderVariant.trim() || undefined,
        );
        resultJobId = response.jobId;
      } else if (selectedJobType === "FinalVideoQa") {
        const response = await enqueueFinalVideoQaJob(
          projectId,
          qaRenderJobId.trim(),
        );
        resultJobId = response.jobId;
      }

      setEnqueuing(false);
      setJobIdInput(resultJobId);
      fetchJob(resultJobId);
      showToast(t("project.production.toast.enqueued"), "success");
      onProjectChanged();
    } catch (err: unknown) {
      setEnqueuing(false);
      setEnqueueApiError(toApiError(err));
    }
  };

  const hasStoryboardScenesToCopy = Boolean(
    inspectedJob &&
      inspectedJob.type === "GenerateStoryboard" &&
      getInspectedStoryboardScenes(),
  );

  return (
    <div className="stack">
      <div className="detail-grid">
        {/* Job Inspector Card */}
        <Card
          title={t("project.production.inspector.title")}
          description={t("project.production.inspector.description")}
        >
          <form
            className="form-stack"
            onSubmit={handleInspectSubmit}
            noValidate
          >
            <div className="field">
              <label
                className="field__label"
                htmlFor="inspect-job-id"
              >
                {t("project.production.inspector.jobId")}
              </label>
              <div style={{ display: "flex", gap: "var(--space-2)" }}>
                <input
                  id="inspect-job-id"
                  className="field__input mono"
                  type="text"
                  placeholder={t(
                    "project.production.inspector.jobIdPlaceholder",
                  )}
                  value={jobIdInput}
                  onChange={(e) => setJobIdInput(e.target.value)}
                />
                <Button
                  type="submit"
                  variant="secondary"
                  loading={inspectLoading}
                  loadingLabel={t("project.production.inspecting")}
                >
                  {t("project.production.inspectJob")}
                </Button>
              </div>
            </div>

            {inspectError ? (
              <InlineError
                title={t("project.production.inspector.errorTitle")}
                detail={inspectError.detail ?? inspectError.title}
                code={errorCode(inspectError)}
              />
            ) : null}

            {inspectLoading && !inspectedJob ? (
              <div role="status" aria-busy="true">
                <span className="visually-hidden">
                  {t("project.production.inspecting")}
                </span>
                <div className="detail-skeleton" aria-hidden="true">
                  <Skeleton width="100%" height={32} />
                  <Skeleton width="100%" height={80} />
                </div>
              </div>
            ) : null}

            {inspectedJob ? (
              <div className="stack-sm">
                <dl className="definition-list">
                  <div>
                    <dt>{t("project.production.job.id")}</dt>
                    <dd className="mono">{inspectedJob.id}</dd>
                  </div>
                  <div>
                    <dt>{t("project.production.job.type")}</dt>
                    <dd className="mono">{inspectedJob.type}</dd>
                  </div>
                  <div>
                    <dt>{t("project.production.job.status")}</dt>
                    <dd>
                      <Badge tone={jobStatusTone(inspectedJob.status)}>
                        {inspectedJob.status}
                      </Badge>
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.production.job.retries")}</dt>
                    <dd>
                      {inspectedJob.retryCount ?? 0} /{" "}
                      {inspectedJob.maxRetries ?? 0}
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.production.job.createdAt")}</dt>
                    <dd>
                      <time dateTime={inspectedJob.createdAt}>
                        {dateTime(inspectedJob.createdAt)}
                      </time>
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.production.job.completedAt")}</dt>
                    <dd>
                      {inspectedJob.completedAt ? (
                        <time dateTime={inspectedJob.completedAt}>
                          {dateTime(inspectedJob.completedAt)}
                        </time>
                      ) : (
                        "—"
                      )}
                    </dd>
                  </div>
                  {inspectedJob.errorCode || inspectedJob.errorMessage ? (
                    <div>
                      <dt>{t("project.production.job.error")}</dt>
                      <dd>
                        <InlineError
                          title={
                            inspectedJob.errorCode ??
                            t("project.production.job.failed")
                          }
                          detail={inspectedJob.errorMessage ?? undefined}
                        />
                      </dd>
                    </div>
                  ) : null}
                  {inspectedJob.result ? (
                    <div>
                      <dt>{t("project.production.job.result")}</dt>
                      <dd>
                        <JobResultSummary
                          result={inspectedJob.result}
                          t={t}
                          number={number}
                          onCopyScenePrompt={
                            getInspectedStoryboardScenes()
                              ? handleCopyScenePrompts
                              : undefined
                          }
                        />
                      </dd>
                    </div>
                  ) : null}
                </dl>

                {isJobActive(inspectedJob.status) ? (
                  <div
                    style={{
                      display: "flex",
                      gap: "var(--space-2)",
                      marginTop: "var(--space-3)",
                    }}
                  >
                    <Button
                      variant="secondary"
                      onClick={handleRefreshJob}
                      loading={inspectLoading}
                    >
                      {t("project.production.refreshJob")}
                    </Button>
                    <Button
                      variant="danger"
                      onClick={() => setCancelDialogOpen(true)}
                    >
                      {t("project.production.cancelJobOpen")}
                    </Button>
                  </div>
                ) : null}
              </div>
            ) : null}
          </form>
        </Card>

        {/* Enqueue Pipeline Jobs Card */}
        <Card
          title={t("project.production.enqueue.title")}
          description={t("project.production.enqueue.description")}
        >
          <form
            className="form-stack"
            onSubmit={handleEnqueueSubmit}
            noValidate
          >
            {enqueueApiError ? (
              <InlineError
                title={t("project.production.enqueue.errorTitle")}
                detail={enqueueApiError.detail ?? enqueueApiError.title}
                code={errorCode(enqueueApiError)}
              />
            ) : null}

            <div className="field">
              <label
                className="field__label"
                htmlFor="pipeline-job-select"
              >
                {t("project.production.enqueue.selectJobType")}
              </label>
              <select
                id="pipeline-job-select"
                className="select"
                value={selectedJobType}
                onChange={(e) => {
                  setSelectedJobType(e.target.value as PipelineJobType);
                  setEnqueueFormErrors({});
                  setEnqueueApiError(null);
                }}
              >
                <option value="GenerateIdea">
                  {t("project.production.jobType.GenerateIdea")}
                </option>
                <option value="GenerateScript">
                  {t("project.production.jobType.GenerateScript")}
                </option>
                <option value="GenerateStoryboard">
                  {t("project.production.jobType.GenerateStoryboard")}
                </option>
                <option value="GenerateSceneVisuals">
                  {t("project.production.jobType.GenerateSceneVisuals")}
                </option>
                <option value="GenerateAudio">
                  {t("project.production.jobType.GenerateAudio")}
                </option>
                <option value="RenderVideo">
                  {t("project.production.jobType.RenderVideo")}
                </option>
                <option value="FinalVideoQa">
                  {t("project.production.jobType.FinalVideoQa")}
                </option>
              </select>
            </div>

            {/* 1. GenerateIdea Fields */}
            {selectedJobType === "GenerateIdea" ? (
              <>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="idea-provider"
                  >
                    {t("project.production.field.preferredProvider")}
                  </label>
                  <select
                    id="idea-provider"
                    className="select"
                    value={ideaProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "text.generation",
                        e.target.value,
                        setIdeaProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    {textGenerationProviders.map((pid) => (
                      <option key={pid} value={pid}>
                        {getProviderLabel(pid, t)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="idea-model">
                    {t("project.production.field.model")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <input
                    id="idea-model"
                    className="field__input mono"
                    type="text"
                    placeholder={t(
                      "project.production.field.modelPlaceholder",
                    )}
                    value={ideaModel}
                    onChange={(e) => setIdeaModel(e.target.value)}
                  />
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="idea-topic">
                    {t("project.production.field.topic")}
                  </label>
                  <input
                    id="idea-topic"
                    className="field__input"
                    type="text"
                    value={ideaTopic}
                    aria-invalid={
                      enqueueFormErrors.ideaTopic ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.ideaTopic
                        ? "idea-topic-error"
                        : undefined
                    }
                    onChange={(e) => setIdeaTopic(e.target.value)}
                  />
                  {enqueueFormErrors.ideaTopic ? (
                    <p className="field__error" id="idea-topic-error" role="alert">
                      {enqueueFormErrors.ideaTopic}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="idea-audience">
                    {t("project.production.field.targetAudience")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <input
                    id="idea-audience"
                    className="field__input"
                    type="text"
                    value={ideaAudience}
                    onChange={(e) => setIdeaAudience(e.target.value)}
                  />
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="idea-language">
                    {t("project.production.field.language")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <input
                    id="idea-language"
                    className="field__input"
                    type="text"
                    value={ideaLanguage}
                    onChange={(e) => setIdeaLanguage(e.target.value)}
                  />
                </div>
              </>
            ) : null}

            {/* 2. GenerateScript Fields */}
            {selectedJobType === "GenerateScript" ? (
              <>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="script-provider"
                  >
                    {t("project.production.field.preferredProvider")}
                  </label>
                  <select
                    id="script-provider"
                    className="select"
                    value={scriptProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "text.generation",
                        e.target.value,
                        setScriptProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    {textGenerationProviders.map((pid) => (
                      <option key={pid} value={pid}>
                        {getProviderLabel(pid, t)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-model">
                    {t("project.production.field.model")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <input
                    id="script-model"
                    className="field__input mono"
                    type="text"
                    placeholder={t(
                      "project.production.field.modelPlaceholder",
                    )}
                    value={scriptModel}
                    onChange={(e) => setScriptModel(e.target.value)}
                  />
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-title">
                    {t("project.production.field.title")}
                  </label>
                  <input
                    id="script-title"
                    className="field__input"
                    type="text"
                    value={scriptTitle}
                    aria-invalid={
                      enqueueFormErrors.scriptTitle ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.scriptTitle
                        ? "script-title-error"
                        : undefined
                    }
                    onChange={(e) => setScriptTitle(e.target.value)}
                  />
                  {enqueueFormErrors.scriptTitle ? (
                    <p className="field__error" id="script-title-error" role="alert">
                      {enqueueFormErrors.scriptTitle}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-hook">
                    {t("project.production.field.hook")}
                  </label>
                  <textarea
                    id="script-hook"
                    className="field__textarea"
                    rows={2}
                    value={scriptHook}
                    aria-invalid={
                      enqueueFormErrors.scriptHook ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.scriptHook
                        ? "script-hook-error"
                        : undefined
                    }
                    onChange={(e) => setScriptHook(e.target.value)}
                  />
                  {enqueueFormErrors.scriptHook ? (
                    <p className="field__error" id="script-hook-error" role="alert">
                      {enqueueFormErrors.scriptHook}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-summary">
                    {t("project.production.field.summary")}
                  </label>
                  <textarea
                    id="script-summary"
                    className="field__textarea"
                    rows={3}
                    value={scriptSummary}
                    aria-invalid={
                      enqueueFormErrors.scriptSummary ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.scriptSummary
                        ? "script-summary-error"
                        : undefined
                    }
                    onChange={(e) => setScriptSummary(e.target.value)}
                  />
                  {enqueueFormErrors.scriptSummary ? (
                    <p className="field__error" id="script-summary-error" role="alert">
                      {enqueueFormErrors.scriptSummary}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-angle">
                    {t("project.production.field.angle")}
                  </label>
                  <input
                    id="script-angle"
                    className="field__input"
                    type="text"
                    value={scriptAngle}
                    aria-invalid={
                      enqueueFormErrors.scriptAngle ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.scriptAngle
                        ? "script-angle-error"
                        : undefined
                    }
                    onChange={(e) => setScriptAngle(e.target.value)}
                  />
                  {enqueueFormErrors.scriptAngle ? (
                    <p className="field__error" id="script-angle-error" role="alert">
                      {enqueueFormErrors.scriptAngle}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-audience">
                    {t("project.production.field.targetAudience")}
                  </label>
                  <input
                    id="script-audience"
                    className="field__input"
                    type="text"
                    value={scriptAudience}
                    aria-invalid={
                      enqueueFormErrors.scriptAudience ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.scriptAudience
                        ? "script-audience-error"
                        : undefined
                    }
                    onChange={(e) => setScriptAudience(e.target.value)}
                  />
                  {enqueueFormErrors.scriptAudience ? (
                    <p className="field__error" id="script-audience-error" role="alert">
                      {enqueueFormErrors.scriptAudience}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-format">
                    {t("project.production.field.suggestedFormat")}
                  </label>
                  <input
                    id="script-format"
                    className="field__input"
                    type="text"
                    value={scriptFormat}
                    aria-invalid={
                      enqueueFormErrors.scriptFormat ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.scriptFormat
                        ? "script-format-error"
                        : undefined
                    }
                    onChange={(e) => setScriptFormat(e.target.value)}
                  />
                  {enqueueFormErrors.scriptFormat ? (
                    <p className="field__error" id="script-format-error" role="alert">
                      {enqueueFormErrors.scriptFormat}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="script-language">
                    {t("project.production.field.language")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <input
                    id="script-language"
                    className="field__input"
                    type="text"
                    value={scriptLanguage}
                    onChange={(e) => setScriptLanguage(e.target.value)}
                  />
                </div>
              </>
            ) : null}

            {/* 3. GenerateStoryboard Fields */}
            {selectedJobType === "GenerateStoryboard" ? (
              <>
                <p className="inline-note">
                  {t("project.production.enqueue.storyboardHint")}
                </p>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="storyboard-provider"
                  >
                    {t("project.production.field.preferredProvider")}
                  </label>
                  <select
                    id="storyboard-provider"
                    className="select"
                    value={storyboardProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "text.generation",
                        e.target.value,
                        setStoryboardProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    {textGenerationProviders.map((pid) => (
                      <option key={pid} value={pid}>
                        {getProviderLabel(pid, t)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="storyboard-notes">
                    {t("project.production.field.notes")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <textarea
                    id="storyboard-notes"
                    className="field__textarea"
                    rows={2}
                    value={storyboardNotes}
                    onChange={(e) => setStoryboardNotes(e.target.value)}
                  />
                </div>
              </>
            ) : null}

            {/* 4. GenerateSceneVisuals Fields */}
            {selectedJobType === "GenerateSceneVisuals" ? (
              <>
                {hasStoryboardScenesToCopy ? (
                  <div
                    style={{
                      display: "flex",
                      flexDirection: "column",
                      gap: "var(--space-1)",
                      padding: "var(--space-3)",
                      background: "var(--color-surface-muted)",
                      borderRadius: "var(--radius-md)",
                      marginBottom: "var(--space-2)",
                    }}
                  >
                    <div>
                      <Button
                        type="button"
                        variant="secondary"
                        size="sm"
                        onClick={handleCopyScenePrompts}
                      >
                        {t("project.production.copyScenePrompt")}
                      </Button>
                    </div>
                    <p className="inline-note">
                      {t("project.production.copyScenePromptHint")}
                    </p>
                  </div>
                ) : null}
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="visuals-production-recipe"
                  >
                    {t("project.production.field.productionRecipe")}
                  </label>
                  <select
                    id="visuals-production-recipe"
                    className="select"
                    value={visualsRecipe}
                    onChange={(e) => setVisualsRecipe(e.target.value)}
                  >
                    <option value="auto">
                      {t("project.production.recipe.auto")}
                    </option>
                    <option value="tech-explainer:1">
                      {t("project.production.recipe.techExplainerV1")}
                    </option>
                    <option value="motion-comic:1">
                      {t("project.production.recipe.motionComicV1")}
                    </option>
                    <option value="motion-comic:2">
                      {t("project.production.recipe.motionComicV2")}
                    </option>
                  </select>
                </div>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="visuals-image-provider"
                  >
                    {t("project.production.field.preferredImageProvider")}
                  </label>
                  <select
                    id="visuals-image-provider"
                    className="select"
                    value={visualsImageProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "visual.ai_image",
                        e.target.value,
                        setVisualsImageProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    {visualImageProviders.map((pid) => (
                      <option key={pid} value={pid}>
                        {getProviderLabel(pid, t)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="visuals-video-provider"
                  >
                    {t("project.production.field.preferredVideoProvider")}
                  </label>
                  <select
                    id="visuals-video-provider"
                    className="select"
                    value={visualsVideoProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "visual.character_animation",
                        e.target.value,
                        setVisualsVideoProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    {visualVideoProviders.map((pid) => (
                      <option key={pid} value={pid}>
                        {getProviderLabel(pid, t)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="visuals-storyboard-job-id"
                  >
                    {t("project.production.field.storyboardJobId")}
                  </label>
                  <input
                    id="visuals-storyboard-job-id"
                    className="field__input mono"
                    type="text"
                    value={visualsStoryboardJobId}
                    aria-invalid={
                      enqueueFormErrors.visualsStoryboardJobId
                        ? true
                        : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.visualsStoryboardJobId
                        ? "visuals-job-id-error"
                        : undefined
                    }
                    onChange={(e) =>
                      setVisualsStoryboardJobId(e.target.value)
                    }
                  />
                  {enqueueFormErrors.visualsStoryboardJobId ? (
                    <p className="field__error" id="visuals-job-id-error" role="alert">
                      {enqueueFormErrors.visualsStoryboardJobId}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: "var(--space-2)",
                      cursor: "pointer",
                      fontSize: "var(--text-sm)",
                    }}
                  >
                    <input
                      type="checkbox"
                      checked={visualsForce}
                      onChange={(e) => setVisualsForce(e.target.checked)}
                    />
                    {t("project.production.field.forceRegenerate")}
                  </label>
                </div>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="visuals-art-direction"
                  >
                    {t("project.production.field.artDirection")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <textarea
                    id="visuals-art-direction"
                    className="field__textarea"
                    rows={2}
                    value={visualsArtDirection}
                    onChange={(e) => setVisualsArtDirection(e.target.value)}
                  />
                </div>
              </>
            ) : null}

            {/* 5. GenerateAudio Fields */}
            {selectedJobType === "GenerateAudio" ? (
              <>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="audio-speech-provider"
                  >
                    {t("project.production.field.preferredSpeechProvider")}
                  </label>
                  <select
                    id="audio-speech-provider"
                    className="select"
                    value={audioSpeechProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "speech.narration",
                        e.target.value,
                        setAudioSpeechProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    {audioSpeechProviders.map((pid) => (
                      <option key={pid} value={pid}>
                        {getProviderLabel(pid, t)}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="audio-music-provider"
                  >
                    {t("project.production.field.preferredMusicProvider")}
                  </label>
                  <select
                    id="audio-music-provider"
                    className="select"
                    value={audioMusicProvider}
                    onChange={(e) =>
                      handleProviderSelect(
                        "music.instrumental",
                        e.target.value,
                        setAudioMusicProvider,
                      )
                    }
                  >
                    <option value="auto">
                      {t("project.production.provider.auto")}
                    </option>
                    <option value="ace-step">
                      {t("project.production.provider.aceStep")}
                    </option>
                  </select>
                </div>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="audio-storyboard-job-id"
                  >
                    {t("project.production.field.storyboardJobId")}
                  </label>
                  <input
                    id="audio-storyboard-job-id"
                    className="field__input mono"
                    type="text"
                    value={audioStoryboardJobId}
                    aria-invalid={
                      enqueueFormErrors.audioStoryboardJobId ? true : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.audioStoryboardJobId
                        ? "audio-job-id-error"
                        : undefined
                    }
                    onChange={(e) => setAudioStoryboardJobId(e.target.value)}
                  />
                  {enqueueFormErrors.audioStoryboardJobId ? (
                    <p className="field__error" id="audio-job-id-error" role="alert">
                      {enqueueFormErrors.audioStoryboardJobId}
                    </p>
                  ) : null}
                </div>
              </>
            ) : null}

            {/* 6. RenderVideo Fields */}
            {selectedJobType === "RenderVideo" ? (
              <>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="render-storyboard-job-id"
                  >
                    {t("project.production.field.storyboardJobId")}
                  </label>
                  <input
                    id="render-storyboard-job-id"
                    className="field__input mono"
                    type="text"
                    value={renderStoryboardJobId}
                    aria-invalid={
                      enqueueFormErrors.renderStoryboardJobId
                        ? true
                        : undefined
                    }
                    aria-describedby={
                      enqueueFormErrors.renderStoryboardJobId
                        ? "render-job-id-error"
                        : undefined
                    }
                    onChange={(e) =>
                      setRenderStoryboardJobId(e.target.value)
                    }
                  />
                  {enqueueFormErrors.renderStoryboardJobId ? (
                    <p className="field__error" id="render-job-id-error" role="alert">
                      {enqueueFormErrors.renderStoryboardJobId}
                    </p>
                  ) : null}
                </div>
                <div className="field">
                  <label className="field__label" htmlFor="render-variant">
                    {t("project.production.field.variant")}
                    <span className="field__optional">
                      {t("projects.form.optional")}
                    </span>
                  </label>
                  <input
                    id="render-variant"
                    className="field__input"
                    type="text"
                    placeholder={t(
                      "project.production.field.variantPlaceholder",
                    )}
                    value={renderVariant}
                    onChange={(e) => setRenderVariant(e.target.value)}
                  />
                </div>
              </>
            ) : null}

            {/* 7. FinalVideoQa Fields */}
            {selectedJobType === "FinalVideoQa" ? (
              <div className="field">
                <label className="field__label" htmlFor="qa-render-job-id">
                  {t("project.production.field.renderJobId")}
                </label>
                <input
                  id="qa-render-job-id"
                  className="field__input mono"
                  type="text"
                  value={qaRenderJobId}
                  aria-invalid={
                    enqueueFormErrors.qaRenderJobId ? true : undefined
                  }
                  aria-describedby={
                    enqueueFormErrors.qaRenderJobId
                      ? "qa-job-id-error"
                      : undefined
                  }
                  onChange={(e) => setQaRenderJobId(e.target.value)}
                />
                {enqueueFormErrors.qaRenderJobId ? (
                  <p className="field__error" id="qa-job-id-error" role="alert">
                    {enqueueFormErrors.qaRenderJobId}
                  </p>
                ) : null}
              </div>
            ) : null}

            <div style={{ marginTop: "var(--space-2)" }}>
              <Button
                type="submit"
                variant="primary"
                loading={enqueuing}
                loadingLabel={t("project.production.enqueue.submitting")}
              >
                {t("project.production.enqueue.submit")}
              </Button>
            </div>
          </form>
        </Card>
      </div>

      {/* Cancel Job Confirmation Dialog */}
      <Dialog
        open={cancelDialogOpen}
        title={t("project.production.cancelDialog.title")}
        description={t("project.production.cancelDialog.description")}
        onClose={() => setCancelDialogOpen(false)}
        dismissible={!cancelling}
      >
        <div className="form-stack">
          {cancelError ? (
            <InlineError
              title={t("project.production.cancelDialog.errorTitle")}
              detail={cancelError.detail ?? cancelError.title}
              code={errorCode(cancelError)}
            />
          ) : null}

          {inspectedJob ? (
            <p className="detail-prose">
              {t("project.production.cancelDialog.confirmPrompt", {
                id: inspectedJob.id,
              })}
            </p>
          ) : null}

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={() => setCancelDialogOpen(false)}
              disabled={cancelling}
            >
              {t("action.cancel")}
            </Button>
            <Button
              variant="danger"
              onClick={handleCancelJob}
              loading={cancelling}
              loadingLabel={t("project.production.cancelDialog.cancelling")}
            >
              {t("project.production.cancelDialog.confirm")}
            </Button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

interface JobResultSummaryProps {
  result: unknown;
  t: Translate;
  number: (val: number) => string;
  onCopyScenePrompt?: () => void;
}

function JobResultSummary({
  result,
  t,
  number,
  onCopyScenePrompt,
}: JobResultSummaryProps) {
  if (result === null || typeof result !== "object") {
    return <span className="mono">{String(result)}</span>;
  }

  const record = result as Record<string, unknown>;
  const outputPath =
    typeof record.outputPath === "string" ? record.outputPath : null;
  const durationSeconds =
    typeof record.durationSeconds === "number"
      ? record.durationSeconds
      : null;
  const width = typeof record.width === "number" ? record.width : null;
  const height = typeof record.height === "number" ? record.height : null;
  const byteSize =
    typeof record.byteSize === "number" ? record.byteSize : null;
  const title = typeof record.title === "string" ? record.title : null;
  const scenes = Array.isArray(record.scenes) ? record.scenes : null;
  const visuals = Array.isArray(record.visuals) ? record.visuals : null;
  const hasVideo =
    typeof record.hasVideo === "boolean" ? record.hasVideo : null;
  const hasAudio =
    typeof record.hasAudio === "boolean" ? record.hasAudio : null;
  const hasSubtitle =
    typeof record.hasSubtitle === "boolean" ? record.hasSubtitle : null;

  const hasSpecificFields =
    outputPath !== null ||
    durationSeconds !== null ||
    (width !== null && height !== null) ||
    byteSize !== null ||
    title !== null ||
    scenes !== null ||
    visuals !== null ||
    hasVideo !== null;

  return (
    <div className="stack-sm">
      {hasSpecificFields ? (
        <dl className="definition-list">
          {title ? (
            <div>
              <dt>{t("project.production.result.title")}</dt>
              <dd>{title}</dd>
            </div>
          ) : null}
          {outputPath ? (
            <div>
              <dt>{t("project.production.result.outputPath")}</dt>
              <dd className="mono">{outputPath}</dd>
            </div>
          ) : null}
          {durationSeconds !== null ? (
            <div>
              <dt>{t("project.production.result.duration")}</dt>
              <dd>
                {t("project.production.result.durationSeconds", {
                  seconds: durationSeconds,
                })}
              </dd>
            </div>
          ) : null}
          {width !== null && height !== null ? (
            <div>
              <dt>{t("project.production.result.resolution")}</dt>
              <dd>
                {width} × {height}
              </dd>
            </div>
          ) : null}
          {byteSize !== null ? (
            <div>
              <dt>{t("project.production.result.byteSize")}</dt>
              <dd>{number(byteSize)}</dd>
            </div>
          ) : null}
          {scenes ? (
            <div>
              <dt>{t("project.production.result.scenesCount")}</dt>
              <dd>{scenes.length}</dd>
            </div>
          ) : null}
          {visuals ? (
            <div>
              <dt>{t("project.production.result.visualsCount")}</dt>
              <dd>{visuals.length}</dd>
            </div>
          ) : null}
          {hasVideo !== null ? (
            <div>
              <dt>{t("project.production.result.qaChecks")}</dt>
              <dd>
                {hasVideo
                  ? t("project.production.result.videoPassed")
                  : t("project.production.result.videoFailed")}
                {" · "}
                {hasAudio
                  ? t("project.production.result.audioPassed")
                  : t("project.production.result.audioFailed")}
                {" · "}
                {hasSubtitle
                  ? t("project.production.result.subtitlePassed")
                  : t("project.production.result.subtitleMissing")}
              </dd>
            </div>
          ) : null}
        </dl>
      ) : null}

      {scenes && scenes.length > 0 && onCopyScenePrompt ? (
        <div style={{ marginTop: "var(--space-2)" }}>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={onCopyScenePrompt}
          >
            {t("project.production.copyScenePrompt")}
          </Button>
        </div>
      ) : null}

      <details>
        <summary
          style={{
            cursor: "pointer",
            fontSize: "var(--text-xs)",
            color: "var(--color-text-subtle)",
          }}
        >
          {t("project.production.result.rawJson")}
        </summary>
        <pre
          className="mono inline-note"
          style={{
            marginTop: "var(--space-2)",
            maxHeight: "200px",
            overflowX: "auto",
            padding: "var(--space-2) var(--space-3)",
            background: "var(--color-surface-muted)",
            borderRadius: "var(--radius-sm)",
          }}
        >
          {JSON.stringify(result, null, 2)}
        </pre>
      </details>
    </div>
  );
}
