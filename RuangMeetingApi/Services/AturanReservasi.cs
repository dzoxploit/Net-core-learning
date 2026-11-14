using RuangMeetingApi.Contracts;
using RuangMeetingApi.Models;

namespace RuangMeetingApi.Services;

/// <summary>
/// Reservation rules that don't need the database (pure, unit-testable).
/// The rules that need data (room active, capacity, overlap) are in ReservasiService.
/// </summary>
public static class AturanReservasi
{
    public const int DurasiMinMenit = 30;
    public const int DurasiMaksMenit = 8 * 60;

    public static Dictionary<string, string[]> Validasi(ReservasiRequest r)
    {
        var e = new Dictionary<string, string[]>();
        void Add(string field, string msg) => e[field] = [.. e.GetValueOrDefault(field, []), msg];

        if (r.RuanganId <= 0) Add(nameof(r.RuanganId), "Ruangan wajib dipilih.");
        if (string.IsNullOrWhiteSpace(r.NamaPemesan)) Add(nameof(r.NamaPemesan), "Nama pemesan wajib diisi.");
        else if (r.NamaPemesan.Trim().Length > 100) Add(nameof(r.NamaPemesan), "Nama pemesan maksimal 100 karakter.");
        if (string.IsNullOrWhiteSpace(r.JudulMeeting)) Add(nameof(r.JudulMeeting), "Judul meeting wajib diisi.");
        else if (r.JudulMeeting.Trim().Length > 200) Add(nameof(r.JudulMeeting), "Judul meeting maksimal 200 karakter.");
        if (r.JumlahPeserta < 1) Add(nameof(r.JumlahPeserta), "Jumlah peserta minimal 1.");
        if (r.Status is not null && !StatusReservasi.Semua.Contains(r.Status))
            Add(nameof(r.Status), $"Status harus salah satu dari: {string.Join(", ", StatusReservasi.Semua)}.");

        if (r.JamMulai >= r.JamSelesai)
        {
            Add(nameof(r.JamSelesai), "Jam mulai harus lebih awal dari jam selesai.");
        }
        else
        {
            var durasi = (r.JamSelesai - r.JamMulai).TotalMinutes;
            if (durasi < DurasiMinMenit) Add(nameof(r.JamSelesai), "Durasi reservasi minimal 30 menit.");
            if (durasi > DurasiMaksMenit) Add(nameof(r.JamSelesai), "Durasi reservasi maksimal 8 jam.");
        }
        return e;
    }

    /// <summary>Half-open intervals: 08:00-10:00 and 10:00-11:00 do NOT overlap.</summary>
    public static bool Bentrok(TimeOnly mulaiA, TimeOnly selesaiA, TimeOnly mulaiB, TimeOnly selesaiB) =>
        mulaiA < selesaiB && selesaiA > mulaiB;
}
