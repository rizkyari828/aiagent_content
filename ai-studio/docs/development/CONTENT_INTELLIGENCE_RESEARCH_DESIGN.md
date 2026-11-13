# Content Intelligence, AI Researcher & CLI Experimental Provider — Design Spec

Status: **DESIGN DRAFT** (2026-11-13, MacBook `code-only`).
Lingkup: Arsitektur perencanaan — **belum ada kode C#/TS/migrasi yang ditulis di Mac** agar tidak berbenturan dengan service `Research` + `Gemini Pro` yang sedang dikerjakan di mesin rumah.

Dokumen terkait:
- `docs/product/parts/04_JOBS_AND_ARTIFACTS.md` (§20 `ResearchPackage` artifact)
- `docs/product/parts/08_HERMES_AGENT_AND_WSL_SECURITY.md` (§38–§40 bounded adaptive research, allow/deny tools)
- `docs/product/parts/10_EXECUTION_ROADMAP.md` (§48 lightweight research automation & human learning loop)
- `docs/product/parts/13_EXTRACTABLE_MODULE_BOUNDARIES.md` (A5 *Content Intelligence*, `ADR-018`, `ADR-019`)
- `docs/development/P1_READINESS.md` (`AC-03` Sources / Evidence / Claims gap)

---

## 1. Masalah & Prinsip Desain

Operator ingin AI membantu **riset topik viral / tren (YouTube, web, short-form/TikTok)** lalu mengubahnya menjadi **kandidat ide** (`GenerateIdea` / `GenerateScript`), sambil tetap bisa bereksperimen memakai CLI subscription Pro (`@google/gemini-cli`) secara aman tanpa biaya API berlebih atau halusinasi data.

Lima aturan dasar arsitektur:
1. **Collector Terpisah dari Synthesizer:** Pengambilan angka statistik riil (views, likes, tanggal publikasi, URL sumber) dilakukan oleh **Collector deterministik** (API resmi / RSS / input operator), bukan ditebak LLM. LLM hanya bertugas menganalisis, membandingkan pola, dan menyintesis kandidat ide dari bukti nyata.
2. **Evidence & Provenance Wajib (`AC-03`):** Setiap temuan riset memisahkan **fakta terverifikasi sumber** (`sourceUrl`, `retrievedAt`, `metricSnapshot`) dari **hipotesis/kesimpulan sintesis AI**.
3. **Human Selection Gate (A1):** AI Researcher mengusulkan 3–5 `IdeaCandidate`; operator memilih mana yang dipromosikan menjadi `ContentProject` / job `GenerateIdea` atau `GenerateScript`. AI tidak membuat proyek atau menerbitkan secara otomatis.
4. **Kepatuhan Akses Platform (No Anti-Bot Bypass):** Tidak pernah menembak atau mengakali CAPTCHA, anti-bot, atau login wall (khususnya TikTok). Platform tertutup dilayani lewat leaderboard publik, rangkuman web search resmi, atau seed link manual operator.
5. **CLI Provider Strictly Opt-In:** Adapter CLI (`CliTextGenerator`) adalah eksperimen dev pribadi (`Enabled = false` secara default), berada di Priority terendah di bawah Ollama (`100`) & API Key (`50`), dengan rate limit + cooldown ketat.

---

