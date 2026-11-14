# Senior Test: Dokumentasi Project

Repositori ini berisi tiga soal yang dibangun dengan **.NET 11 (ASP.NET Core MVC / Web API)** dan **SQL Server**:

| # | Project | Ringkasan | URL lokal |
|---|---|---|---|
| 1 | [`Parkir`](Parkir/) | Hitung biaya parkir berdasarkan durasi, tarif per jenis kendaraan diatur dari database | http://localhost:5181 |
| 2 | [`RuangMeetingApi`](RuangMeetingApi/) + [`RuangMeetingWeb`](RuangMeetingWeb/) | Reservasi ruang meeting: master ruangan + reservasi, lewat REST API | API http://localhost:5133 · Web http://localhost:5151 |
| 3 | [`DeretBilangan`](DeretBilangan/) | Menampilkan deret `2 + 3 + 4 + 4 + 6 + 8 + …`, mencari posisi angka, dan menjumlahkan | http://localhost:5014 |

Tampilan ketiga aplikasi memakai gaya UI dari repositori *gunadarma-gulf-airplane-app*:
- font Cabin
- palet Material "Blue Grey" gelap (`#121A1F`, `#151E23`, `#263238`, `#455A64`)
- sidebar gelap dengan aksen garis kiri 3px
- kartu *well*, tombol *raised* gelap
- header tabel `#90A4AE` dan sorotan bergaya `<code>`

Tata letak form mengikuti mockup masing-masing soal.

---

## Daftar Isi

