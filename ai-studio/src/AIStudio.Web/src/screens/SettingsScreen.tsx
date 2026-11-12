import { useCallback, useEffect, useState } from "react";
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
import { Badge } from "../components/Badge";
import { Card } from "../components/Card";
import { InlineError } from "../components/InlineError";
import { LanguageSelect, ThemePicker } from "../components/Preferences";
import { Skeleton } from "../components/Skeleton";
import { useToast } from "../components/Toast";
import { useI18n } from "../i18n/i18n";

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

export function SettingsScreen() {
  const { t } = useI18n();
  const { showToast } = useToast();

  const [capabilities, setCapabilities] = useState<CapabilityView[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);
  const [preferredProviders, setPreferredProviders] =
    useState<PreferredProvidersMap>(() => loadPreferredProviders());

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
    </>
  );
}
