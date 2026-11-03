# P1 readiness audit

Status: audit selesai, **P1 belum selesai**. Dokumen ini bukti, bukan rencana implementasi.
Tanggal: 2026-11-03 (office Mac, code-only). Branch: `docs/p1-readiness` dari `main` @ `d2d887b`.
Lingkup: hanya audit; tidak ada fitur, endpoint, skema, migrasi, paket, atau tes produk yang diubah.

## 1. Ringkasan

1. **P1 belum selesai**: hanya 2 dari 12 kriteria traceability berstatus Done (AC-02, AC-12), 6 Partial, 3 Missing (AC-08 export + video ID, AC-09 metrics/waktu/biaya, AC-10 portability), dan 1 Unverified (AC-11).
2. Backend pipeline inti (idea → script → review → storyboard → asset → narration/subtitle → render → QA) nyata, teruji, dan bisa dibangun ulang: 1246 tes, 1245 lulus, 0 gagal, 1 skip.
3. Yang paling menentukan kegagalan P1 bukan bug, melainkan **tidak adanya lapisan penyelesaian**: A3 final approval, publication record/video ID, metrics, dan export/import tidak ada sama sekali; `src/AIStudio.Web` masih halaman placeholder tanpa satu pun panggilan API.
4. Dua klaim "selesai" di dokumen tidak sesuai kode: DEV-01 menyebut project "create/read/update" padahal hanya `POST /api/content-projects` yang ada, dan DEV-04 menyebut cancel "verified" padahal `Job.Cancel` tidak punya pemanggil, endpoint, atau tes.
5. I2V masih scaffold: port + adapter + capability terdaftar di DI, tetapi tidak ada job type, handler, route, atau pemakaian di renderer; belum menjadi penghalang P1 karena memang deferred.

## 2. Metode dan bukti yang dipakai

Dijalankan dari `ai-studio/` pada 2026-11-03, mesin tanpa GPU/Ollama/ComfyUI/Blender.

| Perintah | Hasil persis |
| --- | --- |
| `dotnet --version` | `10.0.401` |
| `ffmpeg -version` | `ffmpeg version 9.0.1` |
| `docker ps` | `aistudio-dev-postgres-1  127.0.0.1:5544->5432/tcp` (PostgreSQL 18.6) |
| `dotnet build AIStudio.slnx -c Release` | **Build succeeded. 0 Warning(s), 0 Error(s)** (5.00 s) |
| `dotnet test AIStudio.slnx -c Release` dengan `ConnectionStrings__DefaultConnection` (port 5544) | **total 1246, succeeded 1245, failed 0, skipped 1** (7s 740ms) |
| Skip yang tersisa | `AudioGenerationEndToEndTests.RealProviders_SynthesizeNarrationAndMusic_ThenMix` — butuh `AISTUDIO_VOXCPM_*` / `AISTUDIO_ACESTEP_*` |
| Jumlah file tes (`find tests -name "*Tests.cs"`) | `157` |
| `ls src/AIStudio.Api/Endpoints/` | `11` file endpoint |
| Route produk yang terpetakan | **21** (tabel di bawah) |
| Route infrastruktur di `Program.cs` | **4** (`/`, `/health`, `/health/live`, `/health/ready`) |
| Migrasi EF | **5** (lihat tabel) |
| Tabel bisnis di DB | **6** (`content_projects`, `jobs`, `reviewed_scripts`, `scene_assets`, `narration_tracks`, `subtitle_tracks`) |
| `__EFMigrationsHistory` | 5 baris, semua migrasi teraplikasi |
| Isi DB saat audit | `jobs` 0 baris, `content_projects` 0, `scene_assets` 0 (suite membersihkan dirinya) |

Verifikasi DB dijalankan lewat `docker exec aistudio-dev-postgres-1 psql -U postgres -d aistudio`.

### 2.1 Inventaris endpoint (21 route produk)

