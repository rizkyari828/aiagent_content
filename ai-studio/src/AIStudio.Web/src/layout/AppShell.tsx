import { useEffect, useRef, type ReactNode } from "react";
import { LanguageSelect, ThemePicker } from "../components/Preferences";
import {
  IconLogo,
  IconProjects,
  IconRuns,
  IconSettings,
  type IconProps,
} from "../components/icons";
import { useI18n } from "../i18n/i18n";
import type { AppRoute } from "../routing/useHashRoute";

interface AppShellProps {
  route: AppRoute;
  onNavigate: (route: AppRoute) => void;
  children: ReactNode;
}

interface NavLinkProps {
  route: AppRoute;
  label: string;
  current: boolean;
  onNavigate: (route: AppRoute) => void;
  icon: (props: IconProps) => ReactNode;
}

function NavLink({ route, label, current, onNavigate, icon: Icon }: NavLinkProps) {
  return (
    <a
      className="nav-item"
      href={`#/${route}`}
      aria-current={current ? "page" : undefined}
      onClick={(event) => {
        event.preventDefault();
        onNavigate(route);
      }}
    >
      <span className="nav-item__icon">
        <Icon size={18} />
      </span>
      <span className="nav-item__label">{label}</span>
    </a>
  );
}

export function AppShell({ route, onNavigate, children }: AppShellProps) {
  const { t } = useI18n();
  const pageTitle = t(`${route}.title`);
  const mainRef = useRef<HTMLElement>(null);

  useEffect(() => {
    document.title = `${pageTitle} · ${t("app.name")}`;
  }, [pageTitle, t]);

  return (
    <>
      <a
        className="skip-link"
        href="#main-content"
        onClick={(event) => {
          event.preventDefault();
          mainRef.current?.focus();
        }}
      >
        {t("nav.skipToContent")}
      </a>
      <div className="app-shell">
        <aside className="sidebar">
          <a
            className="brand"
            href="#/projects"
            onClick={(event) => {
              event.preventDefault();
              onNavigate("projects");
            }}
          >
            <span className="brand__mark" aria-hidden="true">
              <IconLogo size={18} />
            </span>
            <span className="brand__text">
              <span className="brand__name">{t("app.name")}</span>
              <span className="brand__tagline">{t("app.tagline")}</span>
            </span>
          </a>

          <nav className="sidebar__nav" aria-label={t("nav.label")}>
            <NavLink
              route="projects"
              label={t("nav.projects")}
              current={route === "projects"}
              onNavigate={onNavigate}
              icon={IconProjects}
            />
            <button
              type="button"
              className="nav-item"
              disabled
              title={t("nav.runsSoon")}
            >
              <span className="nav-item__icon">
                <IconRuns size={18} />
              </span>
              <span className="nav-item__label">{t("nav.runs")}</span>
              <span className="nav-item__soon">{t("nav.soon")}</span>
            </button>
            <NavLink
              route="settings"
              label={t("nav.settings")}
              current={route === "settings"}
              onNavigate={onNavigate}
              icon={IconSettings}
            />
          </nav>

          <p className="sidebar__footer">{t("nav.footer")}</p>
        </aside>

        <div className="app-main">
          <header className="topbar">
            <nav className="breadcrumb" aria-label={t("nav.breadcrumb")}>
              <ol className="breadcrumb__list">
                <li className="breadcrumb__item">
                  <span>{t("app.name")}</span>
                </li>
                <li className="breadcrumb__item">
                  <span className="breadcrumb__current" aria-current="page">
                    {pageTitle}
                  </span>
                </li>
              </ol>
            </nav>

            <div className="topbar__actions">
              <div className="control-row">
                <span className="control-row__label" aria-hidden="true">
                  {t("header.language")}
                </span>
                <LanguageSelect />
              </div>
              <ThemePicker />
            </div>
          </header>

          <main id="main-content" className="content" tabIndex={-1} ref={mainRef}>
            {children}
          </main>
        </div>
      </div>
    </>
  );
}
