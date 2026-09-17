# Figma handoff

Status: desain sedang dikerjakan pengguna. Link file/frame, design tokens, dan aset belum diterima. Dokumen ini menjadi referensi saat desain tersedia; pekerjaan backend dapat dilanjutkan secara independen.

## Area sesuai PRD §12

| Area | Isi dan interaksi yang perlu ditampung |
| --- | --- |
| Projects | Daftar proyek, status, tindakan berikutnya, buat/buka proyek, ringkasan waktu |
| Project detail | Brief/Research; Script; Assets/Audio; Production/Review; Publish/Metrics |
| Settings | Lokasi data, konfigurasi model lokal, batas job, template, export/backup |

Ini kebutuhan fungsi, bukan layout final. Editor script berupa teks terstruktur; scene memakai form dan pengurutan sederhana. Editor timeline profesional berada di luar P1.

## State yang perlu terlihat

- Empty, loading, validation error, success, dan runtime lokal tidak tersedia.
- Job queued/running/failed/cancelled, alasan kegagalan, retry/cancel, lokasi/preview hasil.
- A1/A2/A3: belum disetujui, disetujui, dan perlu review ulang setelah perubahan.
- QA Pass/Warning/Fail beserta alasan dan tindakan; aksi terblokir punya penjelasan.
- Metrik unavailable/delayed berbeda dari nilai nol; estimated berbeda dari actual.
- Operasi import/export dan penyimpanan gagal tidak membuang input pengguna.

## Bahan handoff saat siap

1. Link file dan frame utama dengan versi/tanggal yang jelas.
2. Warna, font beserta sumber/hak penggunaan, spacing, komponen dan variannya.
3. Alur utama: buat proyek → produksi/review → export dan catat publikasi.
4. State penting dan aturan adaptasi ukuran layar; tandai bagian yang belum final.
5. Aset ikon/gambar yang boleh dipakai dan diikutkan dalam repo.

## Catatan integrasi

- Ambil detail frame yang sedang diimplementasikan, lalu simpan ringkasan mapping frame → komponen/route jika sudah ada. Hindari dump seluruh file Figma ke konteks.
- Backend menjaga aturan status, approval, dan QA meskipun tombol sudah dinonaktifkan di UI.
- DTO/API mengikuti kebutuhan interaksi dan domain; nama layer desain bukan schema database.
- Saat detail visual belum tersedia, kontrak, domain, storage, job, dan pemeriksaan backend tetap bisa dikerjakan. Visual final mengikuti handoff pengguna.
