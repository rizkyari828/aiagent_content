import { useCallback, useEffect, useState, type ChangeEvent, type FormEvent } from "react";
import {
  FEATURE_CAPABILITY_KEYS,
  getCapabilities,
  loadPreferredProviders,
  savePreferredProvider,
  type CapabilityView,
  type FeatureCapabilityKey,
  type PreferredProvidersMap,
} from "../api/capabilities";
import {
  API_BASE_URL_LABEL,
  isApiRequestError,
  type ApiError,
} from "../api/client";
import {
  exportProject,
  importProjectPackage,
  type ProjectExportResponse,
  type ProjectImportResponse,
} from "../api/transfer";
import { Badge } from "../components/Badge";
import { Button } from "../components/Button";
import { Card } from "../components/Card";
import { Dialog } from "../components/Dialog";
import { InlineError } from "../components/InlineError";
import { LanguageSelect, ThemePicker } from "../components/Preferences";
import { Skeleton } from "../components/Skeleton";
import { useToast } from "../components/Toast";
import { useFormatters } from "../i18n/format";
import { useI18n } from "../i18n/i18n";

export interface SettingsScreenProps {
  onOpenProject?: (projectId: string) => void;
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

function formatByteSize(bytes: number, numberFormatter: (n: number) => string): string {
  if (bytes < 1024) {
    return `${numberFormatter(bytes)} B`;
  }
  const kb = bytes / 1024;
  if (kb < 1024) {
    return `${numberFormatter(Math.round(kb * 10) / 10)} KB (${numberFormatter(bytes)} B)`;
  }
  const mb = kb / 1024;
  return `${numberFormatter(Math.round(mb * 10) / 10)} MB (${numberFormatter(bytes)} B)`;
}

export function SettingsScreen({ onOpenProject }: SettingsScreenProps = {}) {
  const { t } = useI18n();
  const { dateTime, number } = useFormatters();
  const { showToast } = useToast();

  const [capabilities, setCapabilities] = useState<CapabilityView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);
  const [preferredProviders, setPreferredProviders] =
    useState<PreferredProvidersMap>(() => loadPreferredProviders());

  // Project Transfer Export state
  const [exportProjectId, setExportProjectId] = useState("");
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<ApiError | null>(null);
  const [exportResult, setExportResult] =
    useState<ProjectExportResponse | null>(null);

  // Project Transfer Import state
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [importConfirmOpen, setImportConfirmOpen] = useState(false);
  const [importing, setImporting] = useState(false);
  const [importError, setImportError] = useState<ApiError | null>(null);
  const [importResult, setImportResult] =
    useState<ProjectImportResponse | null>(null);

