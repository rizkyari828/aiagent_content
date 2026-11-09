# UI Implementation — Content Studio Web

Status: 2026-11-09. Frontend-only: no backend, API contract, domain, job, schema,
or provider change. Implemented on branch `feat/ui-projects` (from `main`
`72ba50f`) and merged to `main`. Handoff, workflow, and remaining screens:
`docs/agent/UI_TODO.md`.

This document records the implemented UI foundation and the first product screen
(`Projects`). Visual design follows the agreed direction in the UI task and
`docs/design/FIGMA-HANDOFF.md`; it is not final Figma branding.

## Design conventions

- **Light-first with dark support.** All colors are CSS variables in
  `src/styles/tokens.css`. `[data-theme="light|dark"]` sets the active theme, and
  `@media (prefers-color-scheme: dark)` covers the pre-hydration first paint.
- **Theme modes.** `auto | light | dark`, default `auto`, stored in
  `localStorage` under `ai-studio.theme`. `useTheme` resolves `auto` through the
  media query, applies `data-theme` + `color-scheme`, and reacts to OS changes.
- **i18n without a library.** `id.json` (default) and `en.json`, provider/hook
  `useI18n()`, `t("key", { name })` with simple `{name}` interpolation, locale in
  `localStorage` under `ai-studio.locale`, and `<html lang>` kept in sync.
  `src/i18n/format.ts` owns `Intl` date/number helpers.
- **Density.** Base font 14px, table rows 48px, inputs 38px, sidebar 248px,
  topbar 56px, content max width 1120px. Radius scale: 6/10/14px, with 10px as
  the control default. One indigo accent (`#4F46E5`; `#6366F1` in dark for
  contrast) plus neutral, success, warning, and danger tones.
- **Conventions.** Left sidebar (Projects, Runs, Settings) + breadcrumb in the
  topbar; primary action at the top-right of the page header; inline field
  validation; inline error with retry; loading skeletons; one-sentence empty
  state with one action; confirmation dialog before discarding a typed project
  draft; success toast after creation.
- **Copy.** Human instructions first; stable API error `code` only as a secondary
  line. No UI copy is hardcoded in components: every visible string comes from
  `t(...)`.
- **Accessibility.** Skip link, visible `:focus-visible` ring, explicit form
  labels, `aria-invalid` + `aria-describedby`, `role="alert"` +
  `aria-live="assertive"` for load errors, `role="status"` skeleton text,
  `role="dialog"` + `aria-modal`, `Esc` closes, focus trap, focus restore, body
  scroll lock while open, and reduced-motion support.

## Folder structure

```text
src/AIStudio.Web/
  index.html                      # lang="id"; AppShell updates <html lang>
  src/
    main.tsx                      # I18nProvider > ToastProvider > App
    App.tsx                       # route switch: projects | settings
    styles/tokens.css             # colors, spacing, radius, shadows (light/dark)
    styles/app.css                # shell, components, responsive, a11y
    i18n/i18n.tsx                 # Locale, provider, useI18n, t(key, params)
    i18n/format.ts                # Intl date/number helpers + useFormatters
    i18n/id.json, i18n/en.json    # key-parity enforced at type level
    theme/useTheme.ts             # auto/light/dark store + media-query listener
    api/client.ts                 # fetch wrapper, timeout, normalized ApiError
    api/projects.ts               # listProjects / createProject
    routing/useHashRoute.ts       # #/projects, #/settings; no dependency
    components/                   # Button, Badge, Card, Table, EmptyState,
                                  # Skeleton, InlineError, Dialog, Toast,
                                  # Preferences (language/theme), icons
    layout/AppShell.tsx           # sidebar, topbar, breadcrumb, skip link
    screens/ProjectsScreen.tsx    # list + create flow
    screens/SettingsScreen.tsx    # theme/language/API preference shell
```

## Screens

| Screen | Route | Status | Notes |
| --- | --- | --- | --- |
| Projects | `#/projects` | Implemented | List, skeleton, inline error + retry, empty state, status badges, `Intl` updated-at, create dialog with title/brief validation, success toast, refresh after create. |
| Settings | `#/settings` | Implemented (shell) | Theme, language, read-only API base URL. Production settings follow. |
| Runs | none | Not built | Sidebar entry is disabled with a translated "Soon" hint. |
| Project detail | none | Not built | Next UI slice (brief/script/assets/audio/production/review/publish/metrics). |

## Routing

