# Restore & Offline Production Runbook (`AC-10` & `AC-11`)

Status: 2026-11-13 (drafted on office MacBook; operational execution & verification run on Home WSL2 / target host).

## 1. `AC-10` — Cross-Machine Project Restore Runbook

Tujuan: membuktikan satu `ContentProject` dapat diekspor dari mesin/database asal dan dipulihkan utuh ke mesin atau database PostgreSQL yang benar-benar bersih tanpa kehilangan relasi, hash file, atau status A3 (`schemaVersion=2`).

### Prasyarat
- Proyek asal tidak memiliki job berstatus `Queued` atau `Running` (jika ada, ekspor ditolak dengan `export_project_busy`).
- Mesin tujuan memiliki .NET 10 SDK, PostgreSQL 18.6 kosong, dan direktori `Assets:RootPath` yang bersih.

### Langkah Operator
1. **Export di mesin asal:**
   - Via UI (`SettingsScreen` → Project Transfer atau tab Publish di `ProjectDetailScreen`), atau via API:
     `POST /api/content-projects/{id}/exports`
   - Catat `packageRelativePath`, `sha256`, dan `byteSize` paket `.zip`.
2. **Transfer paket ke mesin tujuan:**
   - Salin file `.zip` hasil ekspor ke mesin tujuan dan verifikasi checksum `sha256sum <file>.zip` cocok dengan langkah 1.
3. **Siapkan database & storage bersih di mesin tujuan:**
   - Jalankan `docker compose up -d` di `ai-studio/` dan pastikan migrasi EF terpasang (`dotnet ef database update`).
   - Pastikan proyek `{id}` belum ada di tabel `content_projects`.
4. **Import paket di mesin tujuan:**
   - Via UI (`SettingsScreen` → Import ZIP) atau via API:
     `POST /api/projects/imports` (multipart form field `package`).
5. **Verifikasi pasca-restore:**
   - `GET /api/content-projects/{id}` mengembalikan 200 OK dengan `id`, `title`, `brief`, dan `status` identik.
   - `GET /api/content-projects/{id}/readiness` dan `GET /api/content-projects/{id}/final-approvals` mempertahankan `decision` (`approved`/`changes_requested`), `decisionNote`, `decidedAt`, serta `revision`.
   - `GET /api/content-projects/{id}/assets`, `.../narration`, `.../subtitle`, `.../publications`, dan `.../tracking-summary` cocok dengan data asal; hash file di `Assets:RootPath` identik.
   - Catat hasil eksekusi aktual ke `docs/context/STATE.md` dan perbarui status `AC-10` di `P1_READINESS.md`.

---

## 2. `AC-11` — Offline Cached-Input Production Runbook

Tujuan: membuktikan render video penuh dapat diselesaikan dengan jaringan dimatikan menggunakan input & model lokal yang sudah tersedia (`Rp0` cloud dependency).

### Prasyarat
- Semua provider cloud dalam keadaan nonaktif (`Gemini:Enabled=false`, `GeminiImage:Enabled=false`, `GeminiVideo:Enabled=false`, `GeminiSpeech:Enabled=false`, `DeepSeek:Enabled=false`, `OpenAi:Enabled=false`).
- Runtime lokal terpasang dan ter-cache di mesin eksekusi: Ollama (`qwen3.8:27b-q4_K_M`), FFmpeg/FFprobe, serta aset input (`ReviewedScript` Approved, `GenerateStoryboard`, `SceneAsset` / `GenerateSceneVisuals`, `NarrationTrack` / `GenerateAudio`, `SubtitleTrack`).

### Langkah Operator
1. **Verifikasi konfigurasi offline-safe:**
   - Cek `GET /api/capabilities`: pastikan seluruh provider `cloud_api` berstatus `enabled: false` dan provider lokal (`ollama`, `svg-still`, dll.) aktif sesuai kebutuhan.
2. **Putuskan koneksi jaringan eksternal:**
   - Matikan Wi-Fi / adapter jaringan eksternal (sisakan loopback `127.0.0.1` untuk API, PostgreSQL, Ollama, dan ComfyUI lokal).
3. **Jalankan produksi dari input lokal / cache:**
   - Jalankan `POST /api/content-projects/{id}/storyboard-jobs/{storyboardJobId}/render-jobs` (dan `qa-jobs` setelah render selesai).
4. **Verifikasi hasil:**
   - `RenderVideo` berstatus `Succeeded` dengan `outputPath` MP4 valid di `Assets:RootPath`.
   - `FinalVideoQa` berstatus `Succeeded` (video stream, audio stream, durasi > 0, subtitle terverifikasi).
   - Catat perintah, timestamp, dan hash artefak ke `docs/context/STATE.md` dan `P1_READINESS.md`.