| # | Method + route | File |
| --- | --- | --- |
| 1 | `POST /api/content-projects` | `src/AIStudio.Api/Endpoints/GenerateIdeaEndpoints.cs:15` |
| 2 | `POST /api/content-projects/{id}/generate-idea-jobs` | `GenerateIdeaEndpoints.cs:20` |
| 3 | `POST /api/content-projects/{id}/generate-script-jobs` | `GenerateIdeaEndpoints.cs:28` |
| 4 | `GET /api/jobs/{jobId}` | `GenerateIdeaEndpoints.cs:36` |
| 5 | `POST /api/content-projects/{id}/script-review` | `src/AIStudio.Api/Endpoints/ScriptReviewEndpoints.cs:15` |
| 6 | `GET /api/content-projects/{id}/script-review` | `ScriptReviewEndpoints.cs:23` |
| 7 | `PUT /api/content-projects/{id}/script-review` | `ScriptReviewEndpoints.cs:29` |
| 8 | `POST /api/content-projects/{id}/script-review/approve` | `ScriptReviewEndpoints.cs:36` |
| 9 | `POST /api/content-projects/{id}/storyboard-jobs` | `src/AIStudio.Api/Endpoints/StoryboardEndpoints.cs:13` |
| 10 | `POST /api/content-projects/{id}/storyboard-jobs/{jobId}/assets` | `src/AIStudio.Api/Endpoints/AssetEndpoints.cs:14` |
| 11 | `GET /api/content-projects/{id}/assets` | `AssetEndpoints.cs:23` |
| 12 | `POST /api/content-projects/{id}/storyboard-jobs/{jobId}/narration` | `src/AIStudio.Api/Endpoints/NarrationEndpoints.cs:14` |
| 13 | `GET /api/content-projects/{id}/narration` | `NarrationEndpoints.cs:23` |
| 14 | `POST /api/content-projects/{id}/storyboard-jobs/{jobId}/subtitle` | `src/AIStudio.Api/Endpoints/SubtitleEndpoints.cs:14` |
| 15 | `GET /api/content-projects/{id}/subtitle` | `SubtitleEndpoints.cs:23` |
| 16 | `POST /api/content-projects/{id}/storyboard-jobs/{storyboardJobId}/render-jobs` | `src/AIStudio.Api/Endpoints/RenderEndpoints.cs:14` |
| 17 | `POST /api/content-projects/{id}/render-jobs/{renderJobId}/qa-jobs` | `src/AIStudio.Api/Endpoints/FinalVideoQaEndpoints.cs:13` |
| 18 | `POST /api/content-projects/{id}/storyboard-jobs/{storyboardJobId}/visual-jobs` | `src/AIStudio.Api/Endpoints/VisualEndpoints.cs:17` |
| 19 | `POST /api/content-projects/{id}/storyboard-jobs/{storyboardJobId}/audio-jobs` | `src/AIStudio.Api/Endpoints/AudioEndpoints.cs:14` |
| 20 | `POST /api/identity-assets` | `src/AIStudio.Api/Endpoints/IdentityAssetEndpoints.cs:29` |
| 21 | `POST /api/identity-assets/{assetId}/versions/{version}/approve` | `IdentityAssetEndpoints.cs:36` |

Yang **tidak** ada di permukaan HTTP: baca/daftar/ubah project, cancel job, export, import, approval final (A3), publication record, metrics, preview/render artifact serving.

### 2.2 Migrasi dan tabel

| MigrationId | Tabel |
| --- | --- |
| `20260915160805_InitialCoreDomain` | `content_projects`, `jobs` |
| `20260917113100_AddReviewedScripts` | `reviewed_scripts` |
| `20260918041620_AddSceneAssets` | `scene_assets` |
| `20260918042508_AddNarrationTracks` | `narration_tracks` |
| `20260918081522_AddSubtitleTracks` | `subtitle_tracks` |

Tidak ada tabel untuk approval final, publikasi, metrik, sumber/evidence, atau export. Itu bukti struktural, bukan sekadar belum di-wire.

## 3. Matriks AC-01…AC-12

Catatan penting: tabel traceability di `docs/development/PLAN.md:34-47` memuat **dua belas** baris, `AC-01` sampai `AC-12`, dan `PLAN.md:49` menulis "all twelve criteria". Jadi tidak ada selisih di PLAN; matrix ini mengikuti 12 baris tersebut (lihat bagian 7).

Status: **Done** = ada kode + tes yang membuktikan; **Partial** = sebagian jalur ada, sebagian tidak; **Missing** = tidak ada sama sekali; **Unverified** = tidak bisa dibuktikan di mesin ini.

