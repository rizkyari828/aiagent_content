import { useCallback, useEffect, useState } from "react";

export type AppRoute = "projects" | "settings";

const DEFAULT_ROUTE: AppRoute = "projects";

function parseRoute(hash: string): AppRoute {
  const path = hash
    .replace(/^#\/?/, "")
    .replace(/\/+$/, "")
    .split(/[?&]/)[0];

  return path === "settings" ? "settings" : DEFAULT_ROUTE;
}

function routeToHash(route: AppRoute): string {
  return `#/${route}`;
}

/**
 * Minimal hash router: internal state plus `#/projects` / `#/settings` so a
 * screen can be bookmarked and survives a reload. No routing dependency.
 */
export function useHashRoute(): readonly [AppRoute, (route: AppRoute) => void] {
  const [route, setRoute] = useState<AppRoute>(() =>
    parseRoute(window.location.hash),
  );

  useEffect(() => {
    const sync = () => setRoute(parseRoute(window.location.hash));

    if (!window.location.hash) {
      window.history.replaceState(null, "", routeToHash(DEFAULT_ROUTE));
    } else if (parseRoute(window.location.hash) === DEFAULT_ROUTE) {
      const raw = window.location.hash.replace(/^#\/?/, "").split(/[?&]/)[0];

      if (raw !== "" && raw !== DEFAULT_ROUTE && raw !== "projects/") {
        window.history.replaceState(null, "", routeToHash(DEFAULT_ROUTE));
      }
    }

    window.addEventListener("hashchange", sync);
    return () => window.removeEventListener("hashchange", sync);
  }, []);

  const navigate = useCallback((next: AppRoute) => {
    const hash = routeToHash(next);

    if (window.location.hash === hash) {
      setRoute(next);
      return;
    }

    window.location.hash = hash;
  }, []);

  return [route, navigate] as const;
}
