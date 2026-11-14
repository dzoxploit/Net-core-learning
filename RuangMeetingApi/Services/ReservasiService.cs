using System.Data;
using Microsoft.EntityFrameworkCore;
using RuangMeetingApi.Contracts;
using RuangMeetingApi.Data;
using RuangMeetingApi.Models;
using static RuangMeetingApi.Services.DbKunci;

namespace RuangMeetingApi.Services;

/// <summary>
/// Reservation writes. Race-condition handling:
///  - the room row is locked (UPDLOCK) before the availability check, so two users booking the
///    same room at the same time are serialized and can never both pass the overlap check;
///  - Idempotency-Key (unique) makes a resent/double-clicked create return the existing reservation;
///  - ROWVERSION (If-Match) rejects edits/cancels based on stale data (HTTP 412);
///  - the TR_RESERVASI_NoOverlap trigger re-checks overlap inside SQL Server as a backstop.
/// </summary>
public class ReservasiService(AppDbContext db, TimeProvider clock, ILogger<ReservasiService> log)
{
    public async Task<List<ReservasiResponse>> DaftarAsync(DateOnly? tanggal, int? ruanganId, CancellationToken ct)
    {
        var q = db.Reservasi.AsNoTracking().Include(r => r.Ruangan).AsQueryable();
        if (tanggal is not null) q = q.Where(r => r.Tanggal == tanggal);
        if (ruanganId is not null) q = q.Where(r => r.RuanganId == ruanganId);
        var rows = await q.OrderByDescending(r => r.Tanggal).ThenBy(r => r.JamMulai).ThenBy(r => r.ReservasiId)
            .Take(500).ToListAsync(ct);
        return rows.Select(r => ReservasiResponse.From(r, r.Ruangan!)).ToList();
    }

    public async Task<Hasil<ReservasiResponse>> AmbilAsync(long id, CancellationToken ct)
    {
        var r = await db.Reservasi.AsNoTracking().Include(x => x.Ruangan).FirstOrDefaultAsync(x => x.ReservasiId == id, ct);
        return r is null ? TidakAda() : Hasil<ReservasiResponse>.Sukses(ReservasiResponse.From(r, r.Ruangan!));
    }