1. [Prasyarat & Menjalankan](#prasyarat--menjalankan)
2. [Project 1: Parkir](#project-1-parkir)
3. [Project 2: Reservasi Ruang Meeting](#project-2-reservasi-ruang-meeting)
4. [Project 3: Deret Bilangan](#project-3-deret-bilangan)
5. [Pengujian](#pengujian)
6. [Struktur Folder](#struktur-folder)
7. [Catatan & Keputusan Desain](#catatan--keputusan-desain)

---

## Prasyarat & Menjalankan

- .NET SDK 11
- SQL Server 2022. Hanya dibutuhkan oleh Parkir dan Ruang Meeting; Docker sudah cukup:

```bash
docker run -d --name sqlserver -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='user@12345' \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

Koneksi database ada di `appsettings.json` masing-masing project (`ConnectionStrings:Default`, user `sa`). **Database, tabel, dan data awal dibuat otomatis** saat aplikasi pertama kali dijalankan dari script `Database/*.sql`. Script-nya idempotent, jadi aman dijalankan berulang.

```bash
# Project 1
cd Parkir && dotnet run --launch-profile http

# Project 2: jalankan API dulu, lalu Web (dua terminal)
cd RuangMeetingApi && dotnet run --launch-profile http
cd RuangMeetingWeb && dotnet run --launch-profile http

# Project 3 (tidak butuh database)
cd DeretBilangan && dotnet run --launch-profile http
```

---

## Project 1: Parkir

Program menghitung biaya parkir dari selisih jam masuk dan jam keluar. Tarif bergantung pada jenis kendaraan dan bisa diubah sesuai kebijakan lewat database maupun form.

### Aturan biaya ([`Services/KalkulatorTarif.cs`](Parkir/Services/KalkulatorTarif.cs))

| Aturan | Default | Disimpan di |
|---|---|---|
| Durasi **< 10 menit** gratis | `MenitGratis = 10` | `TARIF` |
| Kelebihan menit **> 5** dihitung 1 jam penuh (02:04 = 2 jam, 02:06 = 3 jam) | `MenitToleransi = 5` | `TARIF` |
| **Tarif 1** berlaku flat untuk 2 jam pertama | `JamTarif1 = 2` | `TARIF` |
| **Tarif 2** untuk tiap jam berikutnya | | `TARIF` |

Tarif awal:

| Jenis Kendaraan | Tarif 1 (dua jam pertama) | Tarif 2 (jam berikutnya) |
|---|---|---|
| Mobil | 2000 | 500 |
| Motor | 1000 | 500 |

Contoh dari mockup: Mobil, 08:15 sampai 09:15, durasi `01:00`, bayar **2000**.

### Tabel ([`Database/ParkirDb.sql`](Parkir/Database/ParkirDb.sql))

| Tabel | Isi |
|---|---|
| `TARIF` | JenisKendaraan (unik), Tarif1, Tarif2, JamTarif1, MenitGratis, MenitToleransi, IsAktif, RowVer |
| `TRANSAKSI` | NoTransaksi (dari `SEQUENCE`, contoh `PRK-000000001`), NoPolisi, TarifId, JamMasuk, JamKeluar, DurasiMenit, JamDitagih, Bayar, **salinan tarif yang dipakai** (Tarif1/Tarif2/JamTarif1), IdempotencyKey, RowVer |

Salinan tarif di `TRANSAKSI` membuat transaksi lama tidak ikut berubah ketika tarif diubah.

### Halaman

- **Transaksi Parkir** (mockup soal): form, tabel, tombol Save, Edit, dan Delete.
  - Durasi dan Bayar dihitung otomatis oleh server lewat `/Parkir/Hitung`, jadi aturannya hanya ada di satu tempat.
  - Klik baris tabel untuk Edit atau Delete.
- **Tarif & Kebijakan**: form untuk mengubah tarif dan aturan menit tanpa mengubah kode (poin nilai tambah di soal).

### Pencegahan transaksi ganda / race condition ([`Services/TransaksiService.cs`](Parkir/Services/TransaksiService.cs))

| Risiko | Penanganan |
|---|---|
| Double-click, refresh, kirim ulang | **Idempotency key** (GUID unik per form). Kiriman ulang mengembalikan transaksi yang sama, bukan membuat baru. Ditambah PRG (Post/Redirect/Get) dan tombol dinonaktifkan saat submit. |
| Dua operator mencatat kendaraan yang sama pada waktu bersamaan | **`sp_getapplock` per No. Polisi**: pengecekan periode tumpang tindih dan INSERT berjalan atomik. |
| Logika aplikasi terlewati | **Unique constraint** `(NoPolisi, JamMasuk)`, `IdempotencyKey`, `NoTransaksi` |
| Dua user mengedit atau menghapus data yang sama | **`ROWVERSION`** (optimistic concurrency): user kedua ditolak lalu data terbaru dimuat ulang |
| Nomor transaksi kembar | `SEQUENCE`, bukan `MAX()+1` |

---

## Project 2: Reservasi Ruang Meeting

### Arsitektur

```
Browser ──(form POST + token CSRF)──> RuangMeetingWeb (MVC, :5151)
                                          │  HttpClient + X-Api-Key + X-Correlation-ID
                                          ▼
                                   RuangMeetingApi (Web API, :5133) ──> SQL Server (RuangMeetingDb)
                                          └─ Serilog: console + logs/api-*.log + logs/api-*.json
```

Web tidak pernah mengakses database langsung; semua lewat API sesuai ketentuan soal.

### Tabel ([`RuangMeetingApi/Database/RuangMeetingDb.sql`](RuangMeetingApi/Database/RuangMeetingDb.sql))

| Tabel | Isi |
|---|---|
| `RUANGAN` | RuanganId, KodeRuangan (unik, contoh `MR-001`), NamaRuangan, Lokasi, Kapasitas, IsAktif, RowVer |
| `RESERVASI` | ReservasiId, KodeReservasi (dari `SEQUENCE`), RuanganId, NamaPemesan, JudulMeeting, Tanggal, JamMulai, JamSelesai, JumlahPeserta, Status (`BOOKED` / `CANCELLED` / `COMPLETED`), IdempotencyKey, RowVer |

Data awal: MR-001 Meeting Room 1 (Lantai 1, 10 orang, Aktif), MR-002 Meeting Room 2 (Lantai 2, 20 orang, Aktif), MR-003 Board Room (Lantai 3, 12 orang, Tidak Aktif).

Reservasi tidak pernah dihapus. **Cancel** mengubah status menjadi `CANCELLED` agar riwayat tetap tersimpan, dan hanya `BOOKED` yang memakai slot waktu.

### Aturan bisnis

| Aturan | Ditegakkan di |
|---|---|
| Ruangan tidak aktif tidak bisa direservasi | API (HTTP 422) |
| Jumlah peserta ≤ kapasitas ruangan | API (422) |
| Jam mulai < jam selesai | API (422) + `CHECK` constraint |
| Durasi 30 menit sampai 8 jam | API (422) + `CHECK` constraint |
| Tidak boleh ada reservasi aktif yang bentrok di ruangan yang sama (berurutan 08:00–10:00 lalu 10:00–11:00 boleh) | API (409) + trigger `TR_RESERVASI_NoOverlap` |
| Tidak bisa memesan waktu yang sudah lewat | API (422) |
| Ruangan tidak bisa dinonaktifkan atau kapasitasnya diturunkan jika masih ada reservasi mendatang | API (409) |
| Ruangan yang punya riwayat reservasi tidak bisa dihapus (cukup dinonaktifkan) | API (409) |

### Endpoint API

Semua endpoint `/api/*` (kecuali `/api/health`) wajib header `X-Api-Key`. Contoh request ada di [`RuangMeetingApi.http`](RuangMeetingApi/RuangMeetingApi.http).

| Method | Route | Keterangan |
|---|---|---|
| GET | `/api/ruangan?aktif=` | daftar ruangan |
| GET | `/api/ruangan/{id}` | detail (header `ETag` = rowVersion) |
| POST | `/api/ruangan` | tambah ruangan |
| PUT / DELETE | `/api/ruangan/{id}` | wajib `If-Match: "<rowVersion>"` |
| GET | `/api/reservasi?tanggal=&ruanganId=` | riwayat reservasi |
| POST | `/api/reservasi` | wajib `Idempotency-Key: <guid>` |
| PUT | `/api/reservasi/{id}` | wajib `If-Match` |
| POST | `/api/reservasi/{id}/cancel` | wajib `If-Match` |
| GET | `/api/health` | cek koneksi database |

Semua error dikembalikan dalam format **RFC 7807 ProblemDetails** yang berisi `correlationId`:

| Kode | Arti |
|---|---|
| 401 | API key salah |
| 404 | data tidak ditemukan |
| 409 | bentrok jadwal, kode duplikat, atau ruangan masih dipakai |
| 412 | versi data sudah usang |
| 422 | aturan bisnis dilanggar (field yang salah ada di `errors`) |
| 428 | header `If-Match` tidak ada |

### Race condition

| Skenario | Penanganan |
|---|---|
| Banyak user memesan slot bentrok di ruangan yang sama secara bersamaan | Baris ruangan dikunci (`UPDLOCK, HOLDLOCK`) **sebelum** cek bentrok, jadi penulisan untuk satu ruangan berjalan antre dan atomik. |
| Penulisan yang melewati API | Trigger database memakai kunci yang sama dan mengecek ulang bentrok (`THROW 50010`). |
| Ruangan dinonaktifkan bersamaan dengan pemesanan | Penonaktifan memakai kunci baris yang sama, jadi tidak mungkin ada booking aktif di ruangan nonaktif. |
| Double-click / kirim ulang Save | `Idempotency-Key` unik: request ulang mendapat reservasi yang sama (200, header `Idempotent-Replayed: true`). |
| Dua admin mengedit data yang sama | `ROWVERSION` + `If-Match`: admin kedua mendapat 412 dan Web memuat ulang data terbaru. |

### Keamanan CSRF

| Lapisan | Penanganan |
|---|---|
| **Web** | Filter global `AutoValidateAntiforgeryToken`, jadi setiap POST wajib membawa token valid. Cookie antiforgery dan TempData memakai `SameSite=Strict` dan `HttpOnly`. |
| **Header keamanan** | `X-Frame-Options: DENY`; CSP `frame-ancestors 'none'`, `form-action 'self'`, `script-src 'self'` (tanpa script inline). |
| **API** | Tanpa cookie atau session browser dan tanpa CORS. Setiap panggilan wajib `X-Api-Key` (dibandingkan *constant-time*), dan key hanya ada di server Web. Browser korban tidak bisa dipancing mengirim request terautentikasi ke API. |

Diverifikasi dengan curl: POST tanpa token menghasilkan 400, token tanpa cookie-nya (kasus lintas situs) juga 400, dan token valid diterima.

### Logging API

- **Serilog** (konfigurasi di `appsettings.json`) dengan tiga output:
  - console
  - file teks harian `logs/api-YYYYMMDD.log`
  - file JSON terstruktur `logs/api-YYYYMMDD.json`, siap dibaca Seq, ELK, atau Loki

  Tiap file harian disimpan 14 hari.
- **Correlation ID** (`X-Correlation-ID`):
  - dikirim Web dari ID request-nya, atau dibuat oleh API
  - divalidasi hanya boleh berisi karakter aman
  - menempel di setiap baris log, dikembalikan di header respons, dan ada di setiap body error
  - ditampilkan ke user oleh Web saat terjadi error server, sehingga laporan user bisa dicocokkan dengan log
- **Log request:** satu baris per request (method, path, status, waktu). 4xx dicatat sebagai Warning, 5xx sebagai Error.
- **Log bisnis:**
  - reservasi dibuat, diubah, atau dibatalkan
  - penolakan beserta alasannya (bentrok dengan reservasi mana, kapasitas, ruangan nonaktif)
  - API key salah beserta IP pengirim
  - exception beserta stack trace

---

## Project 3: Deret Bilangan

```
2 + 3 + 4 + 4 + 6 + 8 + 6 + 9 + 12 ... n = ∑Bilangan
```

### Pola

Bilangan dikelompokkan per tiga. Kelompok ke-*k* berisi `2k, 3k, 4k`:

| k | bilangan |
|---|---|
| 1 | 2, 3, 4 |
| 2 | 4, 6, 8 |
| 3 | 6, 9, 12 |
| 4 | 8, 12, 16 |

Rumus bilangan ke-*i* (mulai dari 1), di [`Services/Deret.cs`](DeretBilangan/Services/Deret.cs):

```
suku(i) = ((i-1) div 3 + 1) × ((i-1) mod 3 + 2)
```

### Contoh dari soal

Jumlah Deret = 10, Angka yang Dicari = 6:

| Output | Hasil |
|---|---|
| Deret | `2 + 3 + 4 + 4 + 6 + 8 + 6 + 9 + 12 + 8` |
| Posisi Ditemukan | `5, 7` |
| Hasil Penjumlahan | `62` |

### Fitur

- **Jumlah Deret:** minimal 1 sesuai soal. Batas atas 10.000 hanya agar halaman tetap terbaca.
- **Angka yang Dicari:** opsional. Angka yang cocok **disorot** di dalam deret, dan jika tidak ada muncul pesan "Angka X tidak ditemukan".
- **Form GET:** karena hanya menghitung, form memakai GET sehingga hasil bisa dibagikan lewat URL, misalnya `/?JumlahDeret=10&AngkaDicari=6`.
- **Pesan validasi** dalam Bahasa Indonesia.

---

## Pengujian

```bash
dotnet test Parkir/tests/Parkir.Tests
dotnet test RuangMeetingApi/tests/RuangMeetingApi.Tests
dotnet test DeretBilangan/tests/DeretBilangan.Tests
```

| Project | Jumlah test | Yang diuji |
|---|---|---|
| Parkir | 29 | Semua aturan biaya (batas 9/10 menit, 02:04 vs 02:06, lewat tengah malam, kebijakan diubah); 25 operator bersamaan: double submit, kendaraan sama, periode tumpang tindih, edit/hapus dengan versi lama |
| Ruang Meeting | 35 | Semua aturan bisnis lewat HTTP; API key; correlation ID; 10–30 request serentak: slot bentrok, double submit, slot berbeda, nonaktifkan ruangan sambil booking, dua admin edit; trigger menolak insert langsung ke DB |
| Deret Bilangan | 12 | Contoh soal, pola 15 suku pertama, posisi ganda / tidak ditemukan, jumlah dibandingkan rumus tertutup `9·m(m+1)/2`, minimal 1 |

**Total: 76 test, semua lulus.**

Test Parkir dan Ruang Meeting berjalan ke **SQL Server sungguhan** di database terpisah (`ParkirDb_Test`, `RuangMeetingDb_Test`), karena lock, trigger, unique index, dan ROWVERSION hanya bisa dibuktikan pada engine aslinya. Server bisa diganti lewat variabel lingkungan `PARKIR_TEST_DB` / `RUANGMEETING_TEST_DB`.

**Bukti test race condition benar-benar mendeteksi masalah** (mutation check):

| Konfigurasi | Hasil |
|---|---|
| Parkir tanpa `sp_getapplock` | test periode tumpang tindih dan double submit gagal (data ganda masuk) |
| Ruang Meeting tanpa kunci baris ruangan | trigger masih mencegah bentrok, tetapi booking sah ikut tertolak (deadlock) dan booking bisa masuk ke ruangan yang sedang dinonaktifkan |
| Ruang Meeting tanpa kunci dan tanpa trigger | double booking terjadi |
| Semua pengaman aktif | semua test lulus |

---

## Struktur Folder

```
seniortest/
├── README.md                     ← dokumen ini
├── Parkir/                       Project 1 (MVC + EF Core)
│   ├── Database/ParkirDb.sql     skema TARIF, TRANSAKSI, sequence, seed
│   ├── Services/                 KalkulatorTarif (aturan), TransaksiService (simpan + konkurensi)
│   ├── Controllers/              ParkirController, TarifController
│   ├── Views/                    Parkir/Index, Tarif/Index
│   └── tests/Parkir.Tests/
├── RuangMeetingApi/              Project 2: REST API
│   ├── Database/RuangMeetingDb.sql   skema RUANGAN, RESERVASI, trigger, sequence, seed
│   ├── Services/                 AturanReservasi, ReservasiService, RuanganService, DbKunci
│   ├── Infrastructure/           correlation ID, API key, exception handler, mapping ProblemDetails
│   ├── Controllers/              RuanganController, ReservasiController
│   ├── RuangMeetingApi.http      contoh request
│   └── tests/RuangMeetingApi.Tests/
├── RuangMeetingWeb/              Project 2: tampilan MVC (client API)
│   ├── Services/ApiClient.cs     typed HttpClient + handler header
│   ├── Controllers/              ReservasiController, RuanganController
│   └── Views/                    Reservasi/Index, Ruangan/Index
└── DeretBilangan/                Project 3 (MVC, tanpa database)
    ├── Services/Deret.cs         rumus deret
    └── tests/DeretBilangan.Tests/
```

---

## Catatan & Keputusan Desain

**Parkir**
- Menambah field **No. Polisi**. Tanpa nomor polisi tidak ada cara mengenali bahwa dua input adalah kendaraan yang sama, padahal itu diperlukan untuk mencegah transaksi ganda.
- Label "Jam Pertama / Jam Kedua" di mockup menjadi **Jam Masuk / Jam Keluar**.
- Tarif 1/2, Durasi, dan Bayar hanya-baca dan dihitung server.

**Ruang Meeting**
- Jam Mulai dan Jam Selesai memakai input waktu (kelipatan 5 menit).
- Status reservasi baru selalu BOOKED. Tombol ketiga, **Cancel**, membatalkan reservasi yang dipilih.
- Tabel reservasi diberi filter tanggal.

**Deret Bilangan**
- Batas atas Jumlah Deret 10.000.
- Angka yang Dicari opsional.

**Umum**
- **Konfigurasi rahasia:** password `sa` dan `Api:Key` di `appsettings.json` hanya untuk pengembangan. Di produksi gunakan user-secrets atau variabel lingkungan.
- **Ketergantungan internet:** font Cabin dan ikon Font Awesome dimuat dari Google Fonts dan cdnjs, jadi butuh koneksi internet. Tanpa internet, tampilan tetap berfungsi dengan font cadangan.