  const fetchCapabilitiesData = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError(null);
    try {
      const response = await getCapabilities(signal);
      if (signal?.aborted) {
        return;
      }
      setCapabilities(response.capabilities);
      setLoading(false);
    } catch (err: unknown) {
      if (signal?.aborted) {
        return;
      }
      setLoading(false);
      setError(toApiError(err));
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    fetchCapabilitiesData(controller.signal);
    return () => controller.abort();
  }, [fetchCapabilitiesData]);

  const handleProviderChange = (
    featureKey: FeatureCapabilityKey,
    providerId: string,
  ) => {
    const updated = savePreferredProvider(featureKey, providerId);
    setPreferredProviders(updated);
    showToast(t("settings.providers.savedToast"), "success");
  };

  const handleExport = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const id = exportProjectId.trim();
    if (!id) {
      return;
    }

    setExporting(true);
    setExportError(null);
    try {
      const response = await exportProject(id);
      setExportResult(response);
      showToast(t("settings.transfer.export.successToast"), "success");
    } catch (err: unknown) {
      setExportError(toApiError(err));
    } finally {
      setExporting(false);
    }
  };

  const handleFileChange = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0] ?? null;
    setSelectedFile(file);
    setImportError(null);
    setImportResult(null);
  };

  const handleConfirmImport = async () => {
    if (!selectedFile) {
      return;
    }

    setImporting(true);
    setImportError(null);
    try {
      const response = await importProjectPackage(selectedFile);
      setImportResult(response);
      setImportConfirmOpen(false);
      showToast(t("settings.transfer.import.successToast"), "success");
    } catch (err: unknown) {
      setImportConfirmOpen(false);
      setImportError(toApiError(err));
      const apiErr = toApiError(err);
      showToast(apiErr.detail ?? apiErr.title, "error");
    } finally {
      setImporting(false);
    }
  };

  return (
    <>
      <div className="page-header">
        <div className="page-header__text">
          <h1 className="page-header__title">{t("settings.title")}</h1>
          <p className="page-header__subtitle">{t("settings.subtitle")}</p>
        </div>
      </div>

      <div className="stack">
        <Card
          title={t("settings.appearance.title")}
          description={t("settings.appearance.description")}
          flush
        >
          <div className="preference-row">
            <div className="preference-row__text">
              <p className="preference-row__title">{t("settings.theme.label")}</p>
              <p className="preference-row__hint">{t("settings.theme.hint")}</p>
            </div>
            <ThemePicker label={t("settings.theme.label")} />
          </div>
          <div className="preference-row">
            <div className="preference-row__text">
              <label className="preference-row__title" htmlFor="settings-language">
                {t("settings.language.label")}
              </label>
              <p className="preference-row__hint">{t("settings.language.hint")}</p>
            </div>
            <LanguageSelect id="settings-language" />
          </div>
        </Card>

        {/* Production AI Providers by Feature */}
        <Card
          title={t("settings.providers.title")}
          description={t("settings.providers.description")}
          flush
        >
          {loading ? (
            <div
              role="status"
              aria-busy="true"
              style={{ padding: "var(--space-5)" }}
            >
              <span className="visually-hidden">
                {t("settings.providers.loading")}
              </span>
              <div
                className="detail-skeleton"
                aria-hidden="true"
                style={{
                  display: "flex",
                  flexDirection: "column",
                  gap: "var(--space-4)",
                }}
              >
                <Skeleton width="100%" height={40} />
                <Skeleton width="100%" height={40} />
                <Skeleton width="100%" height={40} />
              </div>
            </div>
          ) : error ? (
            <div style={{ padding: "var(--space-5)" }}>
              <InlineError
                title={t("settings.providers.errorTitle")}
                detail={error.detail ?? error.title}
                code={error.code}
                actionLabel={t("action.retry")}
                onAction={() => fetchCapabilitiesData()}
              />
            </div>
          ) : (
            FEATURE_CAPABILITY_KEYS.map((featureKey) => {
              const cap = capabilities.find((c) => c.id === featureKey);
              const providers = cap?.providers ?? [];
              const selectedValue = preferredProviders[featureKey] ?? "auto";

              return (
                <div key={featureKey} className="preference-row">
                  <div className="preference-row__text">
                    <label
                      className="preference-row__title"
                      htmlFor={`preferred-provider-${featureKey}`}
                    >
                      {t(`settings.providers.feature.${featureKey}`)}
                    </label>
                    <p className="preference-row__hint">
                      {t(`settings.providers.featureHint.${featureKey}`)}
                    </p>
                    <div
                      style={{
                        display: "flex",
                        flexWrap: "wrap",
                        gap: "var(--space-2)",
                        marginTop: "var(--space-2)",
                      }}
                    >
                      {providers.length > 0 ? (
                        providers.map((p) => (
                          <span
                            key={p.providerId}
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "var(--space-1)",
                              padding: "2px 6px",
                              background: "var(--color-surface-muted)",
                              borderRadius: "var(--radius-sm)",
                            }}
                          >
                            <span
                              className="mono"
                              style={{ fontSize: "var(--text-xs)" }}
                            >
                              {p.providerId}
                            </span>
                            <Badge tone={p.enabled ? "success" : "muted"}>
                              {p.enabled
                                ? t("settings.providers.enabled")
                                : t("settings.providers.disabled")}
                            </Badge>
                            <Badge tone="neutral">
                              {t(
                                `settings.providers.runtimeCategory.${p.runtimeCategory}`,
                              )}
                            </Badge>
                          </span>
                        ))
                      ) : (
                        <span className="inline-note">
                          {t("settings.providers.noProviders")}
                        </span>
                      )}
                    </div>
                  </div>
                  <div style={{ minWidth: "220px" }}>
                    <select
                      id={`preferred-provider-${featureKey}`}
                      className="field__input"
                      value={selectedValue}
                      onChange={(e) =>
                        handleProviderChange(featureKey, e.target.value)
                      }
                    >
                      <option value="auto">
                        {t("settings.providers.autoOption")}
                      </option>
                      {providers.map((p) => (
                        <option key={p.providerId} value={p.providerId}>
                          {p.providerId} (
                          {p.enabled
                            ? t("settings.providers.enabled")
                            : t("settings.providers.disabled")}
                          )
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
              );
            })
          )}
        </Card>

        {/* Project Transfer & Backup Card */}
        <Card
          title={t("settings.transfer.title")}
          description={t("settings.transfer.description")}
        >
          <div className="stack">
            {/* Export Section */}
            <div
              style={{
                borderBottom: "1px solid var(--color-border)",
                paddingBottom: "var(--space-5)",
              }}
            >
              <h3
                style={{
                  fontSize: "var(--text-md)",
                  fontWeight: "var(--font-semibold)",
                  marginBottom: "var(--space-1)",
                }}
              >
                {t("settings.transfer.export.title")}
              </h3>
              <p
                className="inline-note"
                style={{ marginBottom: "var(--space-3)" }}
              >
                {t("settings.transfer.export.hint")}
              </p>

              <form onSubmit={handleExport} className="form-stack" noValidate>
                <div className="field">
                  <label
                    className="field__label"
                    htmlFor="transfer-export-project-id"
                  >
                    {t("settings.transfer.export.projectIdLabel")}
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
                      id="transfer-export-project-id"
                      type="text"
                      className="field__input mono"
                      style={{ flex: 1, minWidth: "260px" }}
                      placeholder={t(
                        "settings.transfer.export.projectIdPlaceholder",
                      )}
                      value={exportProjectId}
                      onChange={(e) => setExportProjectId(e.target.value)}
                    />
                    <Button
                      type="submit"
                      variant="primary"
                      loading={exporting}
                      loadingLabel={t("settings.transfer.export.loading")}
                      disabled={!exportProjectId.trim()}
                    >
                      {t("settings.transfer.export.action")}
                    </Button>
                  </div>
                </div>
              </form>

              {exportError ? (
                <div style={{ marginTop: "var(--space-3)" }}>
                  <InlineError
                    title={t("settings.transfer.export.errorTitle")}
                    detail={exportError.detail ?? exportError.title}
                    code={exportError.code}
                  />
                </div>
              ) : null}

              {exportResult ? (
                <div
                  style={{
                    marginTop: "var(--space-3)",
                    background: "var(--color-surface-muted)",
                    padding: "var(--space-4)",
                    borderRadius: "var(--radius-md)",
                  }}
                >
                  <h4
                    style={{
                      fontSize: "var(--text-sm)",
                      fontWeight: "var(--font-semibold)",
                      marginBottom: "var(--space-2)",
                    }}
                  >
                    {t("settings.transfer.export.resultTitle")}
                  </h4>
                  <dl className="definition-list">
                    <div>
                      <dt>{t("settings.transfer.export.fileName")}</dt>
                      <dd className="mono">
                        {exportResult.relativePackagePath ||
                          exportResult.fileName}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("settings.transfer.export.byteSize")}</dt>
                      <dd>{formatByteSize(exportResult.byteSize, number)}</dd>
                    </div>
                    <div>
                      <dt>{t("settings.transfer.export.sha256")}</dt>
                      <dd className="mono">
                        {exportResult.contentHash ||
                          exportResult.sha256 ||
                          "—"}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("settings.transfer.export.exportedAt")}</dt>
                      <dd>
                        {dateTime(
                          exportResult.exportedAt ||
                            exportResult.createdAtUtc ||
                            new Date().toISOString(),
                        )}
                      </dd>
                    </div>
                  </dl>
                </div>
              ) : null}
            </div>

            {/* Import Section */}
            <div>
              <h3
                style={{
                  fontSize: "var(--text-md)",
                  fontWeight: "var(--font-semibold)",
                  marginBottom: "var(--space-1)",
                }}
              >
                {t("settings.transfer.import.title")}
              </h3>
              <p
                className="inline-note"
                style={{ marginBottom: "var(--space-3)" }}
              >
                {t("settings.transfer.import.hint")}
              </p>

              <div className="field">
                <label
                  className="field__label"
                  htmlFor="transfer-import-file-input"
                >
                  {t("settings.transfer.import.fileLabel")}
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
                    id="transfer-import-file-input"
                    type="file"
                    accept=".zip,application/zip"
                    className="field__input"
                    style={{ flex: 1, minWidth: "260px" }}
                    onChange={handleFileChange}
                  />
                  <Button
                    type="button"
                    variant="primary"
                    disabled={!selectedFile || importing}
                    loading={importing}
                    loadingLabel={t("settings.transfer.import.loading")}
                    onClick={() => setImportConfirmOpen(true)}
                  >
                    {t("settings.transfer.import.action")}
                  </Button>
                </div>
              </div>

              {importError ? (
                <div style={{ marginTop: "var(--space-3)" }}>
                  <InlineError
                    title={t("settings.transfer.import.errorTitle")}
                    detail={importError.detail ?? importError.title}
                    code={importError.code}
                  />
                </div>
              ) : null}

              {importResult ? (
                <div
                  style={{
                    marginTop: "var(--space-3)",
                    background: "var(--color-surface-muted)",
                    padding: "var(--space-4)",
                    borderRadius: "var(--radius-md)",
                  }}
                >
                  <h4
                    style={{
                      fontSize: "var(--text-sm)",
                      fontWeight: "var(--font-semibold)",
                      marginBottom: "var(--space-2)",
                    }}
                  >
                    {t("settings.transfer.import.resultTitle")}
                  </h4>
                  <dl className="definition-list">
                    <div>
                      <dt>{t("settings.transfer.import.projectId")}</dt>
                      <dd className="mono">{importResult.contentProjectId}</dd>
                    </div>
                    <div>
                      <dt>{t("settings.transfer.import.restoredFiles")}</dt>
                      <dd>
                        {number(
                          importResult.totalFilesImported ??
                            importResult.files ??
                            0,
                        )}
                      </dd>
                    </div>
                    <div>
                      <dt>{t("settings.transfer.import.restoredBytes")}</dt>
                      <dd>
                        {formatByteSize(
                          importResult.totalBytesImported ?? 0,
                          number,
                        )}
                      </dd>
                    </div>
                  </dl>
                  <div style={{ marginTop: "var(--space-3)" }}>
                    {onOpenProject ? (
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() =>
                          onOpenProject(importResult.contentProjectId)
                        }
                      >
                        {t("settings.transfer.import.viewProject")}
                      </Button>
                    ) : (
                      <a
                        href={`#/projects/${importResult.contentProjectId}`}
                        className="button button--secondary button--sm"
                      >
                        {t("settings.transfer.import.viewProject")}
                      </a>
                    )}
                  </div>
                </div>
              ) : null}
            </div>
          </div>
        </Card>

        <Card title={t("settings.api.title")} flush>
          <div className="preference-row">
            <div className="preference-row__text">
              <p className="preference-row__title">{t("settings.api.baseUrl")}</p>
              <p className="preference-row__hint">{t("settings.api.hint")}</p>
            </div>
            <code className="mono">{API_BASE_URL_LABEL}</code>
          </div>
        </Card>

        <Card title={t("settings.status.title")}>
          <p className="inline-note">{t("settings.status.description")}</p>
        </Card>
      </div>

      {/* Import Confirmation Dialog */}
      <Dialog
        open={importConfirmOpen}
        title={t("settings.transfer.import.confirmTitle")}
        description={
          selectedFile
            ? t("settings.transfer.import.confirmDescription", {
                fileName: selectedFile.name,
              })
            : undefined
        }
        onClose={() => setImportConfirmOpen(false)}
      >
        <div className="dialog-form__actions">
          <Button
            variant="secondary"
            onClick={() => setImportConfirmOpen(false)}
            data-autofocus
            disabled={importing}
          >
            {t("action.cancel")}
          </Button>
          <Button
            variant="primary"
            onClick={handleConfirmImport}
            loading={importing}
            loadingLabel={t("settings.transfer.import.loading")}
          >
            {t("settings.transfer.import.confirmAction")}
          </Button>
        </div>
      </Dialog>
    </>
  );
}
