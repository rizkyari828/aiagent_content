# P1 readiness audit

Status: audit selesai, **P1 belum selesai**. Dokumen ini bukti, bukan rencana implementasi. Status per kriteria diperbarui 2026-11-03 setelah A3 + publication, lifecycle + cancel, metrics/waktu/biaya (AC-09 backend), project export/import (AC-10 backend), dan koreksi status AC-08: **3 Done (AC-02, AC-06, AC-12), 8 Partial (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08, AC-09, AC-10), 0 Missing, 1 Unverified (AC-11)**; isi audit awal tidak ditulis ulang, hanya status dan celah yang diperbarui.
Tanggal: 2026-11-03 (office Mac, code-only). Branch: `docs/p1-readiness` dari `main` @ `d2d887b`.
Update 2026-11-09: UI foundation + Projects screen sudah diimplementasikan dan di-merge ke `main`; lihat §4.1 dan `docs/agent/UI_TODO.md`. Status backend/AC tidak berubah (3 Done, 8 Partial, 0 Missing, 1 Unverified).
Update 2026-11-13: UI Slice 3 (`ProjectDetailScreen` 5 tab interaktif) & Slice 4 (`RunsScreen`, `SettingsScreen` capability selector + ZIP export/import), `D-052`/`D-053` (opsional Gemini/DeepSeek/OpenAI cloud adapters, disabled by default, local-first priority 100 tetap default), serta batch Mac `code-only` (`Routing*` media providers, I2V `P3.3`–`P3.6` wiring + Ken Burns fallback, editable subtitles `AC-04`, per-publication rollup `AC-09`, UI `node --test` + a11y/responsive, dan `RESTORE_AND_OFFLINE_RUNBOOK.md` untuk `AC-10`/`AC-11`) sudah diimplementasikan. Celah operasional yang tersisa untuk kelulusan P1 penuh: publikasi nyata + real video ID (`AC-08`), verifikasi restore lintas mesin pada DB baru (`AC-10`), uji eksekusi offline tanpa jaringan (`AC-11`), revenue/income (`AC-09`), entity `sources/evidence/claims` (`AC-03`), status rights/eligibility aset (`AC-04`), dan review visual Figma (`UI-01`).
Lingkup: hanya audit; tidak ada fitur, endpoint, skema, migrasi, paket, atau tes produk yang diubah.

## 1. Ringkasan

1. **P1 belum selesai**: audit awal menemukan 2 dari 12 kriteria traceability Done (AC-02, AC-12), 6 Partial, 3 Missing (AC-08 export + video ID, AC-09 metrics/waktu/biaya, AC-10 portability), dan 1 Unverified (AC-11). Setelah A3 + publication record v1 (2026-11-03) statusnya menjadi **2 Done, 7 Partial, 2 Missing, 1 Unverified**: AC-08 berpindah Missing -> Partial. Setelah Content Project Lifecycle + Job Cancel v1 (2026-11-03) statusnya menjadi **3 Done (AC-02, AC-06, AC-12), 6 Partial (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08), 2 Missing (AC-09, AC-10), 1 Unverified (AC-11)**: AC-06 Done karena cancel kini diimplementasikan dan diuji; AC-01 tetap Partial karena backend lengkap tetapi browser/UI masih belum ada; AC-07 tetap Partial karena UI approval dan export belum ada. Setelah metrics + waktu + biaya v1 (2026-11-03) statusnya menjadi **3 Done (AC-02, AC-06, AC-12), 7 Partial (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08, AC-09), 1 Missing (AC-10), 1 Unverified (AC-11)**: AC-09 Partial karena backend manual lengkap tetapi UI, export, revenue/income, dan rollup per-publikasi belum ada. Setelah project export/import v1 (2026-11-03) statusnya menjadi **4 Done (AC-02, AC-06, AC-08, AC-12), 7 Partial (AC-01, AC-03, AC-04, AC-05, AC-07, AC-09, AC-10), 0 Missing, 1 Unverified (AC-11)**: AC-08 Done di backend (export + publication/video ID) dan AC-10 Partial karena alat restore ada serta round-trip DB lokal lulus, tetapi restore di mesin/DB benar-benar baru + runbook belum diuji. **Koreksi status AC-08 (2026-11-03)**: statusnya menjadi **3 Done (AC-02, AC-06, AC-12), 8 Partial (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08, AC-09, AC-10), 0 Missing, 1 Unverified (AC-11)** karena backend export + publication record/videoId selesai dan teruji, tetapi publikasi nyata (video ID asli), UI publikasi/export, dan restore lintas mesin belum ada.
2. Backend pipeline inti (idea → script → review → storyboard → asset → narration/subtitle → render → QA) nyata, teruji, dan bisa dibangun ulang: 1246 tes, 1245 lulus, 0 gagal, 1 skip.
3. Yang paling menentukan kegagalan P1 bukan bug, melainkan **tidak adanya lapisan penyelesaian**: saat audit, A3 final approval, publication record/video ID, metrics, dan export/import tidak ada sama sekali; `src/AIStudio.Web` masih halaman placeholder tanpa satu pun panggilan API. Sejak 2026-11-03 backend A3 + publication record, metrics/waktu/biaya, dan export/import ada; UI tetap belum.
4. Dua klaim "selesai" di dokumen tidak sesuai kode saat audit: DEV-01 menyebut project "create/read/update" padahal hanya `POST /api/content-projects` yang ada, dan DEV-04 menyebut cancel "verified" padahal `Job.Cancel` tidak punya pemanggil, endpoint, atau tes. **Keduanya sudah ditutup pada 2026-11-03** oleh Content Project Lifecycle + Job Cancel v1 (6 route + tes; lihat §2 dan §3).
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