| ID | Kriteria singkat | Status | Bukti konkret | Celah |
| --- | --- | --- | --- | --- |
| AC-01 | Persistent browser project | **Partial** | `POST /api/content-projects` (`GenerateIdeaEndpoints.cs:15`) → `ContentProject.Create` (`src/AIStudio.Domain/Content/ContentProject.cs:39`) → tabel `content_projects`; round-trip DB `PostgreSqlPersistenceTests.ContentProjectAndJob_RoundTripThroughPostgreSql`; kontrak `GenerateIdeaApiContractsTests` | Tidak ada `GET`/`PUT`/list project; `TransitionTo` (`ContentProject.cs:60`) tidak dipanggil kode produksi mana pun (hanya tes); tidak ada browser (lihat §4.1) → "persistent **browser** project" tidak bisa didemonstrasikan |
| AC-02 | Local text AI | **Done (kode) / Unverified (model nyata)** | Satu-satunya `IAiTextGenerator` adalah `OllamaTextGenerator` (`src/AIStudio.Infrastructure/AI/OllamaTextGenerator.cs:16`), base URL default `http://127.0.0.1:11434`; `Ai:Provider` divalidasi "must be 'Ollama'" (`DependencyInjection.cs:157-160`); error `ai_timeout`/`ai_malformed_response` (`GenerateIdeaJobHandler.cs:72-77`); tes `GenerateIdeaJobHandlerTests`, `OllamaTextGeneratorTests` | Tidak ada Ollama/GPU di mesin ini → generasi nyata tidak dijalankan; retry nyata hanya tercatat di riwayat, bukan diuji ulang di sini. Tidak ada fallback cloud (sesuai aturan, jadi bukan celah) |
| AC-03 | Sources/script/A1/A2 | **Partial** | `GenerateScript` job + `ReviewedScript` (`src/AIStudio.Domain/Scripts/ReviewedScript.cs:104` approve, `:89` approved immutable), workflow `ScriptReviewWorkflow`, 4 route review; tes `ScriptReviewWorkflowTests.Start_CopiesCompletedGenerateScriptResultAndIsIdempotent`, `Approve_IsIdempotentAndApprovedHumanVersionIsCanonical`, `ReviewedScriptTests.Edit_IncrementsRevisionOnlyWhenCanonicalContentChanges` | **Sources/evidence/claims tidak ada** (tidak ada entitas, tabel, atau endpoint; hanya `ContentProject.Brief` teks bebas); A1 ada, A2 hanya untuk script — storyboard dan final video tidak punya approval A2 eksplisit; tidak ada UI |
| AC-04 | Media/rights/subtitles | **Partial** | `SceneAsset` mewajibkan `Source`+`License` untuk origin External (`src/AIStudio.Domain/Assets/SceneAsset.cs:136-154`); path safety + symlink (`LocalAssetFileStoreTests.Register_RejectsSymbolicLinkEscapingRoot`); narasi & subtitle bergerbang storyboard; `SceneAssetVerticalSliceTests.Register_PersistsAndReloadsSceneAssetMetadata`, `SubtitleTrackModelTests`; 3 migrasi aset | Rights hanya "string wajib ada", tidak ada status eligibility/resolusi; tidak ada gate QA publikasi yang memblokir rights belum selesai; subtitle hanya register+read, **tidak bisa edit**; tidak ada UI |
| AC-05 | Validated render | **Partial (kode) / Unverified (hasil nyata)** | `RenderVideoJobHandler` + `FfmpegVideoRenderer` + `FinalVideoQa`; tes `RenderVideoJobHandlerTests.Handler_RendersWhenInputsMatchRecordedHashes`, `Handler_RejectsChangedAssetHash`, `FinalVideoQaJobHandlerTests.Handler_PassesForValidArtifactWithSubtitle`, `Handler_RejectsMissingExpectedSubtitleStream`; hasil render & QA diekspos `GET /api/jobs/{jobId}` (`GenerateIdeaWorkflow.cs:107`, `FinalVideoQaContractsTests.JobResponse_ExposesCompletedFinalVideoQaResult`) | Artefak Video #1 **tidak ada** di checkout ini (`src/AIStudio.Api/assets/renders` tidak ada; aset gitignored) dan tidak bisa dirender ulang tanpa model; tidak ada preview/export (DEV-05 mensyaratkan "preview/export works") |
| AC-06 | Job recovery | **Partial** | `PostgreSqlJobQueueTests.ExpiredLease_IsRecoveredAndConsumesRetry`, `ExpiredLease_WithNoRetriesRemaining_BecomesFailed`, `ConcurrentClaims_ReturnTheJobToExactlyOneWorker`, `Failure_RetriesOnceThenEndsFailed`; klaim `FOR UPDATE SKIP LOCKED` (D-014) | **Cancel tidak terverifikasi**: `Job.Cancel` (`src/AIStudio.Domain/Jobs/Job.cs:165`) tidak dipanggil siapa pun, `IJobQueue` tidak punya method cancel, tidak ada endpoint, tidak ada tes (hanya `JobStatus.Cancelled` di pemetaan status API). DEV-04 menuntut "restart/lease recovery and cancel verified" |
| AC-07 | QA + current A3 | **Partial** | `FinalVideoQaJobHandler` deterministik (`qa_artifact_missing`, `qa_hash_mismatch`, `qa_probe_failed`, `qa_duration_invalid`, `qa_video_stream_missing`, `qa_audio_stream_missing`, `qa_subtitle_stream_missing`); tes `FinalVideoQaJobHandlerTests` (11 kasus); enqueue `POST .../qa-jobs` | **A3 tidak ada**: tidak ada entitas/tabel/endpoint approval final; PUBLISHING/§43 "approval binds to artifact version + destination" tidak diimplementasikan; "stale A3 blocks readiness" tidak mungkin karena readiness/publish tidak ada; `ContentStatus.ReadyToPublish/Published` hanya enum tanpa endpoint transisi |
| AC-08 | Export and real video ID | **Missing** | Grep `videoId`/`VideoId` di `src/` = 0 hasil; tidak ada tipe/route export; tidak ada tabel publication; hanya 5 migrasi (§2.2) | Kedua bagian hilang: export package dan publication record + video ID. PRD §43 menuntut approval mengikat artifact+destination. |
| AC-09 | Time/cost/metrics | **Missing** | Grep `metrics|revenue|human time|cost` di `src/` = 0 hasil produk; tidak ada tabel metrik; yang ada hanya timestamp job (`CreatedAt`/`StartedAt`/`CompletedAt`) | Semua yang diminta `parts/06` §31 hilang: null-vs-zero, cumulative snapshots, actual vs estimated cash, fees/refunds, human hours, elapsed/rework time |
| AC-10 | Project portability | **Missing** | Tidak ada route export/import, tidak ada workflow, tidak ada runbook, tidak ada tes fresh-location restore | DEV-07 utuh: export/import project, restore DB + file, licensed omission, offline cached-input |
| AC-11 | Offline cached-input production | **Unverified** | Semua provider lokal (Ollama `127.0.0.1`, ComfyUI/Blender/VoxCPM/ACE-Step lokal, FFmpeg lokal) dan tidak ada panggilan cloud di kode | Tidak ada tes/runbook yang menjalankan render dengan jaringan dimatikan dan input cache; tidak bisa dijalankan di sini (tanpa model/GPU) |
| AC-12 | No required new payments | **Done (statis) / Unverified (biaya nyata)** | Tidak ada SDK/payment gateway, tidak ada endpoint eksternal selain provider lokal; `Ai:Provider` divalidasi hanya `Ollama`; tidak ada API key di konfigurasi | Biaya model nyata tidak bisa diukur di sini; sifat "tidak butuh pembayaran baru" hanya bisa dibuktikan struktural |

