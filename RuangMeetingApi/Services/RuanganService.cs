using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RuangMeetingApi.Contracts;
using RuangMeetingApi.Data;
using RuangMeetingApi.Models;
using static RuangMeetingApi.Services.DbKunci;

namespace RuangMeetingApi.Services;

/// <summary>
/// Room master data. Deactivating a room or lowering its capacity locks the room row first
/// (same lock as booking), so a reservation can't slip in between "check upcoming bookings"
/// and "deactivate". ROWVERSION (If-Match) protects against lost updates between admins.
/// </summary>
public partial class RuanganService(AppDbContext db, TimeProvider clock, ILogger<RuanganService> log)
{
    public Task<List<RuanganResponse>> DaftarAsync(bool? aktif, CancellationToken ct) =>
        db.Ruangan.AsNoTracking()
            .Where(r => aktif == null || r.IsAktif == aktif)
            .OrderBy(r => r.KodeRuangan)
            .Select(r => new RuanganResponse(r.RuanganId, r.KodeRuangan, r.NamaRuangan, r.Lokasi, r.Kapasitas, r.IsAktif, Convert.ToBase64String(r.RowVer)))
            .ToListAsync(ct);

    public async Task<Hasil<RuanganResponse>> AmbilAsync(int id, CancellationToken ct)
    {
        var r = await db.Ruangan.AsNoTracking().FirstOrDefaultAsync(x => x.RuanganId == id, ct);
        return r is null ? TidakAda() : Hasil<RuanganResponse>.Sukses(RuanganResponse.From(r));
    }

    public async Task<Hasil<RuanganResponse>> BuatAsync(RuanganRequest req, CancellationToken ct)
    {
        if (Validasi(req) is { Count: > 0 } errors) return Invalid(errors);

        var entity = new Ruangan();
        Terapkan(entity, req);
        db.Ruangan.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (SqlNumber(ex) is SqlUniqueIndex or SqlUniqueConstraint)
        {
            return KodeDipakai(entity.KodeRuangan);
        }
        log.LogInformation("Ruangan {KodeRuangan} dibuat ({NamaRuangan}, {Lokasi}, kapasitas {Kapasitas}, aktif {IsAktif})",
            entity.KodeRuangan, entity.NamaRuangan, entity.Lokasi, entity.Kapasitas, entity.IsAktif);
        return Hasil<RuanganResponse>.Sukses(RuanganResponse.From(entity));
    }

