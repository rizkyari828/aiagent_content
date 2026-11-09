import { useI18n, type Locale } from "../i18n/i18n";
import { THEME_MODES, useTheme, type ThemeMode } from "../theme/useTheme";

const THEME_LABEL_KEYS: Record<ThemeMode, string> = {
  auto: "theme.auto",
  light: "theme.light",
  dark: "theme.dark",
};

const LOCALE_VALUES: Record<Locale, string> = {
  id: "language.id",
  en: "language.en",
};

export function isLocaleValue(value: string): value is Locale {
  return value === "id" || value === "en";
}

export function LanguageSelect({ id }: { id?: string }) {
  const { locale, setLocale, t } = useI18n();

  return (
    <select
      id={id}
      className="select"
      value={locale}
      aria-label={id ? undefined : t("header.language")}
      onChange={(event) => {
        const next = event.target.value;

        if (isLocaleValue(next)) {
          setLocale(next);
        }
      }}
    >
      <option value="id">{t(LOCALE_VALUES.id)}</option>
      <option value="en">{t(LOCALE_VALUES.en)}</option>
    </select>
  );
}

export function ThemePicker({ label }: { label?: string }) {
  const { mode, setMode } = useTheme();
  const { t } = useI18n();

  return (
    <div className="segmented" role="group" aria-label={label ?? t("header.theme")}>
      {THEME_MODES.map((option) => (
        <button
          key={option}
          type="button"
          className="segmented__option"
          aria-pressed={mode === option}
          onClick={() => setMode(option)}
        >
          {t(THEME_LABEL_KEYS[option])}
        </button>
      ))}
    </div>
  );
}