**Status update 2026-11-03 (A3 + publication record v1).** Angka audit di atas adalah baseline saat audit. Setelah slice A3 + publication: `dotnet build AIStudio.slnx -c Release` 0 warning / 0 error; `dotnet test AIStudio.slnx -c Release` dengan `ConnectionStrings__DefaultConnection` -> **1301 total, 1300 pass, 0 fail, 1 skip**; 164 file `*Tests.cs`; 13 file endpoint; **25 route produk**; **6 migrasi**; **8 tabel bisnis** (`final_approvals`, `publication_records` baru); `__EFMigrationsHistory` 6 baris. Tidak ada route, tabel, atau paket lama yang diubah.

**Status update 2026-11-03 (project export/import v1).** `dotnet build AIStudio.slnx -c Release` 0 warning / 0 error; `dotnet test AIStudio.slnx -c Release` dengan `ConnectionStrings__DefaultConnection` -> **1498 total, 1497 pass, 0 fail, 1 skip**; 181 file `*Tests.cs`; 18 file endpoint (17 kelas + helper `ApiStatus.cs`); **41 route produk**; 7 migrasi; 11 tabel bisnis (tidak ada migrasi/tabel baru); `__EFMigrationsHistory` 7 baris. Export/import menambah 3 route (`POST|GET .../exports`, `POST /api/projects/imports`), 2 kelas workflow aplikasi, 1 store filesystem/ZIP, 1 repository EF, dan 57 tes transfer + 2 tes DB-gated. Smoke HTTP nyata terbatas pada `127.0.0.1:5002` memverifikasi 201 metadata export, 404 `export_project_not_found`, 400 `import_package_invalid`, 409 `import_project_conflict`, dan 201 import ke project row bersih; baris dan file smoke dihapus. Tidak ada route, tabel, kontrak A3/publication/lifecycle/tracking, renderer, atau provider lama yang diubah.

**Status update 2026-11-03 (project lifecycle + job cancel v1).** `dotnet build AIStudio.slnx -c Release` 0 warning / 0 error; `dotnet test AIStudio.slnx -c Release` dengan `ConnectionStrings__DefaultConnection` -> **1377 total, 1376 pass, 0 fail, 1 skip**; 169 file `*Tests.cs`; 15 kelas endpoint (+1 helper `ApiStatus.cs`); **31 route produk**; **6 migrasi**; **8 tabel bisnis** (tidak ada migrasi/tabel baru — readiness dihitung dan update project tidak butuh kolom); `__EFMigrationsHistory` tetap 6 baris. Smoke HTTP nyata terbatas dijalankan pada `127.0.0.1:5199` dan baris smoke dihapus setelahnya. Tidak ada route, tabel, kontrak A3/publication, atau paket lama yang diubah.