    public async Task<Hasil<RuanganResponse>> UbahAsync(int id, RuanganRequest req, byte[] rowVer, CancellationToken ct)
    {
        if (Validasi(req) is { Count: > 0 } errors) return Invalid(errors);

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var entity = await db.KunciRuanganAsync(id, tracking: true, ct);
            if (entity is null) return TidakAda();
            if (!entity.RowVer.SequenceEqual(rowVer)) return VersiUsang();
            db.Entry(entity).Property(r => r.RowVer).OriginalValue = rowVer;

            var dinonaktifkan = entity.IsAktif && !req.IsAktif;
            if (dinonaktifkan || req.Kapasitas < entity.Kapasitas)
            {
                var (today, now) = Sekarang();
                var mendatang = db.Reservasi.Where(r => r.RuanganId == id && r.Status == StatusReservasi.Booked
                    && (r.Tanggal > today || (r.Tanggal == today && r.JamSelesai > now)));

                if (dinonaktifkan && await mendatang.CountAsync(ct) is var n and > 0)
                    return Konflik($"Ruangan masih memiliki {n} reservasi aktif mendatang. Batalkan reservasi tersebut sebelum menonaktifkan ruangan.");

                var maksPeserta = await mendatang.MaxAsync(r => (int?)r.JumlahPeserta, ct) ?? 0;
                if (req.Kapasitas < maksPeserta)
                    return Konflik($"Kapasitas tidak boleh kurang dari {maksPeserta}: sudah ada reservasi mendatang dengan {maksPeserta} peserta.");
            }

            Terapkan(entity, req);
            entity.UpdatedAt = clock.GetLocalNow().DateTime;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            log.LogInformation("Ruangan {KodeRuangan} diubah (kapasitas {Kapasitas}, aktif {IsAktif})", entity.KodeRuangan, entity.Kapasitas, entity.IsAktif);
            return Hasil<RuanganResponse>.Sukses(RuanganResponse.From(entity));
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersiUsang();
        }
        catch (Exception ex) when (SqlNumber(ex) is SqlUniqueIndex or SqlUniqueConstraint)
        {
            return KodeDipakai(req.KodeRuangan!.Trim().ToUpperInvariant());
        }
    }

    public async Task<Hasil<RuanganResponse>> HapusAsync(int id, byte[] rowVer, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var entity = await db.KunciRuanganAsync(id, tracking: true, ct);
        if (entity is null) return TidakAda();
        if (!entity.RowVer.SequenceEqual(rowVer)) return VersiUsang();

        // reservation history must be kept: a room that was ever booked can only be deactivated
        if (await db.Reservasi.AnyAsync(r => r.RuanganId == id, ct))
            return Konflik("Ruangan sudah memiliki riwayat reservasi sehingga tidak dapat dihapus. Ubah statusnya menjadi Tidak Aktif.");

        db.Ruangan.Remove(entity);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Ruangan {KodeRuangan} dihapus", entity.KodeRuangan);
        return Hasil<RuanganResponse>.Sukses(RuanganResponse.From(entity));
    }

    // ------------------------------------------------------------------ helpers

    private (DateOnly, TimeOnly) Sekarang()
    {
        var now = clock.GetLocalNow().DateTime;
        return (DateOnly.FromDateTime(now), TimeOnly.FromDateTime(now));
    }

    private static Dictionary<string, string[]> Validasi(RuanganRequest r)
    {
        var e = new Dictionary<string, string[]>();
        var kode = r.KodeRuangan?.Trim() ?? "";
        if (kode.Length == 0) e[nameof(r.KodeRuangan)] = ["Kode ruangan wajib diisi."];
        else if (kode.Length > 20 || !KodePattern().IsMatch(kode)) e[nameof(r.KodeRuangan)] = ["Kode ruangan maks. 20 karakter: huruf, angka, dan tanda '-'."];
        if (string.IsNullOrWhiteSpace(r.NamaRuangan)) e[nameof(r.NamaRuangan)] = ["Nama ruangan wajib diisi."];
        else if (r.NamaRuangan.Trim().Length > 100) e[nameof(r.NamaRuangan)] = ["Nama ruangan maksimal 100 karakter."];
        if (string.IsNullOrWhiteSpace(r.Lokasi)) e[nameof(r.Lokasi)] = ["Lokasi wajib diisi."];
        else if (r.Lokasi.Trim().Length > 100) e[nameof(r.Lokasi)] = ["Lokasi maksimal 100 karakter."];
        if (r.Kapasitas is < 1 or > 10000) e[nameof(r.Kapasitas)] = ["Kapasitas harus antara 1 dan 10000."];
        return e;
    }

    private static void Terapkan(Ruangan e, RuanganRequest r)
    {
        e.KodeRuangan = r.KodeRuangan!.Trim().ToUpperInvariant();
        e.NamaRuangan = r.NamaRuangan!.Trim();
        e.Lokasi = r.Lokasi!.Trim();
        e.Kapasitas = r.Kapasitas;
        e.IsAktif = r.IsAktif;
    }

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex KodePattern();

    private Hasil<RuanganResponse> Konflik(string pesan)
    {
        log.LogWarning("Perubahan ruangan ditolak: {Alasan}", pesan);
        return Hasil<RuanganResponse>.Error(JenisGagal.Konflik, pesan);
    }

    private Hasil<RuanganResponse> KodeDipakai(string kode) =>
        Hasil<RuanganResponse>.Error(JenisGagal.Konflik, $"Kode ruangan {kode} sudah digunakan.",
            new() { [nameof(RuanganRequest.KodeRuangan)] = [$"Kode ruangan {kode} sudah digunakan."] });

    private static Hasil<RuanganResponse> Invalid(Dictionary<string, string[]> errors) =>
        Hasil<RuanganResponse>.Error(JenisGagal.Validasi, "Data ruangan tidak valid.", errors);
    private static Hasil<RuanganResponse> TidakAda() =>
        Hasil<RuanganResponse>.Error(JenisGagal.TidakDitemukan, "Ruangan tidak ditemukan.");
    private static Hasil<RuanganResponse> VersiUsang() =>
        Hasil<RuanganResponse>.Error(JenisGagal.VersiUsang, "Ruangan ini sudah diubah oleh pengguna lain. Muat ulang data lalu ulangi.");
}
