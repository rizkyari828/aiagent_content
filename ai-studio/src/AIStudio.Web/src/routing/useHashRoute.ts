import { useCallback, useEffect, useState } from "react";

export type AppRoute =
  | { screen: "projects" }
  | { screen: "project"; projectId: string }
  | { screen: "settings" };

const DEFAULT_ROUTE: AppRoute = { screen: "projects" };

export function routeToHash(route: AppRoute): string {
  return route.screen === "project"
    ? `#/projects/${route.projectId}`
    : `#/${route.screen}`;
}

function parseRoute(hash: string): AppRoute {
  const path = hash
    .replace(/^#\/?/, "")
    .replace(/\/+$/, "")
    .split(/[?&]/)[0];

  if (path === "settings") {
    return { screen: "settings" };
  }

  const projectMatch = /^projects\/([0-9a-fA-F-]{36})$/.exec(path);

  if (projectMatch) {
    return { screen: "project", projectId: projectMatch[1] };
  }

  return DEFAULT_ROUTE;
}

/**
 * Minimal hash router: `#/projects`, `#/projects/{id}`, and `#/settings` are
 * bookmarkable and survive a reload. No routing dependency.
 */
export function useHashRoute(): readonly [AppRoute, (route: AppRoute) => void] {
  const [route, setRouteState] = useState<AppRoute>(() =>
    parseRoute(window.location.hash),
  );

  useEffect(() => {
    const sync = () => setRouteState(parseRoute(window.location.hash));

    if (!window.location.hash) {
      window.history.replaceState(null, "", routeToHash(DEFAULT_ROUTE));
    }

    window.addEventListener("hashchange", sync);
    return () => window.removeEventListener("hashchange", sync);
  }, []);

  const navigate = useCallback((next: AppRoute) => {
    const hash = routeToHash(next);

    if (window.location.hash === hash) {
      setRouteState(next);
      return;
    }

    window.location.hash = hash;
  }, []);

  return [route, navigate] as const;
}