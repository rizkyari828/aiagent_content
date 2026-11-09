import { API_BASE_URL } from "../api/client";
import { Card } from "../components/Card";
import { LanguageSelect, ThemePicker } from "../components/Preferences";
import { useI18n } from "../i18n/i18n";

export function SettingsScreen() {
  const { t } = useI18n();

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

        <Card title={t("settings.api.title")} flush>
          <div className="preference-row">
            <div className="preference-row__text">
              <p className="preference-row__title">{t("settings.api.baseUrl")}</p>
              <p className="preference-row__hint">{t("settings.api.hint")}</p>
            </div>
            <code className="mono">{API_BASE_URL}</code>
          </div>
        </Card>

        <Card title={t("settings.status.title")}>
          <p className="inline-note">{t("settings.status.description")}</p>
        </Card>
      </div>
    </>
  );
}