Rekap 12 kriteria: **Done 2** (AC-02, AC-12 — keduanya dengan catatan Unverified runtime), **Partial 6** (AC-01, AC-03, AC-04, AC-05, AC-06, AC-07), **Missing 3** (AC-08, AC-09, AC-10), **Unverified 1** (AC-11).

## 4. Celah lintas-kriteria

### 4.1 UI-01 (paling mahal)

- `src/AIStudio.Web/src/App.tsx` hanya 14 baris: eyebrow "P1 Foundation", judul, satu paragraf. Tidak ada router, tidak ada state, tidak ada panggilan `fetch`/API, tidak ada form.
- `src/AIStudio.Web/src/main.tsx` hanya mount React; `package.json` hanya `react`/`react-dom` (tanpa router, tanpa query client, tanpa test).
- Tidak ada tes UI sama sekali (157 file tes semuanya backend).
- Akibat: AC-01/03/04/05/07/09 yang mensyaratkan `+ UI-01` tidak bisa lulus; "persistent browser project" tidak ada di browser. `docs/design/FIGMA-HANDOFF.md` menyatakan desain belum diterima, jadi kecocokan visual juga tidak bisa dinilai (Unverified, bukan salah kode).

### 4.2 A3 final approval

- Hanya ada approval script (`ReviewedScript.Approve`) dan approval identity asset. Tidak ada approval untuk final video/publikasi.
- PRD §43/§44 A3 mensyaratkan approval mengikat artifact version + destination dan menjadi invalid bila artifact berubah. Tidak ada entitas, tabel, endpoint, atau tes untuk itu.
- `ContentProject.Status` punya `ReadyToPublish`/`Published`, tetapi tidak ada operasi yang mengubahnya di produksi; `TransitionTo` tidak dipanggil dari Application/API.