## 2. Arsitektur 3-Lapis End-to-End

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ LAPIS 1: DETERMINISTIC COLLECTORS (Factual Signals, No LLM Hallucination) │
├──────────────────────────┬───────────────────────┬──────────────────────┤
│ YouTube Data Collector   │ Web & Trends          │ Operator FYP Seed    │
│ - YouTube Data API v3    │ - Google Trends RSS   │ - Link / Judul FYP   │
│   mostPopular (ID) &     │ - Gemini Web Search   │   dari TikTok/Reels  │
│   search (viewCount)     │   Grounding (read-only│ - TikTok Creative    │
│ - Channel RSS / yt-dlp   │   + citations)        │   Center public tags │
│   metadata-only          │                       │                      │
└────────────┬─────────────┴──────────┬────────────┴──────────┬───────────┘
             │                        │                       │
             └────────────────────────┼───────────────────────┘
                                      ▼
                     `TrendSignal` + `ResearchSource` (cached TTL 6-24h)
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ LAPIS 2: BOUNDED RESEARCH SYNTHESIZER (IAiTextGenerator / Gemini Pro)   │
│ - Gabungkan sinyal + dedup topik yang sudah ada di DB proyek            │
│ - Analisis: Why Trending, Hook 3s, Content Gap, Target Audience         │
│ - Ekstrak `EvidenceClaim` (`verified_stat` vs `synthesis_hypothesis`)   │
│ - Hasilkan `ResearchPackage` JSON tervalidasi + sitasi `sourceIds[]`    │
└─────────────────────────────────────┬───────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ LAPIS 3: IDEA & PRODUCTION HANDOFF                                      │
│ - 3–5 `IdeaCandidate` (Topic, Hook, Angle, SuggestedFormat, Sources)    │
│ - Operator pilih 1 kandidat -> "Buat Proyek / GenerateIdea"             │
│ - Membawa `ResearchPackageId` & `EvidenceClaims` (`AC-03`) ke Script    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Strategi Pengumpulan Tren Per Platform

| Platform | Metode Teknis | Data yang Diambil | Batasan & Mitigasi |
| :--- | :--- | :--- | :--- |
| **YouTube** | **YouTube Data API v3** (`videos?chart=mostPopular&regionCode=ID`, `search?order=viewCount`) + Channel Atom RSS | `videoId`, `title`, `channelTitle`, `publishedAt`, `duration`, `viewCount`, `likeCount`, `commentCount`, `tags` | API resmi gratis (10.000 unit/hari). Hitung `viewsPerHour` & `engagementRatio` secara deterministik di C#. |
| **Google Trends / Web** | **Google Trends RSS** (`trendingSearches?geo=ID`) + **Web Search Grounding** (`GeminiSearch` / `gemini` CLI tool) | Topik naik daun, ringkasan berita/artikel, URL sumber | Wajib verifikasi URL HTTP/HTTPS valid; simpan timestamp `retrievedAt`. |
| **TikTok / Short-Form FYP** | **TikTok Creative Center** (hashtag/lagu publik) + **Search Grounding** ("tren TikTok Indonesia") + **Operator FYP Seed Input** | Link publik, hashtag, hook 3 detik, deskripsi tren dari user | TikTok tidak punya API FYP gratis & punya proteksi anti-bot. Operator cukup tempel 3–5 topik/link FYP untuk dianalisis polanya. |

---

## 4. Desain Experimental CLI Text Adapter (`CliTextGenerator : IAiTextGenerator`)

Untuk menguji pemanfaatan subscription Pro di mesin dev/eksperimen tanpa mengorbankan stabilitas dan kuota kerja harian:

### 4.1 Pilihan CLI: `@google/gemini-cli` vs Antigravity CLI (`agy`)
- **Prioritas Utama untuk Runtime App: `@google/gemini-cli`**
  - Perintah: `gemini -p "<prompt>" --output-format json`
  - Keunggulan: Mengembalikan payload JSON terstruktur `{"response":"...","stats":{...},"error":...}` dan menggunakan kuota CLI Google/Code Assist yang terpisah dari window 5-jam Antigravity IDE.
- **Tidak Direkomendasikan sebagai Backend App: `agy` CLI**
  - Dirancang sebagai full workspace coding agent (memuat rules/konteks IDE) dan berbagi window kuota 5-jam Antigravity yang sama, sehingga mudah memicu `429 RESOURCE_EXHAUSTED` saat user coding.

### 4.2 Guardrails Wajib
1. **Default Disabled & Lowest Priority:**
   - Config `CliAi:Enabled = false` by default (`CliAiOptions`).
   - Didaftarkan di `ProductionCapabilityCatalog` (`CapabilityIds.TextGeneration`, `ProviderId = "gemini-cli"`, `Priority = 20`, `RuntimeCategory = "local_cli_experimental"`).
