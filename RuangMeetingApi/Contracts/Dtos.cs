using RuangMeetingApi.Models;

namespace RuangMeetingApi.Contracts;

public record RuanganRequest(string? KodeRuangan, string? NamaRuangan, string? Lokasi, int Kapasitas, bool IsAktif = true);

public record RuanganResponse(int RuanganId, string KodeRuangan, string NamaRuangan, string Lokasi, int Kapasitas, bool IsAktif, string RowVersion)
{
    public static RuanganResponse From(Ruangan r) =>
        new(r.RuanganId, r.KodeRuangan, r.NamaRuangan, r.Lokasi, r.Kapasitas, r.IsAktif, Convert.ToBase64String(r.RowVer));
}

public record ReservasiRequest(
    int RuanganId,
    string? NamaPemesan,
    string? JudulMeeting,
    DateOnly Tanggal,
    TimeOnly JamMulai,
    TimeOnly JamSelesai,
    int JumlahPeserta,
    string? Status = null); // null on create = BOOKED

public record ReservasiResponse(
    long ReservasiId, string KodeReservasi,
    int RuanganId, string KodeRuangan, string NamaRuangan, int Kapasitas,
    string NamaPemesan, string JudulMeeting,
    DateOnly Tanggal, TimeOnly JamMulai, TimeOnly JamSelesai,
    int JumlahPeserta, string Status, string RowVersion)
{
    public static ReservasiResponse From(Reservasi r, Ruangan ruang) => new(
        r.ReservasiId, r.KodeReservasi, r.RuanganId, ruang.KodeRuangan, ruang.NamaRuangan, ruang.Kapasitas,
        r.NamaPemesan, r.JudulMeeting, r.Tanggal, r.JamMulai, r.JamSelesai,
        r.JumlahPeserta, r.Status, Convert.ToBase64String(r.RowVer));
}