### 4.3 Publishing record dan video ID

- Tidak ada `videoId`/`VideoId` di `src/`; tidak ada URL/destination, tanggal publikasi, atau metadata publikasi.
- Akibat langsung: AC-08 tidak bisa dinilai "partial" — memang belum ada. Ini juga memblokir pilot P0-05 (manual publication/logs) secara perangkat lunak, meski pilot boleh manual.

### 4.4 Metrics / waktu / biaya

- Tidak ada model metrik sama sekali. Tidak ada null-vs-zero (PRD §31/ARCHITECTURE "Unknown metrics are null with reason, not zero"), tidak ada snapshot kumulatif, actual/estimated, fees/refunds, human hours, repair time.
- Satu-satunya data waktu adalah timestamp job; itu machine time, bukan human time, dan tidak diekspos sebagai metrik.

### 4.5 Export/import

- Tidak ada export package, tidak ada import project, tidak ada verifikasi restore di lokasi/DB baru, tidak ada daftar omission berlisensi.
- Ini juga berarti tidak ada jalur backup yang diuji, padahal PRD §45 menuntut recovery mungkin.

### 4.6 Auth posture

- Tidak ada `AddAuthentication`/`AddAuthorization`/`UseAuthentication`/`RequireAuthorization` di seluruh `src/` (grep = 0).
- Postur saat ini murni loopback + host filter: `AllowedHosts: "localhost;127.0.0.1;[::1]"` (`src/AIStudio.Api/appsettings.json:115`), `applicationUrl: http://127.0.0.1:5002` (`Properties/launchSettings.json`), PostgreSQL dipublikasikan hanya ke `127.0.0.1:5544` (`compose.yaml`).
- 21 endpoint, termasuk upload multipart 8 MiB (`POST /api/identity-assets`), tidak terautentikasi. Selama benar-benar loopback ini sesuai keputusan internal, tetapi tidak ada mekanisme yang mencegah exposure (tidak ada API key, tidak ada CSRF, `DisableAntiforgery` pada upload). Ini risiko yang harus tetap tercatat, bukan diperbaiki diam-diam pada audit.

### 4.7 Asset Registry

- Yang ada: `SceneAsset` per scene (metadata + hash + provenance) dan `IdentityAssetRegistry` (khusus identity image; metadata/versi/approval persisten lewat `LocalIdentityAssetMetadataPersistence` sejak Identity Asset Persistence v1, bukan in-memory).
- Belum ada katalog aset proyek: pencarian, dedup, lifecycle (replace/delete), status eligibility/rights, thumbnail, atau registry lintas scene. DEV-03 "metadata/hashes/eligibility" baru sebagian (metadata+hash ada, eligibility tidak).
- `IdentityAssetRegistry` mulai kosong sampai registrasi eksplisit; tidak ada endpoint list/get untuk identity asset (hanya import dan approve).

### 4.8 I2V: scaffold vs wired