2. **Isolasi Direktori & Keamanan Proses:**
   - Eksekusi lewat `IProcessRunner` (`ProcessRunRequest` + `ArgumentList`, tanpa shell `/bin/sh -c`).
   - Binary di-whitelist oleh config (`CliAi:Executable`), tidak pernah diterima dari input HTTP.
   - `WorkingDirectory` diarahkan ke folder temp kosong (`Directory.CreateTempSubdirectory`) agar CLI tidak membaca `AGENTS.md`, source code, atau rahasia repo.
   - **Stdin ditutup/kosong** (fail fast bila OAuth expire, tidak hang menunggu prompt login) + `--dangerously-skip-permissions` **dilarang keras**.
   - Untuk pemanggilan `IAiTextGenerator` (`GenerateIdea`, `GenerateScript`, `GenerateStoryboard`, `ResearchSynthesis`), matikan tool-edit/shell; hanya izinkan mode web search read-only khusus jika dipanggil oleh `ResearchCollector` berizin eksplisit.
3. **Concurrency Lock & Circuit Breaker `429`:**
   - `SemaphoreSlim(1, 1)` internal agar Worker tidak men-spawn proses CLI paralel.
   - Bila CLI gagal karena kuota/`429` atau timeout berturut-turut, aktifkan cooldown lokal (`CooldownSeconds = 300..900`) dan langsung lempar `AiGenerationException(AiErrorCode.RateLimited / Unavailable)` tanpa retry loop agresif.
4. **Sanitasi Output JSON:**
   - Ekstrak field `.response` dari `--output-format json`, strip markdown code fence ```` ```json ... ``` ```` bila ada, lalu serahkan ke validator domain/DTO.

---

## 5. Kontrak Data & Rancangan Modul (Transport-Neutral per `ADR-018`)

Semua kontrak berada di `AIStudio.Application` sebagai immutable record tanpa ketergantungan EF/HTTP/CLI:

### 5.1 Kontrak Inti
- **`TrendSignal`**:
  - `SourceType` (`youtube` | `google_trends` | `web_search` | `operator_seed`)
  - `ExternalId`, `Title`, `Url`, `ChannelOrAuthor`, `PublishedAt`, `RetrievedAt`
  - `Metrics`: `ViewCount?`, `LikeCount?`, `CommentCount?`, `VelocityScore?`
  - `RawSummary` (caption/snippet terpotong aman, max ~1.500 char)
- **`ResearchSource` (`AC-03`)**:
  - `Id`, `ContentProjectId?`, `Title`, `Url`, `SourceType`, `RetrievedAt`, `SnapshotHash`
- **`EvidenceClaim` (`AC-03`)**:
  - `Id`, `ResearchPackageId`, `SourceId`, `ClaimText`, `ClaimKind` (`verified_stat` | `observed_pattern` | `synthesis_hypothesis`), `ConfidenceNote?`
- **`IdeaCandidate`**:
  - `Title`, `Hook`, `TargetAudience`, `Angle`, `SuggestedFormat`, `WhyTrendingNow`, `SupportingSourceIds` (`IReadOnlyList<Guid>`), `RecommendedVisualStyle` (`ai_image` | `motion_comic` | `explainer`)
- **`ResearchPackage`**:
  - `Id`, `QueryTopic`, `RegionCode` (default `ID`), `CreatedAt`, `ProviderUsed`, `Sources`, `Claims`, `Candidates`

### 5.2 Application Ports
- `ITrendCollector` (`SourceType`, `bool IsEnabled`, `Task<IReadOnlyList<TrendSignal>> CollectAsync(TrendQuery, CancellationToken)`)
- `IResearchSynthesizer` (`Task<ResearchPackage> SynthesizeAsync(ResearchRequest, CancellationToken)`) — di belakangnya bisa memakai hasil merge `Research` + `Gemini Pro` dari mesin rumah, `Ollama`, atau eksperimental `CliTextGenerator`.
- `IResearchRepository` (simpan & cache `ResearchPackage`, `ResearchSource`, `EvidenceClaim`).