### 2.1 Inventaris endpoint (21 route produk saat audit; 25 setelah A3 + publication; 31 setelah lifecycle + cancel; 41 setelah export/import)

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
| 22 | `POST /api/content-projects/{id}/final-approvals` | `src/AIStudio.Api/Endpoints/FinalApprovalEndpoints.cs:18` |
| 23 | `GET /api/content-projects/{id}/final-approvals` | `FinalApprovalEndpoints.cs:26` |
| 24 | `POST /api/content-projects/{id}/publications` | `src/AIStudio.Api/Endpoints/PublicationEndpoints.cs:17` |
| 25 | `GET /api/content-projects/{id}/publications` | `PublicationEndpoints.cs:25` |
| 26 | `GET /api/content-projects` | `src/AIStudio.Api/Endpoints/ContentProjectEndpoints.cs:20` |
| 27 | `GET /api/content-projects/{id}` | `ContentProjectEndpoints.cs:25` |
| 28 | `PUT /api/content-projects/{id}` | `ContentProjectEndpoints.cs:31` |
| 29 | `GET /api/content-projects/{id}/readiness` | `ContentProjectEndpoints.cs:37` |
| 30 | `POST /api/content-projects/{id}/transitions` | `ContentProjectEndpoints.cs:43` |
| 31 | `POST /api/jobs/{jobId}/cancel` | `src/AIStudio.Api/Endpoints/JobCancelEndpoints.cs:14` |

Yang **tidak** ada di permukaan HTTP: export, import, metrics, preview/render artifact serving. Baca/daftar/ubah project, readiness/transisi, cancel job, A3 final approval, dan publication record kini ada (route 22-31).

### 2.2 Migrasi dan tabel (5 migrasi saat audit; 6 setelah A3 + publication; tetap 6 setelah lifecycle + cancel)

| MigrationId | Tabel |
| --- | --- |
| `20260915160805_InitialCoreDomain` | `content_projects`, `jobs` |
| `20260917113100_AddReviewedScripts` | `reviewed_scripts` |
| `20260918041620_AddSceneAssets` | `scene_assets` |
| `20260918042508_AddNarrationTracks` | `narration_tracks` |
| `20260918081522_AddSubtitleTracks` | `subtitle_tracks` |
| `20261103041001_AddFinalApprovalsAndPublications` | `final_approvals`, `publication_records` |

Tidak ada tabel untuk metrik, sumber/evidence, atau export. Itu bukti struktural, bukan sekadar belum di-wire. Lifecycle + cancel sengaja tidak menambah migrasi: readiness dihitung dari `jobs` + `final_approvals` + `publication_records`, dan update project hanya menyentuh kolom `title`/`brief`/`updated_at` yang sudah ada.

## 3. Matriks AC-01…AC-12

Catatan penting: tabel traceability di `docs/development/PLAN.md:34-47` memuat **dua belas** baris, `AC-01` sampai `AC-12`, dan `PLAN.md:49` menulis "all twelve criteria". Jadi tidak ada selisih di PLAN; matrix ini mengikuti 12 baris tersebut (lihat bagian 7).

Status: **Done** = ada kode + tes yang membuktikan; **Partial** = sebagian jalur ada, sebagian tidak; **Missing** = tidak ada sama sekali; **Unverified** = tidak bisa dibuktikan di mesin ini.