`src/routing/useHashRoute.ts` is a minimal hash router: internal React state is
the source of truth and the hash (`#/projects`, `#/settings`) makes a screen
bookmarkable and reload-safe. Unknown hashes fall back to Projects. No routing
package was added.

## API integration

- `src/api/client.ts`: base URL from `VITE_API_BASE_URL` (default
  `http://127.0.0.1:5080`), trailing slash trimmed, 15s timeout through one
  `AbortController`, JSON parsing, and normalization of problem+json failures to
  `{ code?, title, detail?, status }`; `errors` is preserved as `fieldErrors`.
  The stable `code` extension is read from the problem body.
- `src/api/projects.ts`: `GET /api/content-projects?take=1..100` (clamped) and
  `POST /api/content-projects` with `{ title, brief? }`. No other endpoint is
  called yet.
- The Projects screen maps transport codes to human copy: `network_error`,
  `request_timeout`, other HTTP failures (with the code shown secondarily).
  Server-side field errors are mapped back to the `title`/`brief` fields.

## Toolchain notes (important)

- Commands were run with **system Node 20.17.0** and npm 10.8.2. Do not use the
  bundled DSH Node 24: the native rolldown binding fails to load because of code
  signing on this machine.
- Vite 8.3.0 prints `Vite requires Node.js version 20.19+ or 22.12+` as a
  warning; the production build still succeeds with Node 20.17.0 (verified).
- `@rolldown/binding-darwin-arm64@1.2.8` is installed via
  `npm i --no-save` for this environment. It is intentionally **not** in
  `package.json`; rerun that exact command only if `node_modules` is reinstalled.
- `.npm-cache/` is ignored by the repository-root `.gitignore`.
- No dependency was added: `package.json` and `package-lock.json` are unchanged.
  The pre-existing `engines.node` range (`>=24.17.0 <25`) still does not match
  the verified Node 20.17.0 toolchain; npm only warns, and it was left untouched
  to keep this change frontend-only.

## Verification (exact, 2026-11-09)

```text
$ npx tsc --noEmit
(no output)                                            exit 0

$ npx vite build
vite v8.3.0 building client environment for production...
transforming...
✓ 39 modules transformed.
rendering chunks...
dist/index.html                   0.46 kB │ gzip:   0.28 kB
dist/assets/index-IGVa9gss.css   21.52 kB │ gzip:   4.64 kB
dist/assets/index-o1gU8bMt.js   253.51 kB │ gzip:  78.39 kB
✓ built in 97ms                                        exit 0
```

The Node 20.17 warning above appears before the successful build; it is a
warning only.

Additional headless smoke (Chrome, built `dist/` served locally, stub API on
`127.0.0.1:5080`) covered: populated table with Indonesian `Intl` dates and
status badges; empty state; offline inline error with `network_error` secondary
code and retry; dark theme + English from persisted localStorage with
`<html lang="en">`; dialog field validation (`Isi judul proyek dulu.`, focus
returned to the title input, `aria-invalid`); `Esc` closing the dialog; and a
create flow that POSTed, closed the dialog, showed the success toast, and
refreshed the row. No backend was modified; the stub lived outside the repo.

## Human checks in the browser

1. Run the API on the expected base URL and allow the web origin (CORS) — this
   checkout's `launchSettings.json` uses `http://127.0.0.1:5002` and the API has
   no CORS policy, while the UI defaults to `http://127.0.0.1:5080` per the task.
   Use `VITE_API_BASE_URL` or a proxy if the real backend listens elsewhere.
2. Run `npm run dev` (Vite) and check Projects loading/empty/error/create, toast,
   status badges, and date formatting.
3. Toggle Auto/Light/Dark and Indonesia/English; reload to confirm persistence,
   then check the dark palette contrast on the table and dialog.
4. Keyboard pass: skip link, `Tab` through the dialog (focus stays inside),
   `Esc` closes, `Enter` in the title field submits, validation focus, retry
   button reachable.
5. Responsive pass at ~1024px, ~768px, and ~375px (sidebar collapses, table
   scrolls horizontally, topbar wraps).
6. Confirm `/api/content-projects` error envelopes still carry the `code`
   extension used by the secondary error line.

## Follow-ups

- Project detail screen and its actions (readiness, jobs, approvals,
  publication, export/import).
- Runs screen (job status/cancel) and production Settings.
- Import/export UI, plus project open/navigation from the table.
- Optional relative timestamps and a "updated by" column once the API exposes
  them; do not invent fields client-side.
