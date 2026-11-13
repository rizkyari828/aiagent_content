import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const srcDir = path.resolve(__dirname, "../src");
const idPath = path.resolve(srcDir, "i18n/id.json");
const enPath = path.resolve(srcDir, "i18n/en.json");

function walkDir(dir, filterExt) {
  let results = [];
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results = results.concat(walkDir(full, filterExt));
    } else if (filterExt.some((ext) => entry.name.endsWith(ext))) {
      results.push(full);
    }
  }
  return results;
}

test("JSX verification: zero unlocalized raw text children in .tsx components", () => {
  const tsxFiles = walkDir(srcDir, [".tsx"]);
  assert.ok(tsxFiles.length > 0, "Should find .tsx files to scan");

  const hardcodedIssues = [];

  for (const file of tsxFiles) {
    const content = fs.readFileSync(file, "utf8");
    const lines = content.split("\n");

    lines.forEach((line, index) => {
      // Find text enclosed directly between tags: >Some Text<
      // Exclude comments, JS expressions, SVG paths, punctuation, symbols, whitespace
      const matches = line.match(/>\s*([A-Za-z][^<>{}]*[A-Za-z])\s*</g);
      if (matches) {
        for (const m of matches) {
          const raw = m.slice(1, -1).trim();
          // Skip pure technical symbols, URLs, numbers, math, and SVG entities
          if (
            raw &&
            !raw.startsWith("http") &&
            !raw.startsWith("//") &&
            !/^[\d\s.,:;!?%&+\-_/()#*]+$/.test(raw)
          ) {
            hardcodedIssues.push({
              file: path.relative(srcDir, file),
              line: index + 1,
              text: raw,
            });
          }
        }
      }
    });
  }

  assert.deepStrictEqual(
    hardcodedIssues,
    [],
    `Found hardcoded UI text strings in JSX:\n${hardcodedIssues
      .map((i) => `  at ${i.file}:${i.line} -> "${i.text}"`)
      .join("\n")}`,
  );
});

test("t() call sites: 100% of static t('key') calls map to valid keys in id.json and en.json", () => {
  const idJson = JSON.parse(fs.readFileSync(idPath, "utf8"));
  const enJson = JSON.parse(fs.readFileSync(enPath, "utf8"));
  const allFiles = walkDir(srcDir, [".tsx", ".ts"]);

  const missingKeys = [];

  for (const file of allFiles) {
    // Skip test files or the i18n definition files themselves
    if (file.includes("i18n.ts") || file.includes(".test.")) {
      continue;
    }

    const content = fs.readFileSync(file, "utf8");
    // Match t("some.key") or t('some.key')
    const matches = content.matchAll(/\bt\(["']([a-zA-Z0-9._-]+)["']/g);

    for (const match of matches) {
      const key = match[1];
      const inId = key in idJson;
      const inEn = key in enJson;

      if (!inId || !inEn) {
        missingKeys.push({
          file: path.relative(srcDir, file),
          key,
          inId,
          inEn,
        });
      }
    }
  }

  assert.deepStrictEqual(
    missingKeys,
    [],
    `Found t() calls with missing translation keys:\n${missingKeys
      .map((m) => `  at ${m.file} -> "${m.key}" (in id.json: ${m.inId}, in en.json: ${m.inEn})`)
      .join("\n")}`,
  );
});

test("ARIA regions: InlineError and Toast have proper live/alert semantics", () => {
  const inlineErrorPath = path.resolve(srcDir, "components/InlineError.tsx");
  const toastPath = path.resolve(srcDir, "components/Toast.tsx");

  const inlineErrorContent = fs.readFileSync(inlineErrorPath, "utf8");
  assert.ok(
    inlineErrorContent.includes('role="alert"') && inlineErrorContent.includes('aria-live="assertive"'),
    "InlineError must have role='alert' and aria-live='assertive'",
  );

  const toastContent = fs.readFileSync(toastPath, "utf8");
  assert.ok(
    toastContent.includes('role="status"') && toastContent.includes('aria-live="polite"'),
    "Toast region must have role='status' and aria-live='polite'",
  );
});
