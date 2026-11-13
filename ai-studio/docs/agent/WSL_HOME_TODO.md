# WSL Home Execution Checklist & Master TODO

Updated: 2026-11-13
Target Machine: **Rumah — Windows WSL2 + NVIDIA RTX 4060 Ti 16 GB + PostgreSQL 18.6 (`127.0.0.1:5432`) + Ollama + ComfyUI + Blender + VoxCPM2 + ACE-Step + Faster-Whisper**.

Dokumen ini adalah panduan eksekusi berurutan agar mesin WSL bisa langsung melanjutkan seluruh kerjaan dari Mac + menyatukan modul `Research` (`Gemini Pro`) yang sedang dikerjakan di rumah tanpa konflik.

Dokumen rujukan utama:
- `AGENTS.md` (repo root) & `ai-studio/AGENTS.md`
- `ai-studio/docs/agent/CHECKPOINT.md` (resume terbaru Mac `main` @ `e56995a`+)
- `ai-studio/docs/development/CONTENT_INTELLIGENCE_RESEARCH_DESIGN.md` (desain lengkap Research + CLI + Prompt QA)
- `ai-studio/docs/development/I2V_HOME_TASK.md` & `I2V_PLAN.md` (workflow & benchmark ComfyUI I2V)
- `ai-studio/docs/development/RESTORE_AND_OFFLINE_RUNBOOK.md` (verifikasi `AC-10` & `AC-11`)
- `ai-studio/docs/development/P1_READINESS.md` (matrix kelulusan P1)
- `ai-studio/docs/agent/UI_TODO.md` (cara jalankan Vite + API proxy `127.0.0.1:5173 -> 5002`)

---

## 0. WAJIB PERTAMA: Sinkronisasi Git Aman (Lindungi Kerjaan `Research` di WSL)

Karena di mesin WSL masih ada kerjaan lokal `Research` (`Gemini Pro`) yang belum di-push, **jangan langsung `git pull` di worktree kotor**:

```bash
cd /path/ke/aiengine
git status
git branch --show-current
git log -n 3 --oneline
```

1. **Jika kerjaan `Research` belum di-commit:**
   ```bash
   git switch -c feat/research-gemini-pro
   git add -A
   git commit -m "wip: save local Research service and Gemini Pro work from home WSL"
   ```
2. **Pull semua update Mac ke `main` lokal:**
   ```bash
   git fetch origin --prune
   git switch main
   git pull --ff-only origin main
   ```
3. **Gabungkan branch `Research` ke `main` (selesaikan konflik jika ada di `DependencyInjection.cs` / docs secara hati-hati):**
   ```bash
   git switch feat/research-gemini-pro
   git rebase main          # atau git merge main
   ```
4. **Setelah lolos build + full test (Langkah 1 di bawah), merge ke `main` & push:**
   ```bash
   git switch main
   git merge --no-ff feat/research-gemini-pro
   git push origin main feat/research-gemini-pro
   ```

---

## 1. Checklist Verifikasi Sistem & Full DB Suite di WSL

Pastikan PostgreSQL WSL aktif (`pg_isready -h 127.0.0.1 -p 5432`) dan jalankan dari `ai-studio/`:

- [ ] **1.1 Build Release & Apply Migrasi (jika ada migrasi baru dari branch `Research`):**
  ```bash
  cd ai-studio
  dotnet build AIStudio.slnx -c Release
  # Jika branch Research menambah migrasi EF:
  dotnet ef database update --project src/AIStudio.Infrastructure --startup-project src/AIStudio.Api
  ```
- [ ] **1.2 Full Test Suite WITH PostgreSQL (`Persistence` + Non-Persistence):**
  ```bash
  ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=aistudio;Username=postgres;Password=<PASSWORD>" \
    dotnet test AIStudio.slnx -c Release --no-build
  ```
- [ ] **1.3 Format & Diff Hygiene:**
  ```bash
  dotnet format AIStudio.slnx --no-restore --verify-no-changes
  git diff --check
  ```
- [ ] **1.4 Frontend Rebuild (Wajib `npm ci` di WSL untuk native `@rolldown/binding` Linux):**
  ```bash
  cd src/AIStudio.Web
  npm ci
  npx tsc --noEmit
  npx vite build
  npm test
  ```
- [ ] **1.5 Live API + Vite Smoke (`127.0.0.1:5173 -> 127.0.0.1:5002`):**
  - Terminal 1: `cd ai-studio && dotnet run --project src/AIStudio.Api`
  - Terminal 2: `cd ai-studio/src/AIStudio.Web && npm run dev`
  - Cek `/health/ready`, halaman Projects, Project Detail, dan Settings capabilities.

---

## 2. Checklist Verifikasi Hardware/Runtime Baru dari Mac

