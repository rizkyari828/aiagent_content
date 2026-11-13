import {
  useCallback,
  useEffect,
  useState,
  type FormEvent,
} from "react";
import { isApiRequestError, type ApiError } from "../../api/client";
import {
  getProjectNarration,
  getProjectSubtitle,
  getSubtitleContent,
  listProjectAssets,
  registerProjectNarration,
  registerProjectSubtitle,
  registerSceneAsset,
  updateSubtitleContent,
  type NarrationTrackResponse,
  type SceneAssetItem,
  type SubtitleTrackResponse,
} from "../../api/media";
import { Badge } from "../../components/Badge";
import { Button } from "../../components/Button";
import { Card } from "../../components/Card";
import { Dialog } from "../../components/Dialog";
import { InlineError } from "../../components/InlineError";
import { Skeleton } from "../../components/Skeleton";
import { useToast } from "../../components/Toast";
import { useFormatters } from "../../i18n/format";
import { useI18n, type Translate } from "../../i18n/i18n";

export interface AssetsAudioSectionProps {
  projectId: string;
  onProjectChanged: () => void;
}

type LoadState = "loading" | "ready" | "error";

interface AssetFormErrors {
  storyboardJobId?: string;
  sceneIndex?: string;
  path?: string;
  source?: string;
  license?: string;
}

interface TrackFormErrors {
  storyboardJobId?: string;
  path?: string;
  source?: string;
  license?: string;
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
    return t("project.assets.error.network");
  }

  if (error.code === "request_timeout") {
    return t("project.assets.error.timeout");
  }

  return t("project.assets.error.http");
}

function errorCode(error: ApiError): string | undefined {
  if (error.code) {
    return error.code;
  }

  return error.status > 0 ? `HTTP ${error.status}` : undefined;
}

