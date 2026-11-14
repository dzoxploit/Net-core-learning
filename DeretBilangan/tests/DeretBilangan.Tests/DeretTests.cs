using DeretBilangan.Services;

namespace DeretBilangan.Tests;

public class DeretTests
{
    [Fact]
    public void Contoh_soal_jumlah_10_cari_6()
    {
        var h = Deret.Hitung(10, 6);
        Assert.Equal("2 + 3 + 4 + 4 + 6 + 8 + 6 + 9 + 12 + 8", h.Teks);
        Assert.Equal([5, 7], h.Posisi);
        Assert.Equal(62, h.Jumlah);
    }

    [Fact]
    public void Pola_kelompok_tiga_2k_3k_4k()
    {
        long[] diharapkan = [2, 3, 4, 4, 6, 8, 6, 9, 12, 8, 12, 16, 10, 15, 20];
        Assert.Equal(diharapkan, Enumerable.Range(1, 15).Select(Deret.Suku));
    }

    [Theory]
    [InlineData(1, "2", 2)]
    [InlineData(3, "2 + 3 + 4", 9)]
    [InlineData(4, "2 + 3 + 4 + 4", 13)]
    public void Jumlah_deret_kecil(int n, string teks, long jumlah)
    {
        var h = Deret.Hitung(n, null);
        Assert.Equal(teks, h.Teks);
        Assert.Equal(jumlah, h.Jumlah);
        Assert.Empty(h.Posisi);
    }

    [Theory]
    [InlineData(10, 12, new[] { 9 })]
    [InlineData(12, 12, new[] { 9, 11 })]   // 12 = 4*3 and 3*4
    [InlineData(10, 4, new[] { 3, 4 })]
    [InlineData(10, 5, new int[0])]         // 5 never appears
    [InlineData(10, 100, new int[0])]
    public void Posisi_angka_dicari(int n, long cari, int[] posisi) =>
        Assert.Equal(posisi, Deret.Hitung(n, cari).Posisi);

    [Fact]
    public void Jumlah_sama_dengan_rumus_tertutup()
    {
        // complete groups: sum over k of 9k = 9 * m(m+1)/2
        foreach (var m in new[] { 1, 2, 10, 3333 })
            Assert.Equal(9L * m * (m + 1) / 2, Deret.Hitung(3 * m, null).Jumlah);
    }

    [Fact]
    public void Jumlah_deret_minimal_1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Deret.Hitung(0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => Deret.Suku(0));
    }
}