Fitur-fitur berikut sudah **selesai dan teruji deterministik di Mac** (`code-only`), namun butuh pembuktian di mesin rumah yang punya GPU & binary lengkap:

- [ ] **2.1 Dynamic Media Routing (`RoutingImageGenerationProvider`, `RoutingVideoGenerationProvider`, `RoutingSpeechSynthesisProvider`):**
  - Uji selector UI di `ProductionSection.tsx` dengan `comfyui-flux` vs `gemini-nanobanana`, `voxcpm2` vs `gemini-tts`.
- [ ] **2.2 Real Video Preview Player (`AC-05`):**
  - Jalankan `RenderVideo` pada 1 proyek nyata -> buka tab **Publish** di `AIStudio.Web` -> pastikan `<video controls>` streaming HTTP 200/206 dari `GET /api/content-projects/{id}/render-jobs/{jobId}/preview` berfungsi mulus (seek/scrub lancar).
- [ ] **2.3 Faster-Whisper Transcription Adapter (`PRD §35`):**
  - Pastikan environment Python memiliki `faster-whisper` (`pip install faster-whisper`).
  - Uji `POST /api/content-projects/{id}/subtitle/transcribe` dengan `Transcription:Enabled=true` terhadap `narration.wav` -> pastikan `subtitles/generated.srt` terbentuk dan terbaca di **Subtitle Editor** (`AssetsAudioSection.tsx`).
- [ ] **2.4 I2V Benchmark & Real Workflow (`I2V_HOME_TASK.md`):**
  - Download weights LTX-Video 2B / Wan2.2 TI2V-5B di ComfyUI.
  - Author `i2v_workflow.json` & `i2v_workflow_first_frame.json` sesuai node `GET /object_info`, jalankan benchmark 16 GB VRAM dan rekam bukti sesuai `I2V_BENCHMARK_SPEC.md`.
- [ ] **2.5 Real Audio Generation E2E:**
  - Set env `AISTUDIO_VOXCPM_*` / `AISTUDIO_ACESTEP_*` lalu jalankan `AudioGenerationEndToEndTests.RealProviders_SynthesizeNarrationAndMusic_ThenMix` agar status tidak skipped.

---

## 3. Checklist Pengembangan Lanjutan (Sesuai Desain Baru)

### 3A. Content Intelligence & Research Server (`CONTENT_INTELLIGENCE_RESEARCH_DESIGN.md`)
- [ ] **R1:** Rekonsiliasi & sempurnakan modul `Research` + `Gemini Pro` hasil merge mesin rumah dengan port `ITrendCollector`, `IResearchSynthesizer`, `ResearchPackage` (Section 5).
- [ ] **R2 — Collector Deterministik + Cache:**
  - [ ] `YouTubeDataApiTrendCollector` (`mostPopular ID`, `search`, `ViewsPerHour`, `OutlierRatio`, `EngagementRate`, + `commentThreads.list` comment mining).
  - [ ] `GoogleTrendsRssCollector` + `WatchlistRssCollector` (channel/blog/changelog RSS gratis).
  - [ ] `OperatorSeed` input untuk link/judul TikTok/FYP tanpa bypass anti-bot/CAPTCHA.
  - [ ] `TrendCache` (TTL 6–12 jam) + URL canonicalization & SHA-256 snippet deduplication.
- [ ] **R3 — `AC-03` Persistence + Research Features + UI:**
  - [ ] Migrasi DB `research_packages`, `research_sources`, `evidence_claims` (`verified_stat` vs `synthesis_hypothesis`).
  - [ ] `Transcript & Pacing Deconstructor`, `Content Angle Matrix` (6 angles), `Visual Proof Suggester`, `Saturation/Novelty Check`, `Pre-Production Fact & Policy Screener`, `Multi-Format Atomizer`, dan tag `Traffic Magnet vs Offer Bridge`.
  - [ ] UI **Research & Trend Finder** + tombol **"Promosikan ke Proyek / GenerateIdea"**.
- [ ] **R4 — Experimental `CliTextGenerator` (Opsional Pribadi):**
  - [ ] Implementasi `CliAiOptions` + `CliTextGenerator : IAiTextGenerator` memakai `@google/gemini-cli` (`gemini -p --output-format json`), `Enabled = false` by default, Priority `20`, `Concurrency = 1`, temp dir kosong, closed stdin, & circuit breaker `429`.
  - [ ] Tambah `provider` opsional ke `GenerateStoryboardJobPayload`.

### 3B. Peningkatan Kualitas Konten & Pre-Render QA (`Bagian 8 & 10`)
- [ ] **Prompt Quality Upgrades:**
  - [ ] `GenerateIdeaPrompt`: opsi multi-angle (3 alternatif per call) + negative history 10–15 judul proyek lama.
  - [ ] `GenerateScriptPrompt`: word budget per beat (~2.2–2.4 kata/dtk) + 3-second hook budget (`<= 18` kata) + normalisasi pelafalan angka/singkatan TTS Indonesia.
  - [ ] `GenerateStoryboardPrompt`: `heading` bahasa proyek, **`visual` wajib Bahasa Inggris deskriptif** + continuity anchor.