| ID | Kriteria singkat | Status | Bukti konkret | Celah |
| --- | --- | --- | --- | --- |
| AC-01 | Persistent browser project | **Partial (backend DONE 2026-11-03)** | `POST /api/content-projects` (`GenerateIdeaEndpoints.cs:15`) → `ContentProject.Create` → tabel `content_projects`; kini juga `GET /api/content-projects` (deterministik, `?take=` 1..100 default 20), `GET /api/content-projects/{id}` (detail + ringkasan job terakhir tanpa payload/result), `PUT /api/content-projects/{id}` (title/brief saja; unknown/immutable field ditolak 400 `content_project_invalid_request`; status tidak bisa diubah); `ContentProject.UpdateDetails`; tes `ContentProjectWorkflowTests`, `ContentProjectApiContractTests`, `ContentProjectLifecycleVerticalSliceTests`; round-trip DB lama tetap ada | Browser/UI belum ada (lihat §4.1) → "persistent **browser** project" masih tidak bisa didemonstrasikan; `TransitionTo` tetap tidak dipakai (satu-satunya transisi produksi adalah `MarkReadyToPublish` yang dijaga readiness) |
| AC-02 | Local text AI | **Done (kode) / Unverified (model nyata)** | Satu-satunya `IAiTextGenerator` adalah `OllamaTextGenerator` (`src/AIStudio.Infrastructure/AI/OllamaTextGenerator.cs:16`), base URL default `http://127.0.0.1:11434`; `Ai:Provider` divalidasi "must be 'Ollama'" (`DependencyInjection.cs:157-160`); error `ai_timeout`/`ai_malformed_response` (`GenerateIdeaJobHandler.cs:72-77`); tes `GenerateIdeaJobHandlerTests`, `OllamaTextGeneratorTests` | Tidak ada Ollama/GPU di mesin ini → generasi nyata tidak dijalankan; retry nyata hanya tercatat di riwayat, bukan diuji ulang di sini. Tidak ada fallback cloud (sesuai aturan, jadi bukan celah) |
| AC-03 | Sources/script/A1/A2 | **Partial** | `GenerateScript` job + `ReviewedScript` (`src/AIStudio.Domain/Scripts/ReviewedScript.cs:104` approve, `:89` approved immutable), workflow `ScriptReviewWorkflow`, 4 route review; tes `ScriptReviewWorkflowTests.Start_CopiesCompletedGenerateScriptResultAndIsIdempotent`, `Approve_IsIdempotentAndApprovedHumanVersionIsCanonical`, `ReviewedScriptTests.Edit_IncrementsRevisionOnlyWhenCanonicalContentChanges` | **Sources/evidence/claims tidak ada** (tidak ada entitas, tabel, atau endpoint; hanya `ContentProject.Brief` teks bebas); A1 ada, A2 hanya untuk script — storyboard dan final video tidak punya approval A2 eksplisit; tidak ada UI |
| AC-04 | Media/rights/subtitles | **Partial** | `SceneAsset` mewajibkan `Source`+`License` untuk origin External (`src/AIStudio.Domain/Assets/SceneAsset.cs:136-154`); path safety + symlink (`LocalAssetFileStoreTests.Register_RejectsSymbolicLinkEscapingRoot`); narasi & subtitle bergerbang storyboard; `SceneAssetVerticalSliceTests.Register_PersistsAndReloadsSceneAssetMetadata`, `SubtitleTrackModelTests`; 3 migrasi aset | Rights hanya "string wajib ada", tidak ada status eligibility/resolusi; tidak ada gate QA publikasi yang memblokir rights belum selesai; subtitle hanya register+read, **tidak bisa edit**; tidak ada UI |
| AC-05 | Validated render | **Partial (kode) / Unverified (hasil nyata)** | `RenderVideoJobHandler` + `FfmpegVideoRenderer` + `FinalVideoQa`; tes `RenderVideoJobHandlerTests.Handler_RendersWhenInputsMatchRecordedHashes`, `Handler_RejectsChangedAssetHash`, `FinalVideoQaJobHandlerTests.Handler_PassesForValidArtifactWithSubtitle`, `Handler_RejectsMissingExpectedSubtitleStream`; hasil render & QA diekspos `GET /api/jobs/{jobId}` (`GenerateIdeaWorkflow.cs:107`, `FinalVideoQaContractsTests.JobResponse_ExposesCompletedFinalVideoQaResult`) | Artefak Video #1 **tidak ada** di checkout ini (`src/AIStudio.Api/assets/renders` tidak ada; aset gitignored) dan tidak bisa dirender ulang tanpa model; tidak ada preview/export (DEV-05 mensyaratkan "preview/export works") |
| AC-06 | Job recovery | **Done (kode)** | `PostgreSqlJobQueueTests.ExpiredLease_IsRecoveredAndConsumesRetry`, `ExpiredLease_WithNoRetriesRemaining_BecomesFailed`, `ConcurrentClaims_ReturnTheJobToExactlyOneWorker`, `Failure_RetriesOnceThenEndsFailed`; klaim `FOR UPDATE SKIP LOCKED` (D-014). Sejak 2026-11-03 cancel nyata: `POST /api/jobs/{jobId}/cancel` → `CancelJobWorkflow` → `IJobQueue.CancelAsync` (transaksi Read Committed + `SELECT ... FOR UPDATE` + `Job.Cancel`); Queued/Running → Cancelled, terminal → 409 `job_cancel_not_allowed`, tidak dikenal → 404 `job_not_found`; lease dihapus sehingga job tidak pernah diklaim lagi; `CompleteAsync`/`FailAsync`/`RenewLeaseAsync` bergerbang status+worker sehingga worker tidak bisa menimpa cancelled. Tes: `PostgreSqlJobQueueTests` (queued/running/terminal/unknown/already-cancelled), `JobWorkerTests.Processor_StopsExecutionWhenLeaseOwnershipIsLost`, `JobTests.Cancel_*`, `JobCancelApiContractTests` | Tidak ada UI cancel; cancel job Running bersifat kooperatif (efek samping in-flight berhenti di ownership check berikutnya, bukan atomik) |
| AC-07 | QA + current A3 | **Partial (backend A3 + review decision done)** | Deterministic FinalVideoQa tetap ada (`qa_artifact_missing`, `qa_hash_mismatch`, `qa_probe_failed`, `qa_duration_invalid`, `qa_video_stream_missing`, `qa_audio_stream_missing`, `qa_subtitle_stream_missing`; 11 tes). Sejak 2026-11-03: `FinalApproval` (satu row per project, artifact SHA-256/size + destination + revision), `FinalApprovalWorkflow` (render Succeeded, newest terminal FinalVideoQa pass, artifact re-read via `IAssetFileStore`, newer-render invalidation), `GET`/`POST .../final-approvals`, migration `20261103041001_AddFinalApprovalsAndPublications`; tes `FinalApprovalWorkflowTests`, `FinalApprovalApiContractTests`, `FinalApprovalVerticalSliceTests`. Sejak 2026-11-03 review decision dua arah sudah ada di backend: `FinalApproval.Decision` (`approved`|`changes_requested`), `DecisionNote` wajib untuk change request (maks 2000), `DecidedAt`, `POST .../final-approvals` menerima `decision`/`note` opsional (default `approved` agar klien lama tetap jalan; 400 `final_approval_decision_invalid`/`final_approval_note_required`/`final_approval_note_invalid`), `GET` mengembalikan `decision`/`decisionNote`/`decidedAt` + status `approved|changes_requested|invalid|missing` + `reasons`, readiness `blocked` dengan kode baru `readiness_final_approval_changes_requested`, dan publikasi menolak decision non-`approved` (`publication_approval_required`); migration aditif `20261103064621_AddFinalApprovalDecision`. Tes `ReviewDecisionDomainTests` + kasus workflow/API/persistensi baru. | Belum ada UI approval (termasuk tampilan decision/note/tombol request changes) dan export/report; review decision baru tersedia sebagai kontrak backend. Sejak 2026-11-03 ada readiness terhitung (`GET .../readiness`: `not_started`/`in_progress`/`qa_passed`/`ready_to_publish`/`blocked` + kode `readiness_render_missing`, `readiness_qa_failed`, `readiness_final_approval_missing`, `readiness_final_approval_invalid`, `readiness_publication_missing`) dan satu transisi eksplisit `POST .../transitions` (`ready_to_publish` saja, idempotent, 409 `content_project_not_ready` bila QA/A3 belum valid). Readiness memakai `FinalApprovalWorkflow` (termasuk `EvaluateQaAsync` read-only) sehingga aturan A3 tidak diduplikasi. Invalidasi tetap dihitung saat dibaca dan tidak memblokir render — memang manual. |
| AC-08 | Export and real video ID | **Partial (backend DONE 2026-11-03)** | Sejak 2026-11-03: `PublicationRecord` (platform, https URL, opaque `videoId`, `PublishedAt`, notes, approval id/revision/artifact hash), `PublicationWorkflow` (hanya bila A3 valid untuk destination yang sama; idempotent; tanpa upload), route `POST`/`GET .../publications`, tabel `publication_records`. Export satu project -> satu ZIP: `POST`/`GET .../exports`, `ProjectExportWorkflow` + `LocalProjectTransferStore` (manifest `schemaVersion=2` (paket v1 tetap diimpor) + `files/` dengan path relatif, SHA-256, ukuran), hash dihitung ulang dan dibandingkan dengan metadata, file hilang/path tidak aman/hash berubah/ukuran melebihi batas ditolak dengan kode stabil; tes `ProjectPackageReaderTests`, `ProjectExportWorkflowTests`, `ProjectTransferApiContractTests`, `ProjectTransferVerticalSliceTests`. | **Belum ada publikasi nyata**: tidak ada video yang benar-benar diterbitkan dan tidak ada video ID asli; record publication masih diisi manual, tanpa upload/API eksternal. UI export/publikasi belum ada. Restore lintas mesin/DB baru + runbook belum diuji dan dilacak di AC-10. |
| AC-09 | Time/cost/metrics | **Partial (backend DONE 2026-11-03)** | Sejak 2026-11-03: `MetricSnapshot` (satu row per `content_project` + `captured_at`, bind `publication_records` opsional saat capture), `TimeEntry` (`human`/`machine`), `CostEntry` (`actual`/`estimated`/`refunded` + currency wajib), workflow create/list + `GET .../tracking-summary`, 7 route (`POST|GET .../metrics`, `.../time-entries`, `.../cost-entries`, `GET .../tracking-summary`), migration `20261103043550_AddMetricTimeCostTracking` (3 tabel: `metric_snapshots`, `time_entries`, `cost_entries`); tes `MetricSnapshotTests`, `TimeEntryTests`, `CostEntryTests`, `TrackingWorkflowTests`, `TrackingApiContractTests`, `TrackingVerticalSliceTests`. Null-vs-zero dipertahankan (semua nilai metrik nullable; 0 = terukur, null = tidak diketahui), snapshot kumulatif **tidak pernah dijumlahkan** (summary hanya snapshot terbaru), CTR 0..1, nilai negatif ditolak, waktu UTC, source/category token lowercase, human dan machine dipisah tanpa total gabungan, biaya dijumlah per currency dengan actual/estimated/refunded terpisah (`netActual = actual - refunded`, ditandai `refundExceedsActual` bila refund melebihi actual). | **UI dan export belum ada**; input masih manual (tanpa YouTube Analytics/API eksternal); revenue/income (collected revenue, fees, kontribusi nilai per jam) dan rollup per-publikasi belum dimodelkan; elapsed/rework time belum punya kategori terpisah |
| AC-10 | Project portability | **Partial (backend DONE 2026-11-03)** | Sejak 2026-11-03: import `POST /api/projects/imports` (multipart, dibatasi ukuran) memvalidasi schemaVersion -> path entry aman (tanpa `..`, absolut, symlink, tabrakan case) -> struktur/referensi manifest -> ukuran + SHA-256 per file -> project id belum ada; menulis file ke approved root tanpa menimpa; insert project/reviewed script/jobs/scene assets/narration/subtitle/A3/publication/metrics/time/cost dalam satu transaksi; gagal DB -> rollback + hapus file baru. v1 tanpa remap/merge/overwrite; id project hasil = id manifest. Bukti: 70 tes transfer + 4 tes DB-gated (export -> hapus baris/file -> import -> semua baris & hash kembali; A3 `approved` dan `changes_requested` + note + `decidedAt` kembali sama; paket v1 tanpa decision tetap menjadi `approved`; konflik id -> rollback tanpa sisa file). | **Belum ada restore di mesin/DB benar-benar baru** (host kedua), runbook restore, UI, licensed omission, dan offline cached-input; import hanya menerima multipart (tidak memilih path di transfer root). |
| AC-11 | Offline cached-input production | **Unverified** | Semua provider lokal (Ollama `127.0.0.1`, ComfyUI/Blender/VoxCPM/ACE-Step lokal, FFmpeg lokal) dan tidak ada panggilan cloud di kode | Tidak ada tes/runbook yang menjalankan render dengan jaringan dimatikan dan input cache; tidak bisa dijalankan di sini (tanpa model/GPU) |
| AC-12 | No required new payments | **Done (statis) / Unverified (biaya nyata)** | Tidak ada SDK/payment gateway, tidak ada endpoint eksternal selain provider lokal; `Ai:Provider` divalidasi hanya `Ollama`; tidak ada API key di konfigurasi | Biaya model nyata tidak bisa diukur di sini; sifat "tidak butuh pembayaran baru" hanya bisa dibuktikan struktural |