- **Scaffold (ada)**: `IVideoGenerationProvider` (`src/AIStudio.Application/Rendering/Visuals/IVideoGenerationProvider.cs:11`), `ComfyUiVideoGenerationProvider` terdaftar DI (`src/AIStudio.Infrastructure/DependencyInjection.cs:358`), capability `visual.character_animation` + provider `comfyui-i2v` (`ProductionCapabilityCatalog.cs:97-104`), `VideoClipTiming.Fit` (pure, belum dipakai), validator workflow, tes provider.
- **Tidak wired**: tidak ada `JobType` video, tidak ada handler yang memanggil `IVideoGenerationProvider` (grep di `src/AIStudio.Application` = 0 pemakaian selain interface), tidak ada route, `SceneAsset.Type=Video` tidak dihasilkan dari I2V, renderer tidak mengonsumsi clip I2V. `I2V_PLAN.md` P3.3–P3.6 masih `[ ]`.
- Tidak ada konflik fakta dengan `I2V_PLAN.md`; dokumen itu tetap akurat. Yang basi adalah `STATE.md:254` ("no provider exists in this checkout").

## 5. Urutan pekerjaan yang disarankan

Urutan menurut rasio "membuka kriteria" vs biaya. S ≤ 1 hari, M 2–4 hari, L > 1 minggu (perkiraan kasar, bukan komitmen).

| # | Pekerjaan | Effort | Alasan singkat |
| --- | --- | --- | --- |
| 1 | A3: entitas approval final (artifact hash + destination), tabel, endpoint approve/revoke, invalidasi saat artifact berubah; gate `ReadyToPublish` | **M** | Membuka AC-07 dan menjadi prasyarat AC-08; tanpa ini "readiness" tidak punya arti |
| 2 | Publication record + `videoId` + destination + waktu; endpoint catat/lihat | **S–M** | Melengkapi AC-08; kecil karena menumpang approval A3 |
| 3 | `GET`/`PUT`/list content project + transisi status yang diekspos | **S** | Menutup separuh AC-01; murah dan langsung terpakai UI |
| 4 | Cancel job: method `IJobQueue`, endpoint, tes retry/lease | **S** | Menutup celah DEV-04/AC-06 yang sudah lama diklaim "verified" |
| 5 | Metrics/time/cost model + endpoint (null-vs-zero, actual/estimated, human hours) | **M** | AC-09 hilang total; skema harus benar sejak awal agar tidak migrasi ulang |
| 6 | Export package + import project + uji restore di DB/lokasi baru | **M–L** | Menutup AC-08 (export) dan AC-10 sekaligus; paling berisiko karena menyentuh file+DB |
| 7 | UI-01 bertahap mengikuti Figma: projects → script review → assets/audio → production/QA → publish/metrics | **L** | Satu-satunya cara AC berlabel `+ UI-01` lulus; tertahan handoff desain |
| 8 | Sources/evidence/claims (AC-03) | **M** | Riset/fakta belum punya tempat; bisa setelah A3/metrics |
| 9 | Asset Registry (katalog, rights/eligibility, lifecycle) | **M** | Memperkuat AC-04 dan audit trail; tidak memblokir jika A3 memvalidasi rights |
| 10 | Offline runbook + tes cached-input tanpa jaringan | **S** | Menutup AC-11 dengan biaya kecil |
| 11 | I2V P3.3–P3.6 (opsional, bukan P1) | **L** | Memang deferred; jangan dikerjakan sebelum A3/metrics/export selesai |

Alasan urutan: 1–4 adalah pekerjaan kecil-menengah yang langsung mengubah status kriteria; 5–6 menyentuh skema dan karena itu harus direncanakan sebelum UI; 7 adalah biaya terbesar dan bergantung pada Figma; 8–11 menambah kualitas, bukan kelulusan P1.

## 6. Tidak terverifikasi di mesin ini

Semua item di bawah **tidak** dinilai gagal; statusnya Unverified karena lingkungan.