- [ ] **Pre-Render CPU Quality Check (sebelum GPU):**
  - [ ] Deteksi deviasi durasi kata & leakage karakter `[SFX]`/`(cut)`/URL pada teks narasi.
  - [ ] Tambah pengecekan **SRT Readability (`CPS <= 20`, `<= 42` char/baris, max `2` baris)** di `SubtitleWorkflow`.
- [ ] **Post-Approved Packaging Metadata:** Job/endpoint menyusun 3 judul CTR, deskripsi SEO, hashtag, & thumbnail brief sesudah `ScriptReview` di-approve.
- [ ] **Closed-Loop Learning (`PRD §32`):** Tangkap optional `CorrectionReason` saat revisi + ringkasan `SupportedGuideline` (dipromosikan manual oleh operator).

### 3C. UI Polish & Kelulusan Operasional P1 (`P1_READINESS.md`)
- [ ] Tambah tombol *"Buat Subtitle dari Narasi"* (`POST .../subtitle/transcribe`) di `AssetsAudioSection.tsx` + `api/media.ts`.
- [ ] Code-splitting `AIStudio.Web` untuk menurunkan chunk JS (`501.28 kB` -> `< 500 kB`) + visual Figma audit (`UI-01`).
- [ ] Sisa skema P1 lainnya bila dibutuhkan (`AC-04` Asset Rights/Eligibility status, `AC-09` Revenue/Income tracking).
- [ ] **P1 Gates (`AC-08`, `AC-10`, `AC-11`):**
  - [ ] Produksi & publikasi 1 video nyata ke YouTube -> catat real `videoId` di `PublicationRecord` (`AC-08`).
  - [ ] Uji restore package `.zip` lintas DB/directory baru (`AC-10` per `RESTORE_AND_OFFLINE_RUNBOOK.md`).
  - [ ] Uji full run pipeline dalam mode offline penuh tanpa koneksi internet (`AC-11`).
- [ ] Update `CHECKPOINT.md`, `STATE.md`, dan `P1_READINESS.md` dengan angka & bukti nyata hasil run WSL.

---

## 4. Ready-to-Paste Prompt untuk Coding Agent di Mesin WSL

Salin blok prompt di bawah ini saat membuka sesi agent baru di mesin WSL rumah:

```text
BEGIN PROMPT

Kamu bekerja di repositori AI Studio pada mesin rumah (Windows WSL2, RTX 4060 Ti 16 GB VRAM, PostgreSQL aktif, Ollama, ComfyUI, Blender, VoxCPM2, ACE-Step).

Aturan wajib:
1. Baca secara berurutan sebelum mengubah kode apa pun:
   - AGENTS.md (root repo) & ai-studio/AGENTS.md
   - ai-studio/docs/agent/CHECKPOINT.md
   - ai-studio/docs/context/STATE.md
   - ai-studio/docs/agent/REPO_MAP.md
   - ai-studio/docs/agent/WSL_HOME_TODO.md
   - ai-studio/docs/development/CONTENT_INTELLIGENCE_RESEARCH_DESIGN.md
2. JANGAN timpa atau hapus pekerjaan lokal Research + Gemini Pro yang belum di-push!
   - Jalankan Langkah 0 di `WSL_HOME_TODO.md` lebih dulu (amankan kerjaan lokal ke branch, pull `main` dari `origin/main`, lalu merge/rebase dengan rapi).
3. Setelah merge bersih:
   - Jalankan Langkah 1 di `WSL_HOME_TODO.md` (build Release, apply migrasi jika ada, jalankan FULL `dotnet test AIStudio.slnx -c Release` termasuk `Persistence` dengan PostgreSQL, `dotnet format --verify-no-changes`, dan `npm ci && npx tsc --noEmit && npx vite build && npm test` di `src/AIStudio.Web`).
4. Laporkan status persis hasil sinkronisasi + full test kepada saya, lalu kita eksekusi berurutan mulai dari Langkah 2 (verifikasi Render Preview AC-05, Faster-Whisper STT, I2V `I2V_HOME_TASK.md`) dan Langkah 3 (ekspansi Research Server & Prompt/Pre-Render QA sesuai `CONTENT_INTELLIGENCE_RESEARCH_DESIGN.md`).
5. Komunikasi singkat dalam Bahasa Indonesia; kode dan identifier berbahasa Inggris; catat bukti terverifikasi di `CHECKPOINT.md` dan `STATE.md` sebelum commit akhir.

END PROMPT
```