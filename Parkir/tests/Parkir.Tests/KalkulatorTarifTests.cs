using Parkir.Models;
using Parkir.Services;

namespace Parkir.Tests;

public class KalkulatorTarifTests
{
    private static readonly Tarif Mobil = new() { TarifId = 1, JenisKendaraan = "Mobil", Tarif1 = 2000, Tarif2 = 500 };
    private static readonly Tarif Motor = new() { TarifId = 2, JenisKendaraan = "Motor", Tarif1 = 1000, Tarif2 = 500 };
    private static readonly DateTime T0 = new(2014, 4, 1, 8, 15, 0);

    [Theory]
    // durasi (menit) -> jam ditagih, bayar (Mobil)
    [InlineData(0, 0, 0)]        // free
    [InlineData(9, 0, 0)]        // < 10 minutes: free
    [InlineData(10, 1, 2000)]    // 10 minutes: no longer free
    [InlineData(60, 1, 2000)]    // mockup: 08:15 -> 09:15 = 01:00 -> 2000
    [InlineData(65, 1, 2000)]    // 01:05: exactly 5 extra minutes -> not counted
    [InlineData(66, 2, 2000)]    // 01:06 -> 2 hours, still within Tarif 1
    [InlineData(120, 2, 2000)]
    [InlineData(124, 2, 2000)]   // spec: 02:04 = 2 hours
    [InlineData(125, 2, 2000)]   // 02:05: not "more than 5"
    [InlineData(126, 3, 2500)]   // spec: 02:06 = 3 hours
    [InlineData(180, 3, 2500)]
    [InlineData(305, 5, 3500)]   // 05:05 -> 5h: 2000 + 3*500
    [InlineData(306, 6, 4000)]
    [InlineData(24 * 60, 24, 13000)] // a full day across midnight: 2000 + 22*500
    public void Mobil_sesuai_aturan(int menit, int jam, decimal bayar)
    {
        var h = KalkulatorTarif.Hitung(T0, T0.AddMinutes(menit), Mobil);
        Assert.Equal(menit, h.DurasiMenit);
        Assert.Equal(jam, h.JamDitagih);
        Assert.Equal(bayar, h.Bayar);
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(30, 1000)]
    [InlineData(126, 1500)]
    [InlineData(240, 2000)]
    public void Motor_memakai_tarifnya_sendiri(int menit, decimal bayar) =>
        Assert.Equal(bayar, KalkulatorTarif.Hitung(T0, T0.AddMinutes(menit), Motor).Bayar);

    [Fact]
    public void Kebijakan_bisa_diubah_lewat_tarif()
    {
        var kebijakanBaru = new Tarif { Tarif1 = 3000, Tarif2 = 1000, JamTarif1 = 1, MenitGratis = 15, MenitToleransi = 10 };
        Assert.Equal(0, KalkulatorTarif.Hitung(T0, T0.AddMinutes(14), kebijakanBaru).Bayar);   // free window widened
        Assert.Equal(3000, KalkulatorTarif.Hitung(T0, T0.AddMinutes(70), kebijakanBaru).Bayar); // 01:10 -> 1h
        Assert.Equal(4000, KalkulatorTarif.Hitung(T0, T0.AddMinutes(71), kebijakanBaru).Bayar); // 01:11 -> 2h
    }

    [Fact]
    public void Detik_diabaikan()
    {
        var h = KalkulatorTarif.Hitung(T0.AddSeconds(59), T0.AddMinutes(9).AddSeconds(58), Mobil);
        Assert.Equal(9, h.DurasiMenit);
        Assert.Equal(0, h.Bayar);
    }

    [Fact]
    public void Format_durasi_hh_mm() =>
        Assert.Equal("02:06", KalkulatorTarif.Hitung(T0, T0.AddMinutes(126), Mobil).DurasiTeks);

    [Fact]
    public void Jam_keluar_sebelum_masuk_ditolak() =>
        Assert.Throws<ArgumentException>(() => KalkulatorTarif.Hitung(T0, T0.AddMinutes(-1), Mobil));
}
