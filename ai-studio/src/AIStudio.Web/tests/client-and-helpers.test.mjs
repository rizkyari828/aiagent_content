import test from "node:test";
import assert from "node:assert/strict";

// Invariants for ApiRequestError and error normalization
class ApiRequestError extends Error {
  constructor(error) {
    super(error.detail ?? error.title);
    this.name = "ApiRequestError";
    this.code = error.code;
    this.title = error.title;
    this.detail = error.detail;
    this.status = error.status;
    this.fieldErrors = error.fieldErrors;
  }
}

function normalizeError(status, payload) {
  const problem = typeof payload === "object" && payload !== null && !Array.isArray(payload)
    ? payload
    : null;
  const rawTitle = problem?.title;
  const rawDetail = problem?.detail;
  const rawCode = problem?.code;

  let fieldErrors = undefined;
  if (problem?.errors && typeof problem.errors === "object" && !Array.isArray(problem.errors)) {
    const cleaned = {};
    for (const [k, v] of Object.entries(problem.errors)) {
      if (Array.isArray(v)) {
        const msgs = v.filter((m) => typeof m === "string" && m.length > 0);
        if (msgs.length > 0) cleaned[k] = msgs;
      }
    }
    if (Object.keys(cleaned).length > 0) fieldErrors = cleaned;
  }

  return {
    status,
    code: typeof rawCode === "string" && rawCode.length > 0 ? rawCode : undefined,
    title: typeof rawTitle === "string" && rawTitle.length > 0 ? rawTitle : `HTTP ${status}`,
    detail: typeof rawDetail === "string" && rawDetail.length > 0 ? rawDetail : undefined,
    fieldErrors,
  };
}

// Invariants for hash routing
function routeToHash(route) {
  if (route.screen === "project") {
    return `#/projects/${route.projectId}`;
  }
  if (route.screen === "runs") {
    return route.jobId ? `#/runs/${route.jobId}` : "#/runs";
  }
  return `#/${route.screen}`;
}