### 5.3 Rencana HTTP Endpoints & Storage (Saat Implementasi)
- **Database (1 migrasi terpadu pasca-merge mesin rumah):**
  - `research_packages` (hasil riset + TTL cache key `(topicHash, region, dateBucket)`)
  - `research_sources` (`AC-03`)
  - `evidence_claims` (`AC-03`, FK ke `research_packages` & `research_sources`)
  - `content_projects.source_research_package_id` *(nullable FK opsional untuk melacak asal ide proyek)*
- **API Endpoints (`ResearchEndpoints.cs`):**
  - `POST /api/research/collect` — jalankan collector (YouTube API + Google Trends + optional operator seed URLs) dengan cache TTL 6–24 jam.
  - `POST /api/research/synthesize` — sintesis `ResearchPackage` + 3–5 `IdeaCandidate`.
  - `GET /api/research/packages/{id}` & `GET /api/content-projects/{id}/sources` (`AC-03` evidence/claims per proyek).
  - `POST /api/research/packages/{id}/candidates/{index}/promote` — buat `ContentProject` baru (plus prefill brief + tautkan `research_sources` ke proyek tersebut).

---

## 6. Dukungan Fitur di Pipeline Eksisting (`AI Studio`)

| Tahap Pipeline | Dukungan CLI (`CliTextGenerator`) | Dukungan Collector (`YouTube` / `Trends`) |
| :--- | :--- | :--- |
| **0. Trend Discovery & Research (`AC-03`)** | ✅ Analisis pola tren, hook, & sintesis `ResearchPackage` | ✅ Ambil metrik riil YouTube, Google Trends, & web links |
| **1. `GenerateIdea`** | ✅ (`payload.Provider` UI/API sudah siap) | ✅ Memberi input topik & bukti sumber (`SupportingSourceIds`) |
| **2. `GenerateScript`** | ✅ (`payload.Provider` UI/API sudah siap) | ✅ Memakai `EvidenceClaim` agar naskah akurat & punya sitasi |
| **3. `GenerateStoryboard`** | ✅ *(perlu tambah `payload.Provider` opsional bila ingin pilih per-job)* | ⚠️ Opsional (inspirasi pacing/shot) |
| **4. `CreativeDirector` / `StoryDirector` / `StoryBible`** | ✅ Otomatis bila `AiOptions.Provider` diarahkan ke CLI | — |
| **5. Visual, Audio, Render, STT** | ❌ Tetap di ComfyUI / Veo / VoxCPM2 / ACE-Step / FFmpeg / Faster-Whisper | — |

---

## 7. Roadmap Implementasi Bertahap (Setelah Merge Mesin Rumah)

1. **Fase R1 — Rekonsiliasi dengan Branch Mesin Rumah:**
   - Pull/merge modul `Research` + `Gemini Pro` dari mesin rumah terlebih dahulu.
   - Petakan port mana yang sudah ada dan mana yang perlu diselaraskan dengan kontrak `ResearchPackage` & `AC-03` di dokumen ini.
2. **Fase R2 — Collector Deterministik & Cache:**
   - Tambah `YouTubeDataApiTrendCollector` (gratis via `YouTube:ApiKey`) + `GoogleTrendsRssCollector` + dukungan `OperatorSeedUrls`.
   - Tambah cache TTL (`6–24` jam) supaya riset cepat & hemat kuota.
3. **Fase R3 — `AC-03` Persistence & UI Research Workbench:**
   - Tambah migrasi `research_packages` / `research_sources` / `evidence_claims`.
   - Tambah UI layar/panel **Research & Trend Finder**: input niche/region + seed TikTok/FYP → tampilkan kartu tren dengan angka riil & URL → tombol **"Jadikan Ide Proyek"**.
4. **Fase R4 — Eksperimen `CliTextGenerator` & Storyboard Override:**
   - Implementasikan `CliAiOptions` + `CliTextGenerator` (`gemini -p --output-format json`, `Enabled = false`, cooldown `429`, concurrency `1`) + unit tests via `FakeProcessRunner`.
   - Tambah `provider` opsional di `GenerateStoryboardJobPayload` agar **Idea → Script → Storyboard** bisa diuji terpisah per job lewat UI.
