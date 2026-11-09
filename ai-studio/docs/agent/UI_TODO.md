# UI TODO — handoff WSL

Status: 2026-11-09. Fondasi UI + layar Projects sudah diimplementasikan dan
di-merge ke `main` (branch `feat/ui-projects` berisi tiga commit UI untuk
traceability). Backend, kontrak API, dependency, dan file terlarang tidak
berubah. Dokumen ini memuat workflow kerja, quick start WSL, sisa layar UI, dan
checklist verifikasi.

Konteks desain/toolchain: `docs/design/UI_IMPLEMENTATION.md`.
Status kriteria P1: `docs/development/P1_READINESS.md`.

## 1. Yang sudah ada (jangan dibangun ulang)

- `AppShell`: sidebar Projects/Runs/Settings (Runs dinonaktifkan), breadcrumb,
  language switcher, theme picker, skip link.
- Token light/dark (`prefers-color-scheme` + `data-theme`), theme Auto/Light/Dark
  persisten, i18n `id` (default) + `en` persisten, `<html lang>` sinkron.
- `api/client.ts` (base URL env/default `http://127.0.0.1:5080`, timeout 15s,
  normalisasi problem+json) dan `api/projects.ts` (list + create).
- Projects: skeleton, inline error + retry, empty state, tabel status/tanggal,
  create dialog (validasi title/brief), discard confirmation, success toast.
- Settings: shell preferensi (tema, bahasa, alamat API).
- Hash router minimal: `#/projects` (default), `#/projects/{id}`, dan `#/settings`.
- Project detail (slice 2, 2026-11-09): buka proyek dari tabel, header (judul/status/`updatedAt`/Ubah), kartu Brief (lihat/ubah lewat `PUT`), kartu Kesiapan (readiness + alasan), ringkasan job terakhir, dan daftar bagian berikutnya; skeleton, inline error + retry, dialog edit tervalidasi.

## 2. Workflow kerja (dipakai untuk slice ini)

Ikuti urutan ini agar perubahan UI tetap kecil, terverifikasi, dan mudah
dilanjutkan dari mesin lain.

1. **Mulai dari `main` bersih.** `git status`, `git switch main`,
   `git pull --ff-only`, lalu buat branch `feat/<scope>`. Jangan mulai dari
   worktree kotor atau dari `main` langsung.
2. **Baca konteks dulu, jangan scan seluruh repo.** Baca
   `docs/agent/CHECKPOINT.md`, `docs/context/STATE.md`,
   `docs/agent/REPO_MAP.md`, lalu hanya kontrak endpoint/file yang relevan.
3. **UI-only.** Jangan mengubah backend, kontrak API, domain, schema, job,
   provider, `Ffmpeg*`, `RenderingOptions`, `SceneMediaInput`, I2V, tracking,
   atau transfer. Jangan menambah dependency tanpa keputusan eksplisit.
4. **Implementasi bertahap.** Fondasi (token/i18n/theme/api/components/layout) →
   layar → wiring routing. Semua copy lewat `t(...)`; error code hanya baris
   sekunder; loading/empty/error/validation/success harus ada.
5. **Verifikasi sebelum commit.**
   - `npx tsc --noEmit` (harus exit 0, tanpa output).
   - `npx vite build` (harus sukses; catat ukuran bundle + gzip).
   - Grep teks UI hardcoded di `src/**/*.tsx`.
   - Smoke headless/manual bila memungkinkan; jangan klaim yang belum dijalankan.
6. **Commit terpisah dan scoped.** Contoh slice ini: fondasi → layar Projects →
   docs. Jangan commit `node_modules`, `dist`, `.npm-cache`, artefak lokal, atau
   perubahan backend yang tidak diminta.
7. **Merge dan push setelah izin.** Merge ke `main` (merge commit), push branch
   + `main`, lalu pastikan `git status` bersih dan remote sudah memuatnya.
8. **Perbarui docs di commit yang sama/akhir.** `STATE.md` (bukti persis),
   `CHECKPOINT.md` (resume), `REPO_MAP.md` (lokasi), `P1_READINESS.md` (status
   AC), dan dokumen ini bila sisa pekerjaan berubah.
