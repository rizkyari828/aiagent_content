import { useSyncExternalStore } from "react";

export type ThemeMode = "auto" | "light" | "dark";
export type ResolvedTheme = "light" | "dark";

export const THEME_MODES: readonly ThemeMode[] = ["auto", "light", "dark"];

const STORAGE_KEY = "ai-studio.theme";
const DARK_QUERY = "(prefers-color-scheme: dark)";

interface ThemeState {
  mode: ThemeMode;
  resolved: ResolvedTheme;
}

const listeners = new Set<() => void>();
let mediaQuery: MediaQueryList | null = null;

function isThemeMode(value: string | null): value is ThemeMode {
  return value === "auto" || value === "light" || value === "dark";
}

function readStoredMode(): ThemeMode {
  if (typeof window === "undefined") {
    return "auto";
  }

  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return isThemeMode(stored) ? stored : "auto";
  } catch {
    return "auto";
  }
}

function prefersDark(): boolean {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return false;
  }

  return window.matchMedia(DARK_QUERY).matches;
}

function resolveMode(mode: ThemeMode): ResolvedTheme {
  if (mode === "auto") {
    return prefersDark() ? "dark" : "light";
  }

  return mode;
}

function applyTheme(state: ThemeState): void {
  if (typeof document === "undefined") {
    return;
  }

  document.documentElement.dataset.theme = state.resolved;
  document.documentElement.style.colorScheme = state.resolved;
}

let state: ThemeState = (() => {
  const mode = readStoredMode();
  return { mode, resolved: resolveMode(mode) };
})();

applyTheme(state);

function emit(): void {
  for (const listener of listeners) {
    listener();
  }
}

function ensureMediaListener(): void {
  if (mediaQuery || typeof window === "undefined" || typeof window.matchMedia !== "function") {
    return;
  }

  mediaQuery = window.matchMedia(DARK_QUERY);
  mediaQuery.addEventListener("change", (event) => {
    if (state.mode !== "auto") {
      return;
    }

    const resolved: ResolvedTheme = event.matches ? "dark" : "light";

    if (resolved === state.resolved) {
      return;
    }

    state = { mode: state.mode, resolved };
    applyTheme(state);
    emit();
  });
}

ensureMediaListener();

export function setThemeMode(mode: ThemeMode): void {
  const next: ThemeState = { mode, resolved: resolveMode(mode) };
  state = next;

  try {
    window.localStorage.setItem(STORAGE_KEY, mode);
  } catch {
    // Storage can be unavailable; the in-memory preference still applies.
  }

  applyTheme(next);
  emit();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): ThemeState {
  return state;
}

export function useTheme(): ThemeState & { setMode: (mode: ThemeMode) => void } {
  const snapshot = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);

  return { ...snapshot, setMode: setThemeMode };
}
