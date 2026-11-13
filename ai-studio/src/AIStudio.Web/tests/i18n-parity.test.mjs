import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const idPath = path.resolve(__dirname, "../src/i18n/id.json");
const enPath = path.resolve(__dirname, "../src/i18n/en.json");

test("i18n files exist and parse as valid JSON objects", () => {
  assert.ok(fs.existsSync(idPath), "id.json should exist");
  assert.ok(fs.existsSync(enPath), "en.json should exist");

  const idRaw = fs.readFileSync(idPath, "utf8");
  const enRaw = fs.readFileSync(enPath, "utf8");

  assert.doesNotThrow(() => JSON.parse(idRaw), "id.json should parse without error");
  assert.doesNotThrow(() => JSON.parse(enRaw), "en.json should parse without error");

  const idJson = JSON.parse(idRaw);
  const enJson = JSON.parse(enRaw);

  assert.strictEqual(typeof idJson, "object", "id.json should be an object");
  assert.strictEqual(typeof enJson, "object", "en.json should be an object");
  assert.ok(!Array.isArray(idJson), "id.json should not be an array");
  assert.ok(!Array.isArray(enJson), "en.json should not be an array");
});

test("i18n key parity: 100% key parity between id.json and en.json", () => {
  const idJson = JSON.parse(fs.readFileSync(idPath, "utf8"));
  const enJson = JSON.parse(fs.readFileSync(enPath, "utf8"));

  const idKeys = new Set(Object.keys(idJson));
  const enKeys = new Set(Object.keys(enJson));

  const missingInEn = [...idKeys].filter((k) => !enKeys.has(k));
  const missingInId = [...enKeys].filter((k) => !idKeys.has(k));

  assert.deepStrictEqual(
    missingInEn,
    [],
    `Keys in id.json but missing from en.json: ${missingInEn.join(", ")}`,
  );
  assert.deepStrictEqual(
    missingInId,
    [],
    `Keys in en.json but missing from id.json: ${missingInId.join(", ")}`,
  );
  assert.strictEqual(
    idKeys.size,
    enKeys.size,
    `Total key count should match exactly (${idKeys.size})`,
  );
  assert.ok(idKeys.size > 0, "Translation catalogs must not be empty");
});

test("i18n values: non-empty strings and valid formatting", () => {
  const idJson = JSON.parse(fs.readFileSync(idPath, "utf8"));
  const enJson = JSON.parse(fs.readFileSync(enPath, "utf8"));

  for (const [key, val] of Object.entries(idJson)) {
    assert.strictEqual(
      typeof val,
      "string",
      `id.json key "${key}" must have string value`,
    );
    assert.ok(
      val.trim().length > 0,
      `id.json key "${key}" must not have empty or whitespace-only value`,
    );
  }

  for (const [key, val] of Object.entries(enJson)) {
    assert.strictEqual(
      typeof val,
      "string",
      `en.json key "${key}" must have string value`,
    );
    assert.ok(
      val.trim().length > 0,
      `en.json key "${key}" must not have empty or whitespace-only value`,
    );
  }
});

test("i18n parameter placeholder parity between id and en", () => {
  const idJson = JSON.parse(fs.readFileSync(idPath, "utf8"));
  const enJson = JSON.parse(fs.readFileSync(enPath, "utf8"));

  const extractPlaceholders = (str) => {
    const matches = str.match(/\{([a-zA-Z0-9_]+)\}/g) || [];
    return matches.sort();
  };

  for (const key of Object.keys(idJson)) {
    const idPlaceholders = extractPlaceholders(idJson[key]);
    const enPlaceholders = extractPlaceholders(enJson[key]);

    assert.deepStrictEqual(
      idPlaceholders,
      enPlaceholders,
      `Parameter placeholders mismatch for key "${key}": ID has [${idPlaceholders}] but EN has [${enPlaceholders}]`,
    );
  }
});

test("i18n required production provider keys exist in both locales", () => {
  const idJson = JSON.parse(fs.readFileSync(idPath, "utf8"));
  const enJson = JSON.parse(fs.readFileSync(enPath, "utf8"));

  const requiredProviderKeys = [
    "project.production.provider.auto",
    "project.production.provider.ollama",
    "project.production.provider.geminiText",
    "project.production.provider.deepseek",
    "project.production.provider.openai",
    "project.production.provider.comfyuiFlux",
    "project.production.provider.geminiNanobanana",
    "project.production.provider.comfyuiI2v",
    "project.production.provider.geminiVeo",
    "project.production.provider.voxcpm2",
    "project.production.provider.geminiTts",
    "project.production.provider.aceStep",
  ];

  for (const key of requiredProviderKeys) {
    assert.ok(key in idJson, `Required key "${key}" missing in id.json`);
    assert.ok(key in enJson, `Required key "${key}" missing in en.json`);
  }
});
