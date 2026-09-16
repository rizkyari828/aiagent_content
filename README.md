# Local AI Content Studio

Studio produksi konten lokal untuk satu operator dan satu channel YouTube, berdasarkan PRD v0.7.

**Status:** P1 Foundation, P1.1 Local Development Runtime, dan milestone Core Domain + Persistent Job Model selesai. P1/MVP secara keseluruhan belum selesai; workflow konten dan UI produk masih menunggu milestone berikutnya serta handoff Figma.

## Mulai dari sini

- [Checkpoint agent](docs/agent/CHECKPOINT.md)
- [Peta arsitektur](docs/agent/ARCHITECTURE_MAP.md)
- [Ringkasan proyek](docs/context/PROJECT.md)
- [Status dan verifikasi](docs/context/STATE.md)
- [Backlog](docs/development/PLAN.md)
- [Arsitektur](docs/development/ARCHITECTURE.md)
- [PRD dan indeks](docs/product/INDEX.md)

## Environment development

Topology default:

```text
Browser Windows
  -> AIStudio.Web + AIStudio.Api di WSL2
      -> PostgreSQL di Docker Desktop melalui WSL integration
```

Source dan semua perintah `dotnet`/`npm` dijalankan di WSL. Baseline persisten:

- .NET SDK 10.0.401 di `~/.dotnet`, dipin oleh `global.json`
- Node.js 24.17.0 LTS melalui nvm, dipin oleh `.nvmrc`
- npm 11.13.0
- Docker CLI dan Docker Compose v2 melalui Docker Desktop WSL integration

Shell baru memuat toolchain dari `~/.profile`/`~/.bashrc`. Di repository, gunakan `nvm use` bila versi aktif belum mengikuti `.nvmrc`. Periksa dengan:

```bash
dotnet --version
dotnet --info
nvm use
node --version
npm --version
docker --version
docker compose version
```

## PostgreSQL lokal

Dari root repository di WSL:

```bash
cp --no-clobber .env.example .env
# Ganti POSTGRES_PASSWORD di .env dengan nilai development lokal.
docker compose config
docker compose up --detach --wait postgres
```

`.env` diabaikan Git. Compose menjalankan PostgreSQL 18.6 saja, memublikasikan port dari `POSTGRES_PORT` hanya pada loopback, dan menyimpan cluster pada named volume `aistudio-postgres-data`.

Inspeksi runtime:

```bash
docker compose ps
docker compose logs postgres
docker compose exec postgres postgres --version
```

Menghentikan dan menyalakan kembali service tanpa menghapus data:

```bash
docker compose stop postgres
docker compose up --detach --wait postgres
```

`docker compose down` menghapus container dan network; named volume tetap ada. Jangan gunakan `--volumes` jika data development perlu dipertahankan. Volume PostgreSQL tidak boleh dipasang langsung ke image major lain; gunakan backup/restore atau `pg_upgrade` bila database sudah berisi data bermakna.

## Backend

Muat environment yang sama lalu jalankan API:

```bash
set -a
source .env
set +a

dotnet tool restore
dotnet restore AIStudio.slnx
dotnet build AIStudio.slnx --configuration Release
dotnet test AIStudio.slnx --configuration Release
dotnet run --project src/AIStudio.Api/AIStudio.Api.csproj
```

`ConnectionStrings__DefaultConnection` dibentuk dari `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, dan `POSTGRES_PORT` di `.env`. Jangan commit nilai nyata.
nPersistent worker default-nya nonaktif. Untuk validasi lokal terkontrol, gunakan environment JobWorker__Enabled=true; polling dan lease dapat diubah melalui JobWorker__PollInterval dan JobWorker__LeaseDuration. Placeholder handler belum menjalankan workload bisnis.

Endpoint:

- `GET /health/live`: HTTP 200 ketika proses API hidup.
- `GET /health/ready`: HTTP 200 ketika PostgreSQL dapat dihubungi; HTTP 503 ketika tidak tersedia.
- `GET /health`: seluruh pemeriksaan.

## Frontend

```bash
cd src/AIStudio.Web
nvm use
npm ci
npm run build
npm run dev
```

Vite mengikat ke `127.0.0.1:5173` dan mem-proxy `/api` ke API lokal `127.0.0.1:5002`.

## EF Core dan migrasi

Migration pertama berisi tabel `content_projects`, `jobs`, relasi, constraint retry, JSONB, dan indeks polling. Terapkan migration ke database lokal:

```bash
set -a
source .env
set +a

dotnet tool restore
dotnet ef database update \
  --project src/AIStudio.Infrastructure/AIStudio.Infrastructure.csproj \
  --startup-project src/AIStudio.Api/AIStudio.Api.csproj
```

Untuk migration domain berikutnya:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/AIStudio.Infrastructure/AIStudio.Infrastructure.csproj \
  --startup-project src/AIStudio.Api/AIStudio.Api.csproj \
  --output-dir Persistence/Migrations
```
