using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Parkir.Data;
using Parkir.Models;

namespace Parkir.Services;

public record TransaksiInput(
    string NoPolisi,
    int TarifId,
    DateTime JamMasuk,
    DateTime JamKeluar,
    Guid IdempotencyKey);

public enum StatusHasil { Ok, Validasi, Duplikat, Konflik, TidakDitemukan }

public record HasilService(StatusHasil Status, string Pesan, Transaksi? Data = null)
{
    public bool Ok => Status == StatusHasil.Ok;
}

/// <summary>
/// Writes to TRANSAKSI. Protection against concurrent / double submissions:
///
///  1. Idempotency key - every form render carries a new GUID (unique index). A double click,
///     browser resend or retry with the same key returns the row that already exists instead of inserting again.
///  2. Per-vehicle application lock (sp_getapplock, scoped to the DB transaction) - two operators saving the
///     same plate at the same moment are serialized, so the "is this period already recorded?" check
///     and the INSERT are atomic and overlapping stays can't slip in.
///  3. Unique constraints (NoPolisi + JamMasuk, IdempotencyKey, NoTransaksi) - last line of defence
///     enforced by SQL Server even if the application logic is bypassed.
///  4. ROWVERSION optimistic concurrency on Edit/Delete - if another user changed or deleted
///     the row after you loaded it, your write is rejected instead of silently overwriting theirs.
///  5. Receipt numbers come from a SEQUENCE, never MAX()+1.
/// </summary>
public class TransaksiService(ParkirDbContext db)
{
    private const int LockTimeoutMs = 10_000;

    public static string NormalisasiNoPolisi(string? s) =>
        string.Concat((s ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public async Task<HasilService> SimpanAsync(TransaksiInput input, CancellationToken ct = default)
    {
        var plat = NormalisasiNoPolisi(input.NoPolisi);
        if (Validasi(plat, input) is { } err) return err;

        // fast path: this exact submission was already processed
        if (await CariByKeyAsync(input.IdempotencyKey, ct) is { } sudahAda)
            return new(StatusHasil.Ok, $"Transaksi {sudahAda.NoTransaksi} sudah tersimpan.", sudahAda);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await KunciKendaraanAsync(plat, ct);

            // re-check under the lock: a parallel request with the same key may have just committed
            if (await CariByKeyAsync(input.IdempotencyKey, ct) is { } replay)
                return new(StatusHasil.Ok, $"Transaksi {replay.NoTransaksi} sudah tersimpan.", replay);

            var tarif = await db.Tarif.AsNoTracking().FirstOrDefaultAsync(t => t.TarifId == input.TarifId && t.IsAktif, ct);
            if (tarif is null) return new(StatusHasil.Validasi, "Jenis kendaraan tidak ditemukan / tidak aktif.");

            if (await CariTumpangTindihAsync(plat, input.JamMasuk, input.JamKeluar, null, ct) is { } bentrok)
                return Bentrok(plat, bentrok);

            var trx = new Transaksi { NoPolisi = plat, IdempotencyKey = input.IdempotencyKey };
            Terapkan(trx, tarif, input);
            db.Transaksi.Add(trx);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new(StatusHasil.Ok, $"Transaksi {trx.NoTransaksi} berhasil disimpan.", trx);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var index))
        {
            db.ChangeTracker.Clear();
            if (index.Contains("IdempotencyKey") && await CariByKeyAsync(input.IdempotencyKey, ct) is { } dup)
                return new(StatusHasil.Ok, $"Transaksi {dup.NoTransaksi} sudah tersimpan.", dup);
            return new(StatusHasil.Duplikat, $"Kendaraan {plat} dengan jam masuk tersebut sudah tercatat.");
        }
        catch (SqlException ex) when (ex.Number == 51000)
        {
            return new(StatusHasil.Konflik, "Data kendaraan sedang diproses pengguna lain, silakan coba lagi.");
        }
    }