9. **Handoff ke mesin lain.** Pull `main`, `npm ci`, ulangi verifikasi langkah 5,
   catat hasil di STATE/CHECKPOINT. Jangan menyalin `node_modules` antar mesin.

Aturan tetap: satu branch per scope, tidak ada force push, tidak menulis ulang
history `main`, dan UI tidak memaksa backend berubah hanya demi kenyamanan.

## 3. Quick start di WSL

```bash
cd ~/path/ke/aiengine            # root repository
git fetch origin
git switch main
git pull --ff-only

cd ai-studio/src/AIStudio.Web
node --version                   # gunakan Node sistem; jangan Node bawaan DSH
npm ci                           # fresh clone WSL; lockfile sudah ada
npx tsc --noEmit
npx vite build
npm run dev                      # Vite dev server
```

- **Node.** Di Mac office terverifikasi Node 20.17.0; Vite 8.3.0 hanya memberi
  warning bahwa ia minta 20.19+/22.12+ dan build tetap sukses. `package.json`
  masih mendeklarasikan `engines.node >=24.17.0 <25` (tidak diubah). Di WSL
  gunakan Node sistem 22.12+ atau 24.x agar cocok dengan Vite/engine.
- **Native binding.** Jangan menyalin `@rolldown/binding-darwin-arm64` dari Mac
  dan jangan menambahkannya ke `package.json`. `npm ci` di WSL akan memasang
  binding Linux yang sesuai dari optional dependency di lockfile. Di Mac binding
  darwin dipasang dengan `npm i --no-save`.
- **Cache.** `.npm-cache/` sudah masuk `.gitignore` root; jangan commit
  `node_modules`, `dist`, atau cache.
- **Backend.** Di terminal lain, dari `ai-studio/`:
  `dotnet run --project src/AIStudio.Api`. `launchSettings.json` memakai
  `http://127.0.0.1:5002`, sedangkan UI default `http://127.0.0.1:5080`.

### Keputusan base URL (2026-11-09)

Strategi yang dipakai: **same-origin lewat Vite dev proxy**. Browser memanggil
origin Vite (`127.0.0.1:5173`), dan `vite.config.ts` mem-proxy `/api` apa adanya
ke `http://127.0.0.1:5002`. Tidak ada perubahan backend dan tidak perlu CORS.
`api/client.ts` memakai base kosong (same-origin) secara default;
`VITE_API_BASE_URL` tetap bisa menimpa untuk build yang disajikan dari origin
lain (mis. `dist/` di belakang reverse proxy).

Bukti 2026-11-09: Vite dev + stub API di `5002` -> `GET /api/content-projects/{id}`
dan `GET .../readiness` lewat `127.0.0.1:5173` mengembalikan HTTP 200 dengan
payload utuh (prefix `/api` tidak dipotong).

## 4. Workflow produk yang harus ditampung UI

Urutan ini adalah alur pengguna yang menjadi acuan layar berikutnya; jangan
membuat langkah baru yang tidak didukung API.

```text
Projects (list/create)
  -> Project detail
       Brief/Research
       -> Reviewed script (A2)
       -> Storyboard -> Assets/Audio -> Production
       -> QA (FinalVideoQa) -> A3 final approval
            approved            -> Publish -> Metrics/time/cost
            changes_requested   -> kembali ke Production
       -> Export/import
Runs (status/cancel job)
Settings (data path, model lokal, batas job, template, backup/import)
```

Aturan UX: status selalu terlihat, aksi yang diblokir menjelaskan alasannya,
error manusiawi + kode sekunder, dan aksi irreversible memakai konfirmasi.

## 5. TODO UI berikutnya (urutan yang disarankan)

### 5.1 Buka proyek + route detail (selesai 2026-11-09)

- [x] `useHashRoute` kini bertipe `projects | project | settings` dengan
      `#/projects/{id}`; `#/projects` tetap default dan fallback.
- [x] Aksi "Buka" pada baris tabel (link asli + navigasi, label i18n, fokus
      terlihat); tabel tidak melakukan fetch detail per baris.