export function AssetsAudioSection({
  projectId,
  onProjectChanged,
}: AssetsAudioSectionProps) {
  const { t } = useI18n();
  const { dateTime, number } = useFormatters();
  const { showToast } = useToast();

  const [assets, setAssets] = useState<SceneAssetItem[]>([]);
  const [narration, setNarration] = useState<NarrationTrackResponse | null>(null);
  const [subtitle, setSubtitle] = useState<SubtitleTrackResponse | null>(null);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  // Scene Asset Dialog State
  const [assetDialogOpen, setAssetDialogOpen] = useState(false);
  const [assetJobId, setAssetJobId] = useState("");
  const [assetSceneIndex, setAssetSceneIndex] = useState("0");
  const [assetType, setAssetType] = useState<"image" | "video">("image");
  const [assetPath, setAssetPath] = useState("");
  const [assetOrigin, setAssetOrigin] = useState<"generated" | "imported">("generated");
  const [assetSource, setAssetSource] = useState("");
  const [assetCreator, setAssetCreator] = useState("");
  const [assetLicense, setAssetLicense] = useState("");
  const [assetRetrievedAt, setAssetRetrievedAt] = useState("");
  const [assetFormErrors, setAssetFormErrors] = useState<AssetFormErrors>({});
  const [assetApiError, setAssetApiError] = useState<ApiError | null>(null);
  const [submittingAsset, setSubmittingAsset] = useState(false);

  // Narration Dialog State
  const [narrationDialogOpen, setNarrationDialogOpen] = useState(false);
  const [narrationJobId, setNarrationJobId] = useState("");
  const [narrationPath, setNarrationPath] = useState("");
  const [narrationOrigin, setNarrationOrigin] = useState<"generated" | "imported">("generated");
  const [narrationSource, setNarrationSource] = useState("");
  const [narrationCreator, setNarrationCreator] = useState("");
  const [narrationLicense, setNarrationLicense] = useState("");
  const [narrationRetrievedAt, setNarrationRetrievedAt] = useState("");
  const [narrationFormErrors, setNarrationFormErrors] = useState<TrackFormErrors>({});
  const [narrationApiError, setNarrationApiError] = useState<ApiError | null>(null);
  const [submittingNarration, setSubmittingNarration] = useState(false);

  // Subtitle Dialog State
  const [subtitleDialogOpen, setSubtitleDialogOpen] = useState(false);
  const [subtitleJobId, setSubtitleJobId] = useState("");
  const [subtitlePath, setSubtitlePath] = useState("");
  const [subtitleOrigin, setSubtitleOrigin] = useState<"generated" | "imported">("generated");
  const [subtitleSource, setSubtitleSource] = useState("");
  const [subtitleCreator, setSubtitleCreator] = useState("");
  const [subtitleLicense, setSubtitleLicense] = useState("");
  const [subtitleRetrievedAt, setSubtitleRetrievedAt] = useState("");
  const [subtitleFormErrors, setSubtitleFormErrors] = useState<TrackFormErrors>({});
  const [subtitleApiError, setSubtitleApiError] = useState<ApiError | null>(null);
  const [submittingSubtitle, setSubmittingSubtitle] = useState(false);

  // Subtitle Editor State
  const [subtitleContent, setSubtitleContent] = useState("");
  const [subtitleLoaded, setSubtitleLoaded] = useState(false);
  const [loadingSubtitleContent, setLoadingSubtitleContent] = useState(false);
  const [savingSubtitleContent, setSavingSubtitleContent] = useState(false);
  const [subtitleEditorError, setSubtitleEditorError] = useState<ApiError | null>(null);

  const refresh = useCallback(() => {
    setReloadToken((token) => token + 1);
  }, []);

  useEffect(() => {
    const controller = new AbortController();

    setLoadState("loading");
    setLoadError(null);
    setSubtitleLoaded(false);
    setSubtitleContent("");
    setSubtitleEditorError(null);

    Promise.all([
      listProjectAssets(projectId, controller.signal),
      getProjectNarration(projectId, controller.signal).catch((err: unknown) => {
        if (
          isApiRequestError(err) &&
          (err.status === 404 || err.code === "narration_not_found")
        ) {
          return null;
        }
        throw err;
      }),
      getProjectSubtitle(projectId, controller.signal).catch((err: unknown) => {
        if (
          isApiRequestError(err) &&
          (err.status === 404 || err.code === "subtitle_not_found")
        ) {
          return null;
        }
        throw err;
      }),
    ])
      .then(([nextAssets, nextNarration, nextSubtitle]) => {
        if (controller.signal.aborted) {
          return;
        }

        setAssets(nextAssets);
        setNarration(nextNarration);
        setSubtitle(nextSubtitle);
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
  }, [projectId, reloadToken]);

  const openAssetDialog = useCallback(() => {
    setAssetJobId("");
    setAssetSceneIndex("0");
    setAssetType("image");
    setAssetPath("");
    setAssetOrigin("generated");
    setAssetSource("");
    setAssetCreator("");
    setAssetLicense("");
    setAssetRetrievedAt("");
    setAssetFormErrors({});
    setAssetApiError(null);
    setSubmittingAsset(false);
    setAssetDialogOpen(true);
  }, []);

  const closeAssetDialog = useCallback(() => {
    if (submittingAsset) {
      return;
    }
    setAssetDialogOpen(false);
    setAssetFormErrors({});
    setAssetApiError(null);
  }, [submittingAsset]);

  const handleRegisterAsset = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submittingAsset) {
      return;
    }

    const trimmedJobId = assetJobId.trim();
    const parsedSceneIndex = Number.parseInt(assetSceneIndex, 10);
    const trimmedPath = assetPath.trim();
    const trimmedSource = assetSource.trim();
    const trimmedCreator = assetCreator.trim();
    const trimmedLicense = assetLicense.trim();
    const trimmedRetrievedAt = assetRetrievedAt.trim();

    const errors: AssetFormErrors = {};

    if (!trimmedJobId) {
      errors.storyboardJobId = t("project.assets.validation.storyboardJobIdRequired");
    }

    if (Number.isNaN(parsedSceneIndex) || parsedSceneIndex < 0) {
      errors.sceneIndex = t("project.assets.validation.sceneIndexInvalid");
    }

    if (!trimmedPath) {
      errors.path = t("project.assets.validation.pathRequired");
    }

    if (assetOrigin === "imported") {
      if (!trimmedSource) {
        errors.source = t("project.assets.validation.sourceRequired");
      }
      if (!trimmedLicense) {
        errors.license = t("project.assets.validation.licenseRequired");
      }
    }

    setAssetFormErrors(errors);
    setAssetApiError(null);

    if (Object.keys(errors).length > 0) {
      return;
    }

    setSubmittingAsset(true);

    try {
      await registerSceneAsset(projectId, trimmedJobId, {
        sceneIndex: parsedSceneIndex,
        type: assetType,
        path: trimmedPath,
        origin: assetOrigin,
        source: trimmedSource || null,
        creator: trimmedCreator || null,
        license: trimmedLicense || null,
        retrievedAt: trimmedRetrievedAt || null,
      });

      setSubmittingAsset(false);
      setAssetDialogOpen(false);
      showToast(t("project.assets.toast.assetRegistered"), "success");
      refresh();
      onProjectChanged();
    } catch (err: unknown) {
      setSubmittingAsset(false);
      setAssetApiError(toApiError(err));
    }
  };

  const openNarrationDialog = useCallback(() => {
    setNarrationJobId(narration?.sourceJobId ?? "");
    setNarrationPath(narration?.path ?? "");
    setNarrationOrigin(
      narration?.origin === "imported" ? "imported" : "generated",
    );
    setNarrationSource(narration?.source ?? "");
    setNarrationCreator(narration?.creator ?? "");
    setNarrationLicense(narration?.license ?? "");
    setNarrationRetrievedAt(narration?.retrievedAt ?? "");
    setNarrationFormErrors({});
    setNarrationApiError(null);
    setSubmittingNarration(false);
    setNarrationDialogOpen(true);
  }, [narration]);

  const closeNarrationDialog = useCallback(() => {
    if (submittingNarration) {
      return;
    }
    setNarrationDialogOpen(false);
    setNarrationFormErrors({});
    setNarrationApiError(null);
  }, [submittingNarration]);

  const handleRegisterNarration = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submittingNarration) {
      return;
    }

    const trimmedJobId = narrationJobId.trim();
    const trimmedPath = narrationPath.trim();
    const trimmedSource = narrationSource.trim();
    const trimmedCreator = narrationCreator.trim();
    const trimmedLicense = narrationLicense.trim();
    const trimmedRetrievedAt = narrationRetrievedAt.trim();

    const errors: TrackFormErrors = {};

    if (!trimmedJobId) {
      errors.storyboardJobId = t("project.assets.validation.storyboardJobIdRequired");
    }

    if (!trimmedPath) {
      errors.path = t("project.assets.validation.pathRequired");
    }

    if (narrationOrigin === "imported") {
      if (!trimmedSource) {
        errors.source = t("project.assets.validation.sourceRequired");
      }
      if (!trimmedLicense) {
        errors.license = t("project.assets.validation.licenseRequired");
      }
    }

    setNarrationFormErrors(errors);
    setNarrationApiError(null);

    if (Object.keys(errors).length > 0) {
      return;
    }

    setSubmittingNarration(true);

    try {
      await registerProjectNarration(projectId, trimmedJobId, {
        path: trimmedPath,
        origin: narrationOrigin,
        source: trimmedSource || null,
        creator: trimmedCreator || null,
        license: trimmedLicense || null,
        retrievedAt: trimmedRetrievedAt || null,
      });

      setSubmittingNarration(false);
      setNarrationDialogOpen(false);
      showToast(t("project.assets.toast.narrationRegistered"), "success");
      refresh();
      onProjectChanged();
    } catch (err: unknown) {
      setSubmittingNarration(false);
      setNarrationApiError(toApiError(err));
    }
  };

  const openSubtitleDialog = useCallback(() => {
    setSubtitleJobId(subtitle?.sourceJobId ?? "");
    setSubtitlePath(subtitle?.path ?? "");
    setSubtitleOrigin(
      subtitle?.origin === "imported" ? "imported" : "generated",
    );
    setSubtitleSource(subtitle?.source ?? "");
    setSubtitleCreator(subtitle?.creator ?? "");
    setSubtitleLicense(subtitle?.license ?? "");
    setSubtitleRetrievedAt(subtitle?.retrievedAt ?? "");
    setSubtitleFormErrors({});
    setSubtitleApiError(null);
    setSubmittingSubtitle(false);
    setSubtitleDialogOpen(true);
  }, [subtitle]);

  const closeSubtitleDialog = useCallback(() => {
    if (submittingSubtitle) {
      return;
    }
    setSubtitleDialogOpen(false);
    setSubtitleFormErrors({});
    setSubtitleApiError(null);
  }, [submittingSubtitle]);

  const handleRegisterSubtitle = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submittingSubtitle) {
      return;
    }

    const trimmedJobId = subtitleJobId.trim();
    const trimmedPath = subtitlePath.trim();
    const trimmedSource = subtitleSource.trim();
    const trimmedCreator = subtitleCreator.trim();
    const trimmedLicense = subtitleLicense.trim();
    const trimmedRetrievedAt = subtitleRetrievedAt.trim();

    const errors: TrackFormErrors = {};

    if (!trimmedJobId) {
      errors.storyboardJobId = t("project.assets.validation.storyboardJobIdRequired");
    }

    if (!trimmedPath) {
      errors.path = t("project.assets.validation.pathRequired");
    }

    if (subtitleOrigin === "imported") {
      if (!trimmedSource) {
        errors.source = t("project.assets.validation.sourceRequired");
      }
      if (!trimmedLicense) {
        errors.license = t("project.assets.validation.licenseRequired");
      }
    }

    setSubtitleFormErrors(errors);
    setSubtitleApiError(null);

    if (Object.keys(errors).length > 0) {
      return;
    }

    setSubmittingSubtitle(true);

    try {
      await registerProjectSubtitle(projectId, trimmedJobId, {
        path: trimmedPath,
        origin: subtitleOrigin,
        source: trimmedSource || null,
        creator: trimmedCreator || null,
        license: trimmedLicense || null,
        retrievedAt: trimmedRetrievedAt || null,
      });

      setSubmittingSubtitle(false);
      setSubtitleDialogOpen(false);
      showToast(t("project.assets.toast.subtitleRegistered"), "success");
      refresh();
      onProjectChanged();
    } catch (err: unknown) {
      setSubmittingSubtitle(false);
      setSubtitleApiError(toApiError(err));
    }
  };

  const handleLoadSubtitleContent = async () => {
    setLoadingSubtitleContent(true);
    setSubtitleEditorError(null);
    try {
      const res = await getSubtitleContent(projectId);
      setSubtitleContent(res.content);
      setSubtitleLoaded(true);
    } catch (err: unknown) {
      setSubtitleEditorError(toApiError(err));
    } finally {
      setLoadingSubtitleContent(false);
    }
  };

  const handleSaveSubtitleContent = async (event: FormEvent) => {
    event.preventDefault();
    setSavingSubtitleContent(true);
    setSubtitleEditorError(null);
    try {
      await updateSubtitleContent(projectId, subtitleContent);
      showToast(t("project.assets.toast.subtitleContentSaved"), "success");
      refresh();
      onProjectChanged();
    } catch (err: unknown) {
      setSubtitleEditorError(toApiError(err));
    } finally {
      setSavingSubtitleContent(false);
    }
  };

  return (
    <div className="stack">
      {loadState === "loading" ? <AssetsSkeleton /> : null}

      {loadState === "error" && loadError ? (
        <InlineError
          title={t("project.assets.error.title")}
          detail={describeLoadError(loadError, t)}
          code={errorCode(loadError)}
          actionLabel={t("action.retry")}
          onAction={refresh}
        />
      ) : null}

      {loadState === "ready" ? (
        <>
          <Card
            title={t("project.assets.title")}
            description={t("project.assets.description")}
            actions={
              <Button variant="secondary" onClick={openAssetDialog}>
                {t("project.assets.action.registerAsset")}
              </Button>
            }
          >
            {assets.length === 0 ? (
              <p className="inline-note">{t("project.assets.empty")}</p>
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <caption className="visually-hidden">
                    {t("project.assets.table.caption")}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{t("project.assets.table.sceneIndex")}</th>
                      <th scope="col">{t("project.assets.table.type")}</th>
                      <th scope="col">{t("project.assets.table.path")}</th>
                      <th scope="col">{t("project.assets.table.origin")}</th>
                      <th scope="col" className="table__align-end">
                        {t("project.assets.table.byteSize")}
                      </th>
                      <th scope="col">{t("project.assets.table.sourceJobId")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {assets.map((asset) => (
                      <tr key={asset.id}>
                        <td>#{asset.sceneIndex}</td>
                        <td>
                          <Badge
                            tone={asset.type === "video" ? "accent" : "neutral"}
                          >
                            {asset.type}
                          </Badge>
                        </td>
                        <td className="mono">{asset.path}</td>
                        <td>
                          <Badge
                            tone={
                              asset.origin === "generated"
                                ? "success"
                                : "neutral"
                            }
                          >
                            {asset.origin}
                          </Badge>
                        </td>
                        <td className="table__align-end">
                          {number(asset.byteSize)}
                        </td>
                        <td className="mono">{asset.sourceJobId}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <div className="detail-grid">
            <Card
              title={t("project.assets.narration.title")}
              description={t("project.assets.narration.description")}
              actions={
                <Button variant="secondary" onClick={openNarrationDialog}>
                  {narration
                    ? t("project.assets.narration.action.replace")
                    : t("project.assets.narration.action.register")}
                </Button>
              }
            >
              {narration ? (
                <dl className="definition-list">
                  <div>
                    <dt>{t("project.assets.track.path")}</dt>
                    <dd className="mono">{narration.path}</dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.byteSize")}</dt>
                    <dd>{number(narration.byteSize)}</dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.origin")}</dt>
                    <dd>
                      <Badge
                        tone={
                          narration.origin === "generated"
                            ? "success"
                            : "neutral"
                        }
                      >
                        {narration.origin}
                      </Badge>
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.sourceJobId")}</dt>
                    <dd className="mono">{narration.sourceJobId}</dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.createdAt")}</dt>
                    <dd>
                      <time dateTime={narration.createdAt}>
                        {dateTime(narration.createdAt)}
                      </time>
                    </dd>
                  </div>
                  {narration.source ? (
                    <div>
                      <dt>{t("project.assets.provenance.source")}</dt>
                      <dd>{narration.source}</dd>
                    </div>
                  ) : null}
                  {narration.creator ? (
                    <div>
                      <dt>{t("project.assets.provenance.creator")}</dt>
                      <dd>{narration.creator}</dd>
                    </div>
                  ) : null}
                  {narration.license ? (
                    <div>
                      <dt>{t("project.assets.provenance.license")}</dt>
                      <dd>{narration.license}</dd>
                    </div>
                  ) : null}
                </dl>
              ) : (
                <p className="inline-note">{t("project.assets.narration.empty")}</p>
              )}
            </Card>

            <Card
              title={t("project.assets.subtitle.title")}
              description={t("project.assets.subtitle.description")}
              actions={
                <Button variant="secondary" onClick={openSubtitleDialog}>
                  {subtitle
                    ? t("project.assets.subtitle.action.replace")
                    : t("project.assets.subtitle.action.register")}
                </Button>
              }
            >
              {subtitle ? (
                <>
                  <dl className="definition-list">
                  <div>
                    <dt>{t("project.assets.track.path")}</dt>
                    <dd className="mono">{subtitle.path}</dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.byteSize")}</dt>
                    <dd>{number(subtitle.byteSize)}</dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.origin")}</dt>
                    <dd>
                      <Badge
                        tone={
                          subtitle.origin === "generated"
                            ? "success"
                            : "neutral"
                        }
                      >
                        {subtitle.origin}
                      </Badge>
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.sourceJobId")}</dt>
                    <dd className="mono">{subtitle.sourceJobId}</dd>
                  </div>
                  <div>
                    <dt>{t("project.assets.track.createdAt")}</dt>
                    <dd>
                      <time dateTime={subtitle.createdAt}>
                        {dateTime(subtitle.createdAt)}
                      </time>
                    </dd>
                  </div>
                  {subtitle.source ? (
                    <div>
                      <dt>{t("project.assets.provenance.source")}</dt>
                      <dd>{subtitle.source}</dd>
                    </div>
                  ) : null}
                  {subtitle.creator ? (
                    <div>
                      <dt>{t("project.assets.provenance.creator")}</dt>
                      <dd>{subtitle.creator}</dd>
                    </div>
                  ) : null}
                  {subtitle.license ? (
                    <div>
                      <dt>{t("project.assets.provenance.license")}</dt>
                      <dd>{subtitle.license}</dd>
                    </div>
                  ) : null}
                </dl>
                <details
                  className="subtitle-editor"
                  style={{
                    marginTop: "var(--space-4)",
                    paddingTop: "var(--space-3)",
                    borderTop: "1px solid var(--border-color, rgba(255, 255, 255, 0.1))",
                  }}
                >
                  <summary style={{ cursor: "pointer", fontWeight: 500 }}>
                    {t("project.assets.subtitle.editorToggle")}
                  </summary>
                  <div className="stack-sm" style={{ marginTop: "var(--space-3)" }}>
                    {subtitleEditorError ? (
                      <InlineError
                        title={t("project.assets.subtitle.editorErrorTitle")}
                        detail={
                          subtitleEditorError.detail ?? subtitleEditorError.title
                        }
                        code={errorCode(subtitleEditorError)}
                      />
                    ) : null}
                    {!subtitleLoaded ? (
                      <div>
                        <Button
                          type="button"
                          variant="secondary"
                          size="sm"
                          onClick={handleLoadSubtitleContent}
                          loading={loadingSubtitleContent}
                          loadingLabel={t("project.assets.subtitle.loadingContent")}
                        >
                          {t("project.assets.subtitle.loadContent")}
                        </Button>
                      </div>
                    ) : (
                      <form onSubmit={handleSaveSubtitleContent} className="stack-sm">
                        <div className="field">
                          <label
                            className="field__label"
                            htmlFor="subtitle-content-editor"
                          >
                            {t("project.assets.subtitle.contentLabel")}
                          </label>
                          <textarea
                            id="subtitle-content-editor"
                            className="field__textarea mono"
                            rows={10}
                            value={subtitleContent}
                            onChange={(e) => setSubtitleContent(e.target.value)}
                          />
                        </div>
                        <div style={{ display: "flex", gap: "var(--space-2)" }}>
                          <Button
                            type="submit"
                            variant="primary"
                            size="sm"
                            loading={savingSubtitleContent}
                            loadingLabel={t("project.assets.subtitle.saving")}
                            disabled={savingSubtitleContent}
                          >
                            {t("project.assets.subtitle.saveContent")}
                          </Button>
                          <Button
                            type="button"
                            variant="secondary"
                            size="sm"
                            onClick={handleLoadSubtitleContent}
                            loading={loadingSubtitleContent}
                            loadingLabel={t("project.assets.subtitle.loadingContent")}
                            disabled={savingSubtitleContent}
                          >
                            {t("project.assets.subtitle.reloadContent")}
                          </Button>
                        </div>
                      </form>
                    )}
                  </div>
                </details>
              </>
            ) : (
              <p className="inline-note">{t("project.assets.subtitle.empty")}</p>
            )}
            </Card>
          </div>
        </>
      ) : null}

      {/* Dialog for Registering Scene Asset */}
      <Dialog
        open={assetDialogOpen}
        title={t("project.assets.dialog.registerAssetTitle")}
        description={t("project.assets.dialog.registerAssetDescription")}
        onClose={closeAssetDialog}
        dismissible={!submittingAsset}
      >
        <form className="form-stack" onSubmit={handleRegisterAsset} noValidate>
          {assetApiError ? (
            <InlineError
              title={t("project.assets.form.errorTitle")}
              detail={assetApiError.detail ?? assetApiError.title}
              code={errorCode(assetApiError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="asset-job-id">
              {t("project.assets.form.storyboardJobId")}
            </label>
            <input
              id="asset-job-id"
              data-autofocus
              className="field__input mono"
              type="text"
              value={assetJobId}
              autoComplete="off"
              aria-invalid={assetFormErrors.storyboardJobId ? true : undefined}
              aria-describedby={
                assetFormErrors.storyboardJobId
                  ? "asset-job-id-error"
                  : "asset-job-id-hint"
              }
              onChange={(e) => setAssetJobId(e.target.value)}
            />
            {assetFormErrors.storyboardJobId ? (
              <p className="field__error" id="asset-job-id-error">
                {assetFormErrors.storyboardJobId}
              </p>
            ) : (
              <p className="field__hint" id="asset-job-id-hint">
                {t("project.assets.form.storyboardJobIdHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-scene-index">
              {t("project.assets.form.sceneIndex")}
            </label>
            <input
              id="asset-scene-index"
              className="field__input"
              type="number"
              min="0"
              step="1"
              value={assetSceneIndex}
              aria-invalid={assetFormErrors.sceneIndex ? true : undefined}
              aria-describedby={
                assetFormErrors.sceneIndex
                  ? "asset-scene-index-error"
                  : "asset-scene-index-hint"
              }
              onChange={(e) => setAssetSceneIndex(e.target.value)}
            />
            {assetFormErrors.sceneIndex ? (
              <p className="field__error" id="asset-scene-index-error">
                {assetFormErrors.sceneIndex}
              </p>
            ) : (
              <p className="field__hint" id="asset-scene-index-hint">
                {t("project.assets.form.sceneIndexHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-type">
              {t("project.assets.form.type")}
            </label>
            <select
              id="asset-type"
              className="select"
              value={assetType}
              onChange={(e) => setAssetType(e.target.value as "image" | "video")}
            >
              <option value="image">{t("project.assets.form.typeImage")}</option>
              <option value="video">{t("project.assets.form.typeVideo")}</option>
            </select>
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-path">
              {t("project.assets.form.path")}
            </label>
            <input
              id="asset-path"
              className="field__input mono"
              type="text"
              value={assetPath}
              autoComplete="off"
              aria-invalid={assetFormErrors.path ? true : undefined}
              aria-describedby={
                assetFormErrors.path ? "asset-path-error" : "asset-path-hint"
              }
              onChange={(e) => setAssetPath(e.target.value)}
            />
            {assetFormErrors.path ? (
              <p className="field__error" id="asset-path-error">
                {assetFormErrors.path}
              </p>
            ) : (
              <p className="field__hint" id="asset-path-hint">
                {t("project.assets.form.pathHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-origin">
              {t("project.assets.form.origin")}
            </label>
            <select
              id="asset-origin"
              className="select"
              value={assetOrigin}
              onChange={(e) =>
                setAssetOrigin(e.target.value as "generated" | "imported")
              }
            >
              <option value="generated">
                {t("project.assets.form.originGenerated")}
              </option>
              <option value="imported">
                {t("project.assets.form.originImported")}
              </option>
            </select>
            {assetOrigin === "imported" ? (
              <p className="field__hint">{t("project.assets.provenanceHint")}</p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-source">
              {t("project.assets.form.source")}
              {assetOrigin === "imported" ? null : (
                <span className="field__optional">
                  {t("projects.form.optional")}
                </span>
              )}
            </label>
            <input
              id="asset-source"
              className="field__input"
              type="text"
              value={assetSource}
              aria-invalid={assetFormErrors.source ? true : undefined}
              aria-describedby={
                assetFormErrors.source ? "asset-source-error" : undefined
              }
              onChange={(e) => setAssetSource(e.target.value)}
            />
            {assetFormErrors.source ? (
              <p className="field__error" id="asset-source-error">
                {assetFormErrors.source}
              </p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-creator">
              {t("project.assets.form.creator")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <input
              id="asset-creator"
              className="field__input"
              type="text"
              value={assetCreator}
              onChange={(e) => setAssetCreator(e.target.value)}
            />
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-license">
              {t("project.assets.form.license")}
              {assetOrigin === "imported" ? null : (
                <span className="field__optional">
                  {t("projects.form.optional")}
                </span>
              )}
            </label>
            <input
              id="asset-license"
              className="field__input"
              type="text"
              value={assetLicense}
              aria-invalid={assetFormErrors.license ? true : undefined}
              aria-describedby={
                assetFormErrors.license ? "asset-license-error" : undefined
              }
              onChange={(e) => setAssetLicense(e.target.value)}
            />
            {assetFormErrors.license ? (
              <p className="field__error" id="asset-license-error">
                {assetFormErrors.license}
              </p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="asset-retrieved-at">
              {t("project.assets.form.retrievedAt")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <input
              id="asset-retrieved-at"
              className="field__input"
              type="text"
              value={assetRetrievedAt}
              onChange={(e) => setAssetRetrievedAt(e.target.value)}
            />
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeAssetDialog}
              disabled={submittingAsset}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingAsset}
              loadingLabel={t("project.assets.form.submitting")}
            >
              {t("project.assets.form.submitAsset")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Dialog for Registering/Replacing Narration */}
      <Dialog
        open={narrationDialogOpen}
        title={
          narration
            ? t("project.assets.dialog.replaceNarrationTitle")
            : t("project.assets.dialog.registerNarrationTitle")
        }
        description={t("project.assets.dialog.registerNarrationDescription")}
        onClose={closeNarrationDialog}
        dismissible={!submittingNarration}
      >
        <form className="form-stack" onSubmit={handleRegisterNarration} noValidate>
          {narrationApiError ? (
            <InlineError
              title={t("project.assets.form.errorTitle")}
              detail={narrationApiError.detail ?? narrationApiError.title}
              code={errorCode(narrationApiError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="narration-job-id">
              {t("project.assets.form.storyboardJobId")}
            </label>
            <input
              id="narration-job-id"
              data-autofocus
              className="field__input mono"
              type="text"
              value={narrationJobId}
              autoComplete="off"
              aria-invalid={
                narrationFormErrors.storyboardJobId ? true : undefined
              }
              aria-describedby={
                narrationFormErrors.storyboardJobId
                  ? "narration-job-id-error"
                  : "narration-job-id-hint"
              }
              onChange={(e) => setNarrationJobId(e.target.value)}
            />
            {narrationFormErrors.storyboardJobId ? (
              <p className="field__error" id="narration-job-id-error">
                {narrationFormErrors.storyboardJobId}
              </p>
            ) : (
              <p className="field__hint" id="narration-job-id-hint">
                {t("project.assets.form.storyboardJobIdHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="narration-path">
              {t("project.assets.form.path")}
            </label>
            <input
              id="narration-path"
              className="field__input mono"
              type="text"
              value={narrationPath}
              autoComplete="off"
              aria-invalid={narrationFormErrors.path ? true : undefined}
              aria-describedby={
                narrationFormErrors.path
                  ? "narration-path-error"
                  : "narration-path-hint"
              }
              onChange={(e) => setNarrationPath(e.target.value)}
            />
            {narrationFormErrors.path ? (
              <p className="field__error" id="narration-path-error">
                {narrationFormErrors.path}
              </p>
            ) : (
              <p className="field__hint" id="narration-path-hint">
                {t("project.assets.form.pathHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="narration-origin">
              {t("project.assets.form.origin")}
            </label>
            <select
              id="narration-origin"
              className="select"
              value={narrationOrigin}
              onChange={(e) =>
                setNarrationOrigin(e.target.value as "generated" | "imported")
              }
            >
              <option value="generated">
                {t("project.assets.form.originGenerated")}
              </option>
              <option value="imported">
                {t("project.assets.form.originImported")}
              </option>
            </select>
            {narrationOrigin === "imported" ? (
              <p className="field__hint">{t("project.assets.provenanceHint")}</p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="narration-source">
              {t("project.assets.form.source")}
              {narrationOrigin === "imported" ? null : (
                <span className="field__optional">
                  {t("projects.form.optional")}
                </span>
              )}
            </label>
            <input
              id="narration-source"
              className="field__input"
              type="text"
              value={narrationSource}
              aria-invalid={narrationFormErrors.source ? true : undefined}
              aria-describedby={
                narrationFormErrors.source ? "narration-source-error" : undefined
              }
              onChange={(e) => setNarrationSource(e.target.value)}
            />
            {narrationFormErrors.source ? (
              <p className="field__error" id="narration-source-error">
                {narrationFormErrors.source}
              </p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="narration-creator">
              {t("project.assets.form.creator")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <input
              id="narration-creator"
              className="field__input"
              type="text"
              value={narrationCreator}
              onChange={(e) => setNarrationCreator(e.target.value)}
            />
          </div>

          <div className="field">
            <label className="field__label" htmlFor="narration-license">
              {t("project.assets.form.license")}
              {narrationOrigin === "imported" ? null : (
                <span className="field__optional">
                  {t("projects.form.optional")}
                </span>
              )}
            </label>
            <input
              id="narration-license"
              className="field__input"
              type="text"
              value={narrationLicense}
              aria-invalid={narrationFormErrors.license ? true : undefined}
              aria-describedby={
                narrationFormErrors.license ? "narration-license-error" : undefined
              }
              onChange={(e) => setNarrationLicense(e.target.value)}
            />
            {narrationFormErrors.license ? (
              <p className="field__error" id="narration-license-error">
                {narrationFormErrors.license}
              </p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="narration-retrieved-at">
              {t("project.assets.form.retrievedAt")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <input
              id="narration-retrieved-at"
              className="field__input"
              type="text"
              value={narrationRetrievedAt}
              onChange={(e) => setNarrationRetrievedAt(e.target.value)}
            />
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeNarrationDialog}
              disabled={submittingNarration}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingNarration}
              loadingLabel={t("project.assets.form.submitting")}
            >
              {narration
                ? t("project.assets.form.submitReplaceNarration")
                : t("project.assets.form.submitNarration")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Dialog for Registering/Replacing Subtitle */}
      <Dialog
        open={subtitleDialogOpen}
        title={
          subtitle
            ? t("project.assets.dialog.replaceSubtitleTitle")
            : t("project.assets.dialog.registerSubtitleTitle")
        }
        description={t("project.assets.dialog.registerSubtitleDescription")}
        onClose={closeSubtitleDialog}
        dismissible={!submittingSubtitle}
      >
        <form className="form-stack" onSubmit={handleRegisterSubtitle} noValidate>
          {subtitleApiError ? (
            <InlineError
              title={t("project.assets.form.errorTitle")}
              detail={subtitleApiError.detail ?? subtitleApiError.title}
              code={errorCode(subtitleApiError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="subtitle-job-id">
              {t("project.assets.form.storyboardJobId")}
            </label>
            <input
              id="subtitle-job-id"
              data-autofocus
              className="field__input mono"
              type="text"
              value={subtitleJobId}
              autoComplete="off"
              aria-invalid={
                subtitleFormErrors.storyboardJobId ? true : undefined
              }
              aria-describedby={
                subtitleFormErrors.storyboardJobId
                  ? "subtitle-job-id-error"
                  : "subtitle-job-id-hint"
              }
              onChange={(e) => setSubtitleJobId(e.target.value)}
            />
            {subtitleFormErrors.storyboardJobId ? (
              <p className="field__error" id="subtitle-job-id-error">
                {subtitleFormErrors.storyboardJobId}
              </p>
            ) : (
              <p className="field__hint" id="subtitle-job-id-hint">
                {t("project.assets.form.storyboardJobIdHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="subtitle-path">
              {t("project.assets.form.path")}
            </label>
            <input
              id="subtitle-path"
              className="field__input mono"
              type="text"
              value={subtitlePath}
              autoComplete="off"
              aria-invalid={subtitleFormErrors.path ? true : undefined}
              aria-describedby={
                subtitleFormErrors.path
                  ? "subtitle-path-error"
                  : "subtitle-path-hint"
              }
              onChange={(e) => setSubtitlePath(e.target.value)}
            />
            {subtitleFormErrors.path ? (
              <p className="field__error" id="subtitle-path-error">
                {subtitleFormErrors.path}
              </p>
            ) : (
              <p className="field__hint" id="subtitle-path-hint">
                {t("project.assets.form.pathHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="subtitle-origin">
              {t("project.assets.form.origin")}
            </label>
            <select
              id="subtitle-origin"
              className="select"
              value={subtitleOrigin}
              onChange={(e) =>
                setSubtitleOrigin(e.target.value as "generated" | "imported")
              }
            >
              <option value="generated">
                {t("project.assets.form.originGenerated")}
              </option>
              <option value="imported">
                {t("project.assets.form.originImported")}
              </option>
            </select>
            {subtitleOrigin === "imported" ? (
              <p className="field__hint">{t("project.assets.provenanceHint")}</p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="subtitle-source">
              {t("project.assets.form.source")}
              {subtitleOrigin === "imported" ? null : (
                <span className="field__optional">
                  {t("projects.form.optional")}
                </span>
              )}
            </label>
            <input
              id="subtitle-source"
              className="field__input"
              type="text"
              value={subtitleSource}
              aria-invalid={subtitleFormErrors.source ? true : undefined}
              aria-describedby={
                subtitleFormErrors.source ? "subtitle-source-error" : undefined
              }
              onChange={(e) => setSubtitleSource(e.target.value)}
            />
            {subtitleFormErrors.source ? (
              <p className="field__error" id="subtitle-source-error">
                {subtitleFormErrors.source}
              </p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="subtitle-creator">
              {t("project.assets.form.creator")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <input
              id="subtitle-creator"
              className="field__input"
              type="text"
              value={subtitleCreator}
              onChange={(e) => setSubtitleCreator(e.target.value)}
            />
          </div>

          <div className="field">
            <label className="field__label" htmlFor="subtitle-license">
              {t("project.assets.form.license")}
              {subtitleOrigin === "imported" ? null : (
                <span className="field__optional">
                  {t("projects.form.optional")}
                </span>
              )}
            </label>
            <input
              id="subtitle-license"
              className="field__input"
              type="text"
              value={subtitleLicense}
              aria-invalid={subtitleFormErrors.license ? true : undefined}
              aria-describedby={
                subtitleFormErrors.license ? "subtitle-license-error" : undefined
              }
              onChange={(e) => setSubtitleLicense(e.target.value)}
            />
            {subtitleFormErrors.license ? (
              <p className="field__error" id="subtitle-license-error">
                {subtitleFormErrors.license}
              </p>
            ) : null}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="subtitle-retrieved-at">
              {t("project.assets.form.retrievedAt")}
              <span className="field__optional">{t("projects.form.optional")}</span>
            </label>
            <input
              id="subtitle-retrieved-at"
              className="field__input"
              type="text"
              value={subtitleRetrievedAt}
              onChange={(e) => setSubtitleRetrievedAt(e.target.value)}
            />
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeSubtitleDialog}
              disabled={submittingSubtitle}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingSubtitle}
              loadingLabel={t("project.assets.form.submitting")}
            >
              {subtitle
                ? t("project.assets.form.submitReplaceSubtitle")
                : t("project.assets.form.submitSubtitle")}
            </Button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}

function AssetsSkeleton() {
  const { t } = useI18n();

  return (
    <div role="status" aria-busy="true">
      <span className="visually-hidden">{t("project.assets.loading")}</span>
      <div className="detail-skeleton" aria-hidden="true">
        <Skeleton width="100%" height={120} />
        <Skeleton width="100%" height={120} />
      </div>
    </div>
  );
}