1. Generasi teks Qwen/Ollama nyata (tidak ada Ollama, tanpa GPU) → AC-02/AC-03.
2. VoxCPM2, ACE-Step, dan mixer nyata (model tidak terpasang; 1 tes E2E audio skip) → AC-05.
3. Render ComfyUI FLUX, Blender, Manim nyata (tanpa GPU/model) → AC-05.
4. I2V nyata dan benchmark (P1.6/P2 masih `TODO(verify-on-home)`) → AC-05, I2V.
5. Artefak Video #1 dan demo render (tidak ada di checkout; `assets/` gitignored) → AC-05/AC-07.
6. Kecocokan layar dengan Figma (frame belum diterima) → UI-01.
7. Database produksi/status publikasi nyata (tidak ada; DB lokal kosong) → AC-08.
8. Biaya, revenue, dan human hours nyata (belum ada video terbit) → AC-09.
9. Restore di lokasi/DB baru (butuh host kedua; belum ada alatnya) → AC-10.
10. Render dengan jaringan dimatikan (belum ada tes/runbook) → AC-11.
11. Perilaku HTTP runtime (API tidak dijalankan saat audit untuk menghindari efek samping; postur auth dinilai dari kode/konfigurasi) → §4.6.

## 7. Temuan inkonsistensi dokumen

Tidak ada yang diperbaiki diam-diam; semuanya dicatat di sini.

1. **"Twelve criteria" vs asumsi sebelas.** Brief audit menyebut tabel "tampak AC-01…AC-11". Faktanya `PLAN.md:34-47` memuat `AC-01`…`AC-12` dan `PLAN.md:49` menulis "all twelve criteria". Jadi PLAN konsisten; yang perlu dikoreksi adalah asumsi brief. Matrix di §3 memakai 12 baris. Tidak ada perubahan pada PLAN.
2. **`ARCHITECTURE.md:51` basi.** "Atomic claiming, lease reconciliation, execution, output reuse, and cancellation remain the next milestone" — claiming/lease/eksekusi sudah ada sejak D-014 dan diuji (`PostgreSqlJobQueueTests`). Yang benar-benar belum hanyalah cancellation.
3. **`ARCHITECTURE.md:48` basi.** "ApplicationDbContext exposes ContentProject, Job, and ReviewedScript" — sekarang juga `SceneAsset`, `NarrationTrack`, `SubtitleTrack` (3 migrasi setelahnya).
4. **`STATE.md:254` basi.** "character animation … (no provider exists in this checkout)" — `ComfyUiVideoGenerationProvider` dan port-nya ada (D-046, P3.1/P3.2).
5. **`CHECKPOINT.md:53` basi dan tidak ditandai historis.** Menyebut hasil `FinalVideoQa` "persisted but not yet deserialized by `GET /api/jobs/{jobId}`", padahal `CHECKPOINT.md:5` dan `FinalVideoQaContractsTests.JobResponse_ExposesCompletedFinalVideoQaResult` membuktikan sudah diekspos.
6. **`PLAN.md:21` (DEV-01) vs kode.** Evidence of completion menyebut "project create/read/update"; hanya create yang ada.
7. **`PLAN.md:24` (DEV-04) vs kode.** "restart/lease recovery and cancel verified" — cancel tidak terverifikasi sama sekali.
8. **`PLAN.md:3`** menyebut "working breakdown of PRD v0.7", sedangkan baseline aktif adalah PRD v0.9.2 (`docs/product/PRD_INDEX.md`).
9. **Dua indeks PRD hidup berdampingan.** `README.md:3,15` dan `PROJECT.md:3` merujuk PRD v0.7 + `docs/product/INDEX.md`, sementara `AGENTS.md` dan `docs/product/PRD_INDEX.md` menyatakan v0.9.2 otoritatif. README juga sudah menyatakan P1 belum selesai (benar), tetapi rujukannya ke indeks lama menyesatkan agen baru.
10. **`PLAN.md:35-47` tidak memetakan `AC-12` ke slice mana pun** ("All slices"), sehingga kelulusan AC-12 bergantung pada slice yang belum ada; ini bukan salah ketik, tetapi membuat traceability AC-12 tidak bisa diuji.

## 8. Kesimpulan audit

Jawaban atas "development AI Studio sudah selesai atau belum?": **belum**. Backend produksi konten sudah jauh melampaui fondasi dan lolos 1245 tes, tetapi P1 didefinisikan sebagai 12 kriteria untuk satu proyek nyata; empat di antaranya (export, video ID, metrics, portability) tidak punya kode sama sekali, A3 tidak ada, dan antarmuka browser belum dibangun. Pekerjaan berikutnya yang paling efisien adalah A3 + publication record + project read/update + cancel (semua kecil), lalu metrics dan export/import sebelum UI-01.