    public async Task<Hasil<ReservasiResponse>> BuatAsync(ReservasiRequest req, Guid idempotencyKey, CancellationToken ct)
    {
        var errors = AturanReservasi.Validasi(req);
        if (req.Status is not null && req.Status != StatusReservasi.Booked)
            errors[nameof(req.Status)] = ["Reservasi baru harus berstatus BOOKED."];
        if (idempotencyKey == Guid.Empty)
            errors["Idempotency-Key"] = ["Header Idempotency-Key (GUID) wajib diisi."];
        if (errors.Count > 0) return Invalid(errors);

        if (await CariByKeyAsync(idempotencyKey, ct) is { } replay) return Replay(replay);
        if (DiMasaLalu(req)) return Invalid(nameof(req.Tanggal), "Waktu reservasi sudah lewat.");

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var ruang = await db.KunciRuanganAsync(req.RuanganId, tracking: false, ct);
            if (ruang is null) return Invalid(nameof(req.RuanganId), "Ruangan tidak ditemukan.");

            // a parallel request with the same key may have committed while we waited for the lock
            if (await CariByKeyAsync(idempotencyKey, ct) is { } replay2) return Replay(replay2);

            if (CekRuangan(ruang, req) is { } gagalRuang) return gagalRuang;
            if (await CariBentrokAsync(req, null, ct) is { } bentrok) return Bentrok(ruang, bentrok);

            var entity = new Reservasi { IdempotencyKey = idempotencyKey, Status = StatusReservasi.Booked };
            Terapkan(entity, req);
            db.Reservasi.Add(entity);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            log.LogInformation("Reservasi {KodeReservasi} dibuat: ruangan {KodeRuangan} {Tanggal:yyyy-MM-dd} {JamMulai:HH:mm}-{JamSelesai:HH:mm}, {JumlahPeserta} peserta, pemesan {NamaPemesan}",
                entity.KodeReservasi, ruang.KodeRuangan, entity.Tanggal, entity.JamMulai, entity.JamSelesai, entity.JumlahPeserta, entity.NamaPemesan);
            return Hasil<ReservasiResponse>.Sukses(ReservasiResponse.From(entity, ruang));
        }
        catch (Exception ex) when (SqlNumber(ex) is SqlUniqueIndex or SqlUniqueConstraint)
        {
            db.ChangeTracker.Clear();
            if (await CariByKeyAsync(idempotencyKey, ct) is { } replay3) return Replay(replay3);
            throw;
        }
        catch (Exception ex) when (SqlNumber(ex) is TriggerBentrok or Deadlock)
        {
            log.LogWarning(ex, "Reservasi ditolak oleh database (bentrok/deadlock) untuk ruangan {RuanganId} {Tanggal:yyyy-MM-dd}", req.RuanganId, req.Tanggal);
            return Hasil<ReservasiResponse>.Error(JenisGagal.Konflik, "Ruangan sudah dipesan pada waktu tersebut oleh pengguna lain. Silakan pilih waktu lain.");
        }
    }

    public async Task<Hasil<ReservasiResponse>> UbahAsync(long id, ReservasiRequest req, byte[] rowVer, CancellationToken ct)
    {
        var errors = AturanReservasi.Validasi(req);
        if (errors.Count > 0) return Invalid(errors);
        var status = req.Status ?? StatusReservasi.Booked;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var ruangLama = await db.Reservasi.Where(r => r.ReservasiId == id).Select(r => (int?)r.RuanganId).FirstOrDefaultAsync(ct);
            if (ruangLama is null) return TidakAda();

            // lock old and new room, always in ascending id order so two edits can't deadlock each other
            Ruangan? ruang = null;
            foreach (var rid in new[] { ruangLama.Value, req.RuanganId }.Distinct().Order())
            {
                var r = await db.KunciRuanganAsync(rid, tracking: false, ct);
                if (rid == req.RuanganId) ruang = r;
            }
            if (ruang is null) return Invalid(nameof(req.RuanganId), "Ruangan tidak ditemukan.");

            var entity = await db.Reservasi.FirstOrDefaultAsync(r => r.ReservasiId == id, ct);
            if (entity is null) return TidakAda();
            if (!entity.RowVer.SequenceEqual(rowVer)) return VersiUsang();
            db.Entry(entity).Property(r => r.RowVer).OriginalValue = rowVer;

            if (status == StatusReservasi.Booked)
            {
                var jadwalBerubah = entity.Status != StatusReservasi.Booked || entity.RuanganId != req.RuanganId
                    || entity.Tanggal != req.Tanggal || entity.JamMulai != req.JamMulai || entity.JamSelesai != req.JamSelesai;
                if (jadwalBerubah && DiMasaLalu(req)) return Invalid(nameof(req.Tanggal), "Waktu reservasi sudah lewat.");
                if (CekRuangan(ruang, req) is { } gagalRuang) return gagalRuang;
                if (await CariBentrokAsync(req, id, ct) is { } bentrok) return Bentrok(ruang, bentrok);
            }

            var statusLama = entity.Status;
            Terapkan(entity, req);
            entity.Status = status;
            entity.UpdatedAt = clock.GetLocalNow().DateTime;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            log.LogInformation("Reservasi {KodeReservasi} diubah: ruangan {KodeRuangan} {Tanggal:yyyy-MM-dd} {JamMulai:HH:mm}-{JamSelesai:HH:mm}, status {StatusLama} -> {Status}",
                entity.KodeReservasi, ruang.KodeRuangan, entity.Tanggal, entity.JamMulai, entity.JamSelesai, statusLama, entity.Status);
            return Hasil<ReservasiResponse>.Sukses(ReservasiResponse.From(entity, ruang));
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersiUsang();
        }
        catch (Exception ex) when (SqlNumber(ex) is TriggerBentrok or Deadlock)
        {
            log.LogWarning(ex, "Perubahan reservasi {ReservasiId} ditolak oleh database (bentrok/deadlock)", id);
            return Hasil<ReservasiResponse>.Error(JenisGagal.Konflik, "Ruangan sudah dipesan pada waktu tersebut oleh pengguna lain. Silakan pilih waktu lain.");
        }
    }

    /// <summary>Cancel = status CANCELLED (history is kept). Frees the time slot.</summary>
    public async Task<Hasil<ReservasiResponse>> BatalkanAsync(long id, byte[] rowVer, CancellationToken ct)
    {
        var entity = await db.Reservasi.Include(r => r.Ruangan).FirstOrDefaultAsync(r => r.ReservasiId == id, ct);
        if (entity is null) return TidakAda();
        if (!entity.RowVer.SequenceEqual(rowVer)) return VersiUsang();
        if (entity.Status == StatusReservasi.Cancelled)
            return Hasil<ReservasiResponse>.Sukses(ReservasiResponse.From(entity, entity.Ruangan!)); // already cancelled: idempotent

        db.Entry(entity).Property(r => r.RowVer).OriginalValue = rowVer;
        entity.Status = StatusReservasi.Cancelled;
        entity.UpdatedAt = clock.GetLocalNow().DateTime;
        try
        {
            await db.SaveChangesAsync(ct); // UPDATE ... WHERE RowVer = @rowVer
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersiUsang();
        }
        log.LogInformation("Reservasi {KodeReservasi} dibatalkan ({KodeRuangan} {Tanggal:yyyy-MM-dd} {JamMulai:HH:mm}-{JamSelesai:HH:mm})",
            entity.KodeReservasi, entity.Ruangan!.KodeRuangan, entity.Tanggal, entity.JamMulai, entity.JamSelesai);
        return Hasil<ReservasiResponse>.Sukses(ReservasiResponse.From(entity, entity.Ruangan!));
    }

    // ------------------------------------------------------------------ helpers

    private Hasil<ReservasiResponse>? CekRuangan(Ruangan ruang, ReservasiRequest req)
    {
        if (!ruang.IsAktif)
        {
            log.LogWarning("Reservasi ditolak: ruangan {KodeRuangan} tidak aktif", ruang.KodeRuangan);
            return Invalid(nameof(req.RuanganId), $"Ruangan {ruang.NamaRuangan} tidak aktif dan tidak dapat direservasi.");
        }
        if (req.JumlahPeserta > ruang.Kapasitas)
        {
            log.LogWarning("Reservasi ditolak: {JumlahPeserta} peserta melebihi kapasitas {Kapasitas} ruangan {KodeRuangan}",
                req.JumlahPeserta, ruang.Kapasitas, ruang.KodeRuangan);
            return Invalid(nameof(req.JumlahPeserta), $"Jumlah peserta ({req.JumlahPeserta}) melebihi kapasitas ruangan ({ruang.Kapasitas}).");
        }
        return null;
    }

    private Task<Reservasi?> CariBentrokAsync(ReservasiRequest req, long? kecualiId, CancellationToken ct) =>
        db.Reservasi.AsNoTracking()
            .Where(r => r.RuanganId == req.RuanganId && r.Tanggal == req.Tanggal && r.Status == StatusReservasi.Booked)
            .Where(r => kecualiId == null || r.ReservasiId != kecualiId)
            .Where(r => r.JamMulai < req.JamSelesai && r.JamSelesai > req.JamMulai)
            .OrderBy(r => r.JamMulai)
            .FirstOrDefaultAsync(ct);

    private Hasil<ReservasiResponse> Bentrok(Ruangan ruang, Reservasi b)
    {
        log.LogWarning("Reservasi ditolak: bentrok dengan {KodeReservasi} di ruangan {KodeRuangan} {Tanggal:yyyy-MM-dd} {JamMulai:HH:mm}-{JamSelesai:HH:mm}",
            b.KodeReservasi, ruang.KodeRuangan, b.Tanggal, b.JamMulai, b.JamSelesai);
        return Hasil<ReservasiResponse>.Error(JenisGagal.Konflik,
            $"{ruang.NamaRuangan} sudah dipesan pada {b.Tanggal:dd/MM/yyyy} {b.JamMulai:HH\\:mm}-{b.JamSelesai:HH\\:mm} ({b.KodeReservasi}: {b.JudulMeeting}).");
    }

    private async Task<Reservasi?> CariByKeyAsync(Guid key, CancellationToken ct) =>
        await db.Reservasi.AsNoTracking().Include(r => r.Ruangan).FirstOrDefaultAsync(r => r.IdempotencyKey == key, ct);

    private Hasil<ReservasiResponse> Replay(Reservasi r)
    {
        log.LogInformation("Idempotent replay: reservasi {KodeReservasi} sudah dibuat dengan key yang sama", r.KodeReservasi);
        return Hasil<ReservasiResponse>.Sukses(ReservasiResponse.From(r, r.Ruangan!), replay: true);
    }

    private bool DiMasaLalu(ReservasiRequest req) => req.Tanggal.ToDateTime(req.JamMulai) < clock.GetLocalNow().DateTime;

    private static void Terapkan(Reservasi e, ReservasiRequest req)
    {
        e.RuanganId = req.RuanganId;
        e.NamaPemesan = req.NamaPemesan!.Trim();
        e.JudulMeeting = req.JudulMeeting!.Trim();
        e.Tanggal = req.Tanggal;
        e.JamMulai = req.JamMulai;
        e.JamSelesai = req.JamSelesai;
        e.JumlahPeserta = req.JumlahPeserta;
    }

    private static Hasil<ReservasiResponse> Invalid(Dictionary<string, string[]> errors) =>
        Hasil<ReservasiResponse>.Error(JenisGagal.Validasi, "Data reservasi tidak valid.", errors);
    private static Hasil<ReservasiResponse> Invalid(string field, string msg) =>
        Hasil<ReservasiResponse>.Error(JenisGagal.Validasi, msg, new() { [field] = [msg] });
    private static Hasil<ReservasiResponse> TidakAda() =>
        Hasil<ReservasiResponse>.Error(JenisGagal.TidakDitemukan, "Reservasi tidak ditemukan.");
    private static Hasil<ReservasiResponse> VersiUsang() =>
        Hasil<ReservasiResponse>.Error(JenisGagal.VersiUsang, "Reservasi ini sudah diubah oleh pengguna lain. Muat ulang data lalu ulangi.");
}
