# Local AI Content Studio

Studio produksi konten lokal untuk satu operator dan satu channel YouTube, berdasarkan PRD v0.7.

**Status:** dokumentasi awal tersedia; aplikasi belum diimplementasikan. Pengguna sedang mengerjakan desain Figma secara paralel.

## Mulai dari sini

- [Panduan agent](AGENTS.md)
- [Ringkasan proyek dan peta bacaan](docs/context/PROJECT.md)
- [Status pekerjaan dan langkah berikutnya](docs/context/STATE.md)
- [Backlog implementasi](docs/development/PLAN.md)
- [Arsitektur](docs/development/ARCHITECTURE.md)
- [Handoff Figma](docs/design/FIGMA-HANDOFF.md)
- [PRD dan indeks bagian](docs/product/INDEX.md)

## Konteks tersimpan

`AGENTS.md` mengarahkan agent untuk membaca dua ringkasan kecil, kemudian membuka spesifikasi sesuai tugas. Pendekatan ini mengurangi pembacaan ulang dokumen panjang. File-file tersebut tetap memakai token ketika dibaca; tidak ada jaminan penghematan persentase tertentu atau cache token penyedia yang dikonfigurasi.

Panduan root menggunakan mekanisme [AGENTS.md dalam dokumentasi resmi OpenAI](https://learn.chatgpt.com/docs/agent-configuration/agents-md). Dokumen di `docs/` dibaca sesuai arahan dan kebutuhan tugas, bukan diasumsikan semuanya otomatis dimuat.

## Setup aplikasi

Perintah install, run, dan test akan ditambahkan bersama scaffold yang sudah diverifikasi. Target stack tercatat di dokumen arsitektur; tidak ada dependency aplikasi atau model yang diunduh pada tahap dokumentasi ini.