- [x] Detail memakai state `loading | ready | error` + retry dengan
      `InlineError`/`Skeleton`.
- [x] Kembali ke daftar tanpa kehilangan bahasa/tema (hash route).

### 5.2 Project detail (layar terbesar; header + Brief + readiness selesai 2026-11-09)

Endpoint yang sudah tersedia (lihat `docs/agent/REPO_MAP.md`): detail
`GET /api/content-projects/{id}`, update title/brief, readiness + transition,
script review, storyboard/visual/audio/render/QA jobs, final approval + decision,
publication, metrics/time/cost, export/import.

- [x] Header detail: judul, status badge, `updatedAt`, aksi utama kanan atas.
- [ ] Section/tab: Brief/Research (Brief selesai), Reviewed script, Assets/Audio,
      Production, Review/QA + A3 (approved / changes_requested + note), Publish,
      Metrics. `GET .../readiness` sudah tampil beserta alasan blokir.
- [ ] Semua aksi destruktif/irreversible memakai `Dialog` konfirmasi; jangan
      menonaktifkan tombol tanpa alasan yang terlihat.
- [ ] Pertahankan copy manusiawi; kode error hanya baris sekunder.
- [ ] Job queued/running/failed/cancelled: status, alasan, retry/cancel sesuai
      kontrak yang ada.

### 5.3 Runs

- [ ] **Dependency backend:** belum ada endpoint daftar job. Yang ada hanya
      `GET /api/jobs/{jobId}` dan `POST /api/jobs/{jobId}/cancel`. Jangan
      mengarang endpoint list; putuskan apakah Runs memakai lookup by id atau
      menunggu endpoint list yang disetujui.
- [ ] Setelah kontraknya ada: tabel job, status, waktu, error code sekunder,
      cancel dengan konfirmasi, retry bila diizinkan backend.

### 5.4 Settings produksi + import/export

- [ ] Ganti shell preferensi menjadi halaman Settings lengkap: lokasi data,
      konfigurasi model lokal, batas job, template, export/backup, import.
- [ ] Export/import memakai endpoint yang ada (`.../exports`,
      `POST /api/projects/imports`) dengan upload multipart dan konfirmasi.
- [ ] Jangan menampilkan path/credential yang tidak aman.

### 5.5 Cross-cutting

- [ ] Putuskan strategi tes UI. Saat ini tidak ada dependency tes dan semua tes
      backend; jangan menambah paket tanpa keputusan.
- [ ] Pass aksesibilitas: keyboard, `aria-live`, fokus dialog, screen reader,
      kontras dark/light.
- [ ] Responsive ~1024/768/375; tabel horizontal scroll.
- [ ] Opsional: tanggal relatif, kolom tambahan hanya jika API menyediakannya.
- [ ] Figma belum diterima; jangan klaim kecocokan visual final.

## 6. Verifikasi yang wajib dicatat di WSL

Jalankan dari `ai-studio/src/AIStudio.Web` lalu catat hasil persis:

```text
npx tsc --noEmit      -> exit code
npx vite build        -> exit code + ukuran dist/index.html, CSS, JS (+ gzip)
npm run dev           -> URL dev server
```

Untuk smoke backend, catat juga URL API, status CORS, dan hasil
list/empty/error/create. Setelah selesai, perbarui:

- `docs/context/STATE.md` — entri `PASS (..., <tanggal>) - <pekerjaan>: <perintah> -> <hasil>`.
- `docs/agent/CHECKPOINT.md` — resume point + sisa layar.
- `docs/agent/REPO_MAP.md` — jika struktur/lokasi berubah.
- `docs/development/P1_READINESS.md` — jika status AC berubah.

## 7. Jangan

- Jangan mengubah backend/kontrak API atau menambah dependency untuk kenyamanan
  UI tanpa keputusan eksplisit.
- Jangan menyentuh `Ffmpeg*`, `RenderingOptions`, `SceneMediaInput`, I2V,
  tracking, atau transfer.
- Jangan commit `node_modules`, `dist`, `.npm-cache`.
- Jangan memakai Node bawaan DSH; gunakan Node sistem.
