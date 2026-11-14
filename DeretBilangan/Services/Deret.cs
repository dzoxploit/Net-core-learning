namespace DeretBilangan.Services;

public record HasilDeret(IReadOnlyList<long> Bilangan, IReadOnlyList<int> Posisi, long Jumlah)
{
    public string Teks => string.Join(" + ", Bilangan);
}

/// <summary>
/// Series 2 + 3 + 4 + 4 + 6 + 8 + 6 + 9 + 12 + 8 + ...
///
/// The numbers come in groups of three. Group k (k = 1, 2, 3, ...) is  2k, 3k, 4k:
///   k=1: 2, 3, 4    k=2: 4, 6, 8    k=3: 6, 9, 12    k=4: 8, 12, 16 ...
///
/// So the number at position i (1-based) is  ((i-1)/3 + 1) * ((i-1)%3 + 2).
/// </summary>
public static class Deret
{
    public const int Minimal = 1;
    public const int Maksimal = 10_000; // keeps the page readable; the formula itself has no limit

    public static long Suku(int posisi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(posisi, 1);
        var i = posisi - 1;
        return (long)(i / 3 + 1) * (i % 3 + 2);
    }

    public static HasilDeret Hitung(int jumlahDeret, long? angkaDicari)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(jumlahDeret, Minimal);

        var bilangan = new long[jumlahDeret];
        var posisi = new List<int>();
        long jumlah = 0;

        for (var p = 1; p <= jumlahDeret; p++)
        {
            var n = Suku(p);
            bilangan[p - 1] = n;
            jumlah += n;
            if (n == angkaDicari) posisi.Add(p);
        }
        return new HasilDeret(bilangan, posisi, jumlah);
    }
}
