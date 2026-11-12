import { fetchJson } from "./client";

export interface CapabilityProviderView {
  providerId: string;
  capabilityId: string;
  priority: number;
  enabled: boolean;
  runtimeCategory: string;
}

export interface CapabilityView {
  id: string;
  displayName: string;
  category: string;
  providers: CapabilityProviderView[];
}

export interface CapabilitiesResponse {
  capabilities: CapabilityView[];
}

export type FeatureCapabilityKey =
  | "text.generation"
  | "visual.ai_image"
  | "visual.character_animation"
  | "speech.narration"
  | "music.instrumental";

export const FEATURE_CAPABILITY_KEYS: readonly FeatureCapabilityKey[] = [
  "text.generation",
  "visual.ai_image",
  "visual.character_animation",
  "speech.narration",
  "music.instrumental",
];

export type PreferredProvidersMap = Partial<Record<FeatureCapabilityKey, string>>;

export const PREFERRED_PROVIDERS_STORAGE_KEY = "aistudio.preferredProviders";

export function loadPreferredProviders(): PreferredProvidersMap {
  if (typeof window === "undefined" || !window.localStorage) {
    return {};
  }

  try {
    const raw = window.localStorage.getItem(PREFERRED_PROVIDERS_STORAGE_KEY);
    if (!raw) {
      return {};
    }
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as PreferredProvidersMap;
    }
    return {};
  } catch {
    return {};
  }
}

export function savePreferredProvider(
  capabilityId: FeatureCapabilityKey,
  providerId: string,
): PreferredProvidersMap {
  const current = loadPreferredProviders();
  const trimmed = providerId.trim();

  if (!trimmed || trimmed === "auto") {
    delete current[capabilityId];
  } else {
    current[capabilityId] = trimmed;
  }

  if (typeof window !== "undefined" && window.localStorage) {
    try {
      window.localStorage.setItem(
        PREFERRED_PROVIDERS_STORAGE_KEY,
        JSON.stringify(current),
      );
    } catch {
      // Ignore storage write failures (quota, private browsing)
    }
  }

  return current;
}

export function getCapabilities(signal?: AbortSignal): Promise<CapabilitiesResponse> {
  return fetchJson<CapabilitiesResponse>("/api/capabilities", { signal });
}
