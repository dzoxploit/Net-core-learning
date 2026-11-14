# Parkir: parking fee calculator

ASP.NET Core MVC (.NET 11) + EF Core + SQL Server. The UI follows the mockup (form, grid, Save/Edit/Delete). It uses the theme from *gunadarma-gulf-airplane-app*: Cabin font, dark blue-grey Material palette, fixed sidebar.

## Run

```bash
# SQL Server (already running as the `sqlserver` container in this setup)
docker run -d --name sqlserver -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='user@12345' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

dotnet run --launch-profile http        # http://localhost:5181
dotnet test tests/Parkir.Tests          # 29 tests (unit + concurrency against SQL Server, DB ParkirDb_Test)
```

On startup the app creates `ParkirDb` and runs [`Database/ParkirDb.sql`](Database/ParkirDb.sql). The script is idempotent and seeds the rates. The connection string is in `appsettings.json`. Use the `PARKIR_TEST_DB` environment variable to point the tests at another server.

## Tables

| Table | Purpose |
|---|---|
| `TARIF` | One row per vehicle type: `Tarif1` (flat rate for the first `JamTarif1` hours), `Tarif2` (per extra hour), plus the policy values `MenitGratis` (10) and `MenitToleransi` (5). Edit them on the **Tarif & Kebijakan** page. |
| `TRANSAKSI` | Entry and exit time, duration, hours charged, amount paid, and a **snapshot** of the rate used. Changing a rate later does not change past transactions. |

Seed rates: Mobil 2000 / 500, Motor 1000 / 500.

## Fee rules ([`Services/KalkulatorTarif.cs`](Services/KalkulatorTarif.cs))

1. Duration under `MenitGratis` (10 minutes): free.
2. Hours charged = full hours, plus 1 if the leftover minutes are more than `MenitToleransi` (5). So 02:04 is 2 hours, 02:05 is 2 hours, and 02:06 is 3 hours.
3. Up to `JamTarif1` (2) hours, the fee is `Tarif1`. Each hour after that adds `Tarif2`.

The form's Durasi/Bayar preview calls the server (`/Parkir/Hitung`), so the rules exist in only one place.

## Concurrent and duplicate transactions

The goal is that several operators saving at the same moment, a double-click, or a browser resend never produces duplicate or overlapping rows. All write logic is in [`Services/TransaksiService.cs`](Services/TransaksiService.cs).

| Risk | Protection |
|---|---|
| Double-click, refresh, or a retried request | **Idempotency key**: each form carries a new GUID, which has a `UNIQUE` constraint. A resend gets back the row that already exists, not a second row. Save also redirects after posting (PRG), and the buttons disable themselves on submit. |
| Two operators save the same plate at the same time with overlapping periods | **`sp_getapplock` per plate number**, held for the length of the DB transaction. The overlap check and the INSERT run atomically, so one save wins and the others get a "sudah tercatat" message. |
| Logic bypassed or a bug | **Unique constraints** `(NoPolisi, JamMasuk)`, `IdempotencyKey` and `NoTransaksi`, enforced by SQL Server. |
| Two users edit or delete the same row (lost update) | **`ROWVERSION`** optimistic concurrency on `TRANSAKSI` and `TARIF`. The second write is rejected and the latest data is reloaded. |
| Duplicate receipt numbers | `NoTransaksi` comes from a **`SEQUENCE`**, never `MAX()+1`. |

[`tests/Parkir.Tests/KonkurensiTests.cs`](tests/Parkir.Tests/KonkurensiTests.cs) runs 25 simultaneous "operators", each on its own connection, against the real database, and checks every case above. With the lock removed, the overlap and replay tests fail, which shows the tests really exercise the race.

## Differences from the mockup

- Added a **No. Polisi** field. Without a plate number there is no way to tell that two submissions are the same vehicle.
- "Jam Pertama / Jam Kedua" are labelled **Jam Masuk / Jam Keluar**.
- Tarif 1/2, Durasi and Bayar are read-only and computed by the server. Rates are changed on the Tarif page, not typed in per transaction.
- Save creates a new transaction. Click a row in the grid to load it, then use Edit or Delete.