Rekap setelah koreksi status AC-08 (2026-11-03): **Done 3** (AC-02, AC-06, AC-12), **Partial 8** (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08, AC-09, AC-10), **Missing 0**, **Unverified 1** (AC-11). Rekap 12 kriteria (setelah project export/import, 2026-11-03): **Done 4** (AC-02, AC-06, AC-08, AC-12), **Partial 7** (AC-01, AC-03, AC-04, AC-05, AC-07, AC-09, AC-10), **Missing 0**, **Unverified 1** (AC-11). Rekap setelah lifecycle + cancel, 2026-11-03: **Done 3** (AC-02, AC-06, AC-12 — AC-02/AC-12 tetap dengan catatan Unverified runtime), **Partial 6** (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08), **Missing 2** (AC-09, AC-10), **Unverified 1** (AC-11). Rekap setelah metrics + waktu + biaya (2026-11-03): **Done 3**, **Partial 7** (AC-01, AC-03, AC-04, AC-05, AC-07, AC-08, AC-09), **Missing 1** (AC-10), **Unverified 1** (AC-11). Rekap setelah A3 + publication: Done 2, Partial 7, Missing 2, Unverified 1. Rekap audit awal: Done 2, Partial 6, Missing 3, Unverified 1.

## 4. Celah lintas-kriteria

