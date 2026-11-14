using Parkir.Models;

namespace Parkir.Services;

public record HasilHitung(int DurasiMenit, int JamDitagih, decimal Bayar)
{
    /// <summary>Duration as hh:mm, e.g. 01:00.</summary>
    public string DurasiTeks => $"{DurasiMenit / 60:00}:{DurasiMenit % 60:00}";
}

/// <summary>
/// Pure fee rules (no I/O) so they can be unit tested.
///  - duration &lt; MenitGratis (10)         -> free
///  - extra minutes &gt; MenitToleransi (5)   -> counts as one more hour (02:06 = 3h, 02:04 = 2h)
///  - first JamTarif1 (2) hours             -> Tarif1 (flat)
///  - every following hour                  -> + Tarif2
/// </summary>
public static class KalkulatorTarif
{
    public static HasilHitung Hitung(DateTime masuk, DateTime keluar, Tarif tarif)
    {
        if (keluar < masuk)
            throw new ArgumentException("Jam keluar tidak boleh lebih awal dari jam masuk.");

        // inputs are minute precision; ignore seconds
        var durasi = (int)(Trim(keluar) - Trim(masuk)).TotalMinutes;

        if (durasi < tarif.MenitGratis)
            return new HasilHitung(durasi, 0, 0m);

        var jam = durasi / 60;
        if (durasi % 60 > tarif.MenitToleransi) jam++;
        jam = Math.Max(jam, 1); // anything chargeable is at least one hour

        var bayar = jam <= tarif.JamTarif1
            ? tarif.Tarif1
            : tarif.Tarif1 + (jam - tarif.JamTarif1) * tarif.Tarif2;

        return new HasilHitung(durasi, jam, bayar);
    }

    private static DateTime Trim(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Kind);
}