    public async Task<HasilService> UbahAsync(long id, byte[] rowVer, TransaksiInput input, CancellationToken ct = default)
    {
        var plat = NormalisasiNoPolisi(input.NoPolisi);
        if (Validasi(plat, input) is { } err) return err;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var platLama = await db.Transaksi.Where(t => t.TransaksiId == id).Select(t => t.NoPolisi).FirstOrDefaultAsync(ct);
            if (platLama is null) return TidakAda();

            // lock old and new plate, always in the same order to avoid deadlocks
            foreach (var p in new[] { platLama, plat }.Distinct().Order(StringComparer.Ordinal))
                await KunciKendaraanAsync(p, ct);

            var trx = await db.Transaksi.FirstOrDefaultAsync(t => t.TransaksiId == id, ct);
            if (trx is null) return TidakAda();

            // compare against the version the user loaded, not what's in the DB now
            db.Entry(trx).Property(t => t.RowVer).OriginalValue = rowVer;

            var tarif = await db.Tarif.AsNoTracking().FirstOrDefaultAsync(t => t.TarifId == input.TarifId, ct);
            if (tarif is null) return new(StatusHasil.Validasi, "Jenis kendaraan tidak ditemukan.");

            if (await CariTumpangTindihAsync(plat, input.JamMasuk, input.JamKeluar, id, ct) is { } bentrok)
                return Bentrok(plat, bentrok);

            trx.NoPolisi = plat;
            trx.UpdatedAt = DateTime.Now;
            Terapkan(trx, tarif, input);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new(StatusHasil.Ok, $"Transaksi {trx.NoTransaksi} berhasil diubah.", trx);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Konflik();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out _))
        {
            return new(StatusHasil.Duplikat, $"Kendaraan {plat} dengan jam masuk tersebut sudah tercatat.");
        }
        catch (SqlException ex) when (ex.Number == 51000)
        {
            return new(StatusHasil.Konflik, "Data kendaraan sedang diproses pengguna lain, silakan coba lagi.");
        }
    }

    public async Task<HasilService> HapusAsync(long id, byte[] rowVer, CancellationToken ct = default)
    {
        // DELETE ... WHERE TransaksiId = @id AND RowVer = @rowVer
        var stub = new Transaksi { TransaksiId = id, RowVer = rowVer };
        db.Transaksi.Attach(stub);
        db.Transaksi.Remove(stub);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(StatusHasil.Ok, "Transaksi berhasil dihapus.");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return await db.Transaksi.AnyAsync(t => t.TransaksiId == id, ct) ? Konflik() : TidakAda();
        }
    }

    // ---------------------------------------------------------------- helpers

    private static HasilService? Validasi(string plat, TransaksiInput input)
    {
        if (plat.Length is 0 or > 15) return new(StatusHasil.Validasi, "No. Polisi wajib diisi (maks. 15 karakter).");
        if (input.JamKeluar < input.JamMasuk) return new(StatusHasil.Validasi, "Jam keluar tidak boleh lebih awal dari jam masuk.");
        if (input.IdempotencyKey == Guid.Empty) return new(StatusHasil.Validasi, "Form tidak valid, silakan muat ulang halaman.");
        return null;
    }

    private static void Terapkan(Transaksi trx, Tarif tarif, TransaksiInput input)
    {
        var h = KalkulatorTarif.Hitung(input.JamMasuk, input.JamKeluar, tarif);
        trx.TarifId = tarif.TarifId;
        trx.JamMasuk = input.JamMasuk;
        trx.JamKeluar = input.JamKeluar;
        trx.DurasiMenit = h.DurasiMenit;
        trx.JamDitagih = h.JamDitagih;
        trx.Bayar = h.Bayar;
        trx.Tarif1 = tarif.Tarif1;
        trx.Tarif2 = tarif.Tarif2;
        trx.JamTarif1 = tarif.JamTarif1;
    }

    private Task<Transaksi?> CariByKeyAsync(Guid key, CancellationToken ct) =>
        db.Transaksi.AsNoTracking().FirstOrDefaultAsync(t => t.IdempotencyKey == key, ct);

    /// <summary>Another stay of the same vehicle whose period overlaps [masuk, keluar].</summary>
    private Task<Transaksi?> CariTumpangTindihAsync(string plat, DateTime masuk, DateTime keluar, long? kecualiId, CancellationToken ct) =>
        db.Transaksi.AsNoTracking()
            .Where(t => t.NoPolisi == plat && (kecualiId == null || t.TransaksiId != kecualiId))
            .Where(t => t.JamMasuk == masuk || (t.JamMasuk < keluar && t.JamKeluar > masuk))
            .FirstOrDefaultAsync(ct);

    /// <summary>Exclusive lock on one plate number for the rest of the current DB transaction.</summary>
    private Task KunciKendaraanAsync(string plat, CancellationToken ct)
    {
        var resource = "TRANSAKSI:" + plat;
        return db.Database.ExecuteSqlAsync($"""
            DECLARE @r INT;
            EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                                    @LockOwner = 'Transaction', @LockTimeout = {LockTimeoutMs};
            IF @r < 0 THROW 51000, 'Lock timeout', 1;
            """, ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex, out string index)
    {
        index = "";
        if (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            index = sql.Message;
            return true;
        }
        return false;
    }

    private static HasilService Bentrok(string plat, Transaksi t) => new(StatusHasil.Duplikat,
        $"Kendaraan {plat} sudah tercatat parkir pada rentang waktu tersebut ({t.NoTransaksi}, {t.JamMasuk:dd/MM/yyyy HH:mm} - {t.JamKeluar:HH:mm}).");

    private static HasilService Konflik() => new(StatusHasil.Konflik,
        "Data ini sudah diubah atau dihapus oleh pengguna lain. Data terbaru sudah dimuat, silakan periksa lalu ulangi.");

    private static HasilService TidakAda() => new(StatusHasil.TidakDitemukan, "Transaksi tidak ditemukan (mungkin sudah dihapus pengguna lain).");
}