### 4.1 UI-01 (dimulai 2026-11-09)

- **Status 2026-11-09:** fondasi UI + layar Projects sudah diimplementasikan dan di-merge ke `main` (branch `feat/ui-projects`). `App.tsx` kini memakai hash router (`#/projects`, `#/settings`) dan `AppShell`; `ProjectsScreen` memanggil `GET`/`POST /api/content-projects` dengan skeleton, error + retry, empty state, tabel status/`updatedAt`, dialog create tervalidasi, dan toast. Tersedia pula `api/client.ts`, token light/dark, i18n id/en, theme Auto/Light/Dark, komponen dasar, dan Settings shell preferensi. Rincian: `docs/design/UI_IMPLEMENTATION.md`; workflow + sisa layar: `docs/agent/UI_TODO.md`.
- **Sisa:** Project detail (brief/script/assets/audio/production/QA/A3/publish/metrics), Runs (status/cancel; belum ada endpoint daftar job), production/import-export Settings, dan pass backend/CORS di browser. `docs/design/FIGMA-HANDOFF.md` masih menyatakan desain belum diterima, jadi kecocokan visual tetap Unverified.
- **Tes UI:** belum ada; semua tes yang ada tetap backend. Keputusan strategi tes UI (dan apakah menambah dependency) belum diambil.
- Akibat: AC-01/03/04/05/07/09 yang mensyaratkan `+ UI-01` belum bisa lulus sampai layar detail dan aksinya tersedia; "persistent browser project" sudah bisa didemonstrasikan di level list/create, bukan seluruh lifecycle.