function parseRoute(hash) {
  const path = hash
    .replace(/^#\/?/, "")
    .replace(/\/+$/, "")
    .split(/[?&]/)[0];

  if (path === "settings") {
    return { screen: "settings" };
  }
  if (path === "runs") {
    return { screen: "runs" };
  }
  const runsMatch = /^runs\/([0-9a-fA-F-]{36})$/.exec(path);
  if (runsMatch) {
    return { screen: "runs", jobId: runsMatch[1] };
  }
  const projectMatch = /^projects\/([0-9a-fA-F-]{36})$/.exec(path);
  if (projectMatch) {
    return { screen: "project", projectId: projectMatch[1] };
  }
  return { screen: "projects" };
}

// Invariants for preferred providers storage
const PREFERRED_PROVIDERS_STORAGE_KEY = "aistudio.preferredProviders";

function simulateLoadPreferredProviders(storage) {
  try {
    const raw = storage.getItem(PREFERRED_PROVIDERS_STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed;
    }
    return {};
  } catch {
    return {};
  }
}

function simulateSavePreferredProvider(storage, capabilityId, providerId) {
  const current = simulateLoadPreferredProviders(storage);
  const trimmed = providerId.trim();
  if (!trimmed || trimmed === "auto") {
    delete current[capabilityId];
  } else {
    current[capabilityId] = trimmed;
  }
  storage.setItem(PREFERRED_PROVIDERS_STORAGE_KEY, JSON.stringify(current));
  return current;
}

// Enqueue options shaping tests
function buildVisualBody(options) {
  const trimmedImageProvider = options?.imageProvider?.trim();
  const trimmedVideoProvider = options?.videoProvider?.trim();
  const hasBody =
    options?.productionRecipe !== undefined ||
    options?.identityReference !== undefined ||
    options?.artDirection !== undefined ||
    (trimmedImageProvider !== undefined && trimmedImageProvider.length > 0) ||
    (trimmedVideoProvider !== undefined && trimmedVideoProvider.length > 0);

  return hasBody
    ? {
        ...(options?.productionRecipe !== undefined ? { productionRecipe: options.productionRecipe } : {}),
        ...(options?.identityReference !== undefined ? { identityReference: options.identityReference } : {}),
        ...(options?.artDirection !== undefined ? { artDirection: options.artDirection } : {}),
        ...(trimmedImageProvider && trimmedImageProvider.length > 0 ? { imageProvider: trimmedImageProvider } : {}),
        ...(trimmedVideoProvider && trimmedVideoProvider.length > 0 ? { videoProvider: trimmedVideoProvider } : {}),
      }
    : undefined;
}

function buildAudioBody(options) {
  const trimmedSpeechProvider = options?.speechProvider?.trim();
  const hasBody = trimmedSpeechProvider !== undefined && trimmedSpeechProvider.length > 0;
  return hasBody ? { speechProvider: trimmedSpeechProvider } : undefined;
}

test("ApiRequestError: creates correct error instances with problem+json properties", () => {
  const err = new ApiRequestError({
    code: "not_found",
    title: "Project not found.",
    detail: "Project '123' does not exist.",
    status: 404,
    fieldErrors: { projectId: ["Invalid identifier"] },
  });

  assert.ok(err instanceof Error);
  assert.strictEqual(err.name, "ApiRequestError");
  assert.strictEqual(err.code, "not_found");
  assert.strictEqual(err.status, 404);
  assert.strictEqual(err.message, "Project '123' does not exist.");
  assert.deepStrictEqual(err.fieldErrors, { projectId: ["Invalid identifier"] });
});

test("normalizeError: correctly extracts problem+json title, detail, code, and validation errors", () => {
  const norm = normalizeError(400, {
    title: "Validation error.",
    detail: "One or more fields failed validation.",
    code: "validation_failed",
    errors: {
      title: ["Title is required.", ""],
      age: [],
    },
  });

  assert.strictEqual(norm.status, 400);
  assert.strictEqual(norm.code, "validation_failed");
  assert.strictEqual(norm.title, "Validation error.");
  assert.strictEqual(norm.detail, "One or more fields failed validation.");
  assert.deepStrictEqual(norm.fieldErrors, { title: ["Title is required."] });
});

test("normalizeError: falls back gracefully when problem fields are missing", () => {
  const norm = normalizeError(500, null);
  assert.strictEqual(norm.status, 500);
  assert.strictEqual(norm.title, "HTTP 500");
  assert.strictEqual(norm.code, undefined);
  assert.strictEqual(norm.detail, undefined);
  assert.strictEqual(norm.fieldErrors, undefined);
});

test("routeToHash: maps screens and parameters correctly", () => {
  assert.strictEqual(routeToHash({ screen: "projects" }), "#/projects");
  assert.strictEqual(routeToHash({ screen: "settings" }), "#/settings");
  assert.strictEqual(routeToHash({ screen: "runs" }), "#/runs");
  assert.strictEqual(
    routeToHash({ screen: "runs", jobId: "00000000-0000-0000-0000-000000000001" }),
    "#/runs/00000000-0000-0000-0000-000000000001",
  );
  assert.strictEqual(
    routeToHash({ screen: "project", projectId: "11111111-1111-1111-1111-111111111111" }),
    "#/projects/11111111-1111-1111-1111-111111111111",
  );
});

test("parseRoute: correctly parses hash routes and falls back to default", () => {
  assert.deepStrictEqual(parseRoute("#/projects"), { screen: "projects" });
  assert.deepStrictEqual(parseRoute("#/settings"), { screen: "settings" });
  assert.deepStrictEqual(parseRoute("#/runs"), { screen: "runs" });
  assert.deepStrictEqual(
    parseRoute("#/runs/00000000-0000-0000-0000-000000000001"),
    { screen: "runs", jobId: "00000000-0000-0000-0000-000000000001" },
  );
  assert.deepStrictEqual(
    parseRoute("#/projects/11111111-1111-1111-1111-111111111111"),
    { screen: "project", projectId: "11111111-1111-1111-1111-111111111111" },
  );
  assert.deepStrictEqual(parseRoute("#/invalid-garbage"), { screen: "projects" });
  assert.deepStrictEqual(parseRoute(""), { screen: "projects" });
});

test("PreferredProviders: handles saving, loading, and resetting to auto cleanly", () => {
  const mockStorage = {
    store: new Map(),
    getItem(k) { return this.store.get(k) ?? null; },
    setItem(k, v) { this.store.set(k, String(v)); },
    removeItem(k) { this.store.delete(k); },
  };

  assert.deepStrictEqual(simulateLoadPreferredProviders(mockStorage), {});

  // Save image provider
  simulateSavePreferredProvider(mockStorage, "visual.ai_image", "comfyui-flux");
  assert.deepStrictEqual(simulateLoadPreferredProviders(mockStorage), {
    "visual.ai_image": "comfyui-flux",
  });

  // Save speech provider
  simulateSavePreferredProvider(mockStorage, "speech.narration", "voxcpm2");
  assert.deepStrictEqual(simulateLoadPreferredProviders(mockStorage), {
    "visual.ai_image": "comfyui-flux",
    "speech.narration": "voxcpm2",
  });

  // Reset image provider to auto
  simulateSavePreferredProvider(mockStorage, "visual.ai_image", "auto");
  assert.deepStrictEqual(simulateLoadPreferredProviders(mockStorage), {
    "speech.narration": "voxcpm2",
  });
});

test("buildVisualBody: preserves body-less request when options are unset/empty, sends imageProvider and videoProvider when set", () => {
  // Completely empty options -> body undefined (body-less request preserved)
  assert.strictEqual(buildVisualBody(undefined), undefined);
  assert.strictEqual(buildVisualBody({}), undefined);
  assert.strictEqual(buildVisualBody({ force: true }), undefined);
  assert.strictEqual(buildVisualBody({ imageProvider: "" }), undefined);
  assert.strictEqual(buildVisualBody({ imageProvider: "  " }), undefined);
  assert.strictEqual(buildVisualBody({ videoProvider: "" }), undefined);
  assert.strictEqual(buildVisualBody({ videoProvider: "  " }), undefined);

  // Set image provider
  assert.deepStrictEqual(buildVisualBody({ imageProvider: "comfyui-flux" }), {
    imageProvider: "comfyui-flux",
  });
  assert.deepStrictEqual(buildVisualBody({ imageProvider: "gemini-nanobanana" }), {
    imageProvider: "gemini-nanobanana",
  });

  // Set video provider
  assert.deepStrictEqual(buildVisualBody({ videoProvider: "comfyui-i2v" }), {
    videoProvider: "comfyui-i2v",
  });
  assert.deepStrictEqual(buildVisualBody({ videoProvider: "gemini-veo" }), {
    videoProvider: "gemini-veo",
  });

  // Combined with artDirection, imageProvider, videoProvider, productionRecipe
  assert.deepStrictEqual(
    buildVisualBody({
      imageProvider: "comfyui-flux",
      videoProvider: "comfyui-i2v",
      artDirection: "cinematic noir",
      productionRecipe: { id: "motion-comic", version: 2 },
    }),
    {
      imageProvider: "comfyui-flux",
      videoProvider: "comfyui-i2v",
      artDirection: "cinematic noir",
      productionRecipe: { id: "motion-comic", version: 2 },
    },
  );
});

test("buildAudioBody: preserves body-less request when options are unset/empty, sends speechProvider when set", () => {
  // Completely empty options -> body undefined (body-less request preserved)
  assert.strictEqual(buildAudioBody(undefined), undefined);
  assert.strictEqual(buildAudioBody({}), undefined);
  assert.strictEqual(buildAudioBody({ speechProvider: "" }), undefined);
  assert.strictEqual(buildAudioBody({ speechProvider: "  " }), undefined);

  // Set speech provider
  assert.deepStrictEqual(buildAudioBody({ speechProvider: "voxcpm2" }), {
    speechProvider: "voxcpm2",
  });
  assert.deepStrictEqual(buildAudioBody({ speechProvider: "gemini-tts" }), {
    speechProvider: "gemini-tts",
  });
});

test("transcribeSubtitleFromNarration: correctly builds endpoint url and payload with language or null", () => {
  function buildTranscribeUrl(projectId) {
    return `/api/content-projects/${encodeURIComponent(projectId)}/subtitle/transcribe`;
  }
  function buildTranscribeBody(language) {
    return { language: language ?? null };
  }

  assert.strictEqual(
    buildTranscribeUrl("01b7f0ef-1111-2222-3333-444455556666"),
    "/api/content-projects/01b7f0ef-1111-2222-3333-444455556666/subtitle/transcribe",
  );
  assert.strictEqual(
    buildTranscribeUrl("id/with/special chars"),
    "/api/content-projects/id%2Fwith%2Fspecial%20chars/subtitle/transcribe",
  );
  assert.deepStrictEqual(buildTranscribeBody(), { language: null });
  assert.deepStrictEqual(buildTranscribeBody(undefined), { language: null });
  assert.deepStrictEqual(buildTranscribeBody("id"), { language: "id" });
  assert.deepStrictEqual(buildTranscribeBody("en"), { language: "en" });
});