### 4.2 A3 final approval (backend resolved 2026-11-03)

- Saat audit hanya ada approval script (`ReviewedScript.Approve`) dan approval identity asset; tidak ada approval final video/publikasi.
- Kini `FinalApproval` mengikat source RenderVideo job + artifact SHA-256/size + destination + revision, dan invalid bila artifact berubah, QA terbaru gagal, atau render baru dibuat. Read model mengembalikan approved/invalid/missing + alasan.
- Sisa celah: `ContentStatus.Published` masih tanpa operasi transisi (readiness hanya mengekspos `ready_to_publish`), dan belum ada UI approval. Sejak 2026-11-03 `ReadyToPublish` sudah punya satu transisi eksplisit yang dijaga readiness (§3 AC-07).

### 4.3 Publishing record dan video ID (backend resolved 2026-11-03)

- Saat audit tidak ada `videoId`/`VideoId`, URL/destination, tanggal publikasi, atau metadata publikasi di `src/`.
- Kini `PublicationRecord` menyimpan platform, https URL, opaque `videoId`, `PublishedAt`, notes, `CreatedAt`, dan binding approval; create hanya bila A3 valid dan destination cocok; tidak ada upload/API eksternal.
- Sisa celah: export package/import/restore belum ada; UI publikasi belum ada; record immutable satu per project.

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
| 1 | A3: entitas approval final (artifact hash + destination), tabel, endpoint approve/revoke, invalidasi saat artifact berubah; gate `ReadyToPublish` | **M** | **Backend DONE 2026-11-03** (tanpa revoke/readiness transition); membuka AC-07 dan menjadi prasyarat AC-08 |
| 2 | Publication record + `videoId` + destination + waktu; endpoint catat/lihat | **S–M** | **Backend DONE 2026-11-03** (manual, tanpa upload); melengkapi separuh AC-08 |
| 3 | `GET`/`PUT`/list content project + transisi status yang diekspos | **S** | **Backend DONE 2026-11-03** (readiness + satu transisi `ready_to_publish`); menutup separuh AC-01 dan siap dipakai UI |
| 4 | Cancel job: method `IJobQueue`, endpoint, tes retry/lease | **S** | **DONE 2026-11-03** (queued/running, terminal 409, unknown 404, worker tidak menimpa); menutup celah DEV-04/AC-06 |
| 5 | Metrics/time/cost model + endpoint (null-vs-zero, actual/estimated, human hours) | **M** | **Backend DONE 2026-11-03** (3 tabel, 7 route, summary memakai snapshot terbaru); sisa: UI, export, revenue/income, rollup per-publikasi |
| 6 | Export package + import project + uji restore di DB/lokasi baru | **M–L** | **Backend DONE 2026-11-03** (ZIP manifest+files, import ketat + rollback; round-trip DB lokal lulus); sisa: restore di mesin/DB baru, runbook, dan UI |
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
7. Database produksi/video yang benar-benar terbit (belum ada; DB lokal kosong saat audit). Publication record lokal kini bisa ditulis, tetapi publikasi nyata dan metriknya tidak bisa dibuktikan → AC-08/AC-09.
8. Biaya, revenue, dan human hours nyata (belum ada video terbit) → AC-09. Sejak 2026-11-03 human hours dan biaya **bisa dicatat manual** (dan diuji dengan data sintetis), tetapi angka nyata dari satu video terbit dan revenue/income belum ada / belum dimodelkan.
9. Restore di lokasi/DB baru (butuh host kedua; runbook belum ada). Sejak 2026-11-03 alat export/import tersedia dan round-trip di DB lokal lulus, tetapi restore lintas mesin/lokasi belum diuji → AC-10.
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

Jawaban atas "development AI Studio sudah selesai atau belum?": **belum**. Backend produksi konten sudah jauh melampaui fondasi dan lolos 1497 tes, tetapi P1 didefinisikan sebagai 12 kriteria untuk satu proyek nyata. Setelah A3 + publication record (2026-11-03), lifecycle + cancel (2026-11-03), metrics/waktu/biaya (2026-11-03), dan project export/import (2026-11-03), AC-06 Done, AC-07 punya backend lengkap dengan readiness gate, AC-08 Partial (backend export + publication record/video ID selesai dan teruji; publikasi nyata/video ID asli, UI, dan restore lintas mesin belum ada), dan AC-10 Partial (alat import/export + round-trip DB lokal lulus, restore lintas mesin belum diuji); AC-01 tetap tertahan UI. Antarmuka browser belum ada; metrics/waktu/biaya serta export masih backend tanpa UI. Pekerjaan berikutnya yang paling efisien adalah UI-01, lalu restore di mesin/DB baru + runbook, lalu revenue/rollup metrik.
