using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Parkir.Data;
using Parkir.Models;
using Parkir.Services;

namespace Parkir.Tests;

/// <summary>
/// Runs against a real SQL Server (separate database ParkirDb_Test) because locks, unique indexes
/// and ROWVERSION can only be proven against the real engine.
/// Override the server with env var PARKIR_TEST_DB.
/// </summary>
public class DbFixture : IAsyncLifetime
{
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("PARKIR_TEST_DB")
        ?? "Server=localhost,1433;Database=ParkirDb_Test;User Id=sa;Password=user@12345;TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await DatabaseInitializer.EnsureAsync(ConnectionString, Path.Combine(AppContext.BaseDirectory, "Database", "ParkirDb.sql"));
        await using var db = NewDb();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TRANSAKSI");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public ParkirDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ParkirDbContext>()
            .UseSqlServer(new SqlConnectionStringBuilder(ConnectionString) { MaxPoolSize = 200 }.ConnectionString).Options);
}

public class KonkurensiTests(DbFixture fx) : IClassFixture<DbFixture>
{
    private const int User = 25; // simultaneous operators

    private static string PlatUnik() => "T" + Random.Shared.Next(1_000_000, 9_999_999);
    private static readonly DateTime Masuk = new(2014, 4, 1, 8, 15, 0);

    /// <summary>Each "user" gets its own DbContext/connection, like separate HTTP requests; all start together.</summary>
    private async Task<HasilService[]> Serentak(Func<TransaksiService, int, Task<HasilService>> aksi)
    {
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, User).Select(i => Task.Run(async () =>
        {
            await using var db = fx.NewDb();
            await db.Database.OpenConnectionAsync(); // warm the connection so the race is on the write
            start.Wait();
            return await aksi(new TransaksiService(db), i);
        })).ToArray();
        await Task.Delay(200);
        start.Set();
        return await Task.WhenAll(tasks);
    }

    private async Task<int> JumlahRow(string plat)
    {
        await using var db = fx.NewDb();
        return await db.Transaksi.CountAsync(t => t.NoPolisi == plat);
    }

    [Fact]
    public async Task Double_submit_dengan_key_sama_hanya_satu_row()
    {
        var plat = PlatUnik();
        var key = Guid.NewGuid();
        var input = new TransaksiInput(plat, 1, Masuk, Masuk.AddMinutes(126), key);

        var hasil = await Serentak((s, _) => s.SimpanAsync(input));

        Assert.All(hasil, h => Assert.True(h.Ok, h.Pesan));                                   // every replay gets "ok"...
        Assert.Single(hasil.Select(h => h.Data!.NoTransaksi).Distinct());                    // ...pointing at the same row
        Assert.Equal(1, await JumlahRow(plat));
        Assert.Equal(2500m, hasil[0].Data!.Bayar);
    }

    [Fact]
    public async Task Banyak_operator_input_kendaraan_sama_hanya_satu_yang_masuk()
    {
        var plat = PlatUnik();

        // different forms (different keys), same vehicle, same entry time
        var hasil = await Serentak((s, _) =>
            s.SimpanAsync(new TransaksiInput(plat, 1, Masuk, Masuk.AddHours(1), Guid.NewGuid())));

        Assert.Equal(1, hasil.Count(h => h.Ok));
        Assert.All(hasil.Where(h => !h.Ok), h => Assert.Equal(StatusHasil.Duplikat, h.Status));
        Assert.Equal(1, await JumlahRow(plat));
    }

    [Fact]
    public async Task Periode_tumpang_tindih_serentak_ditolak()
    {
        var plat = PlatUnik();

        // different entry minutes (the unique index alone would NOT catch these), all overlapping
        var hasil = await Serentak((s, i) =>
            s.SimpanAsync(new TransaksiInput(plat, 1, Masuk.AddMinutes(i), Masuk.AddMinutes(i + 90), Guid.NewGuid())));

        Assert.Equal(1, hasil.Count(h => h.Ok));
        Assert.Equal(1, await JumlahRow(plat));
    }

    [Fact]
    public async Task Kendaraan_berbeda_serentak_semua_tersimpan_dengan_nomor_unik()
    {
        var plats = Enumerable.Range(0, User).Select(_ => PlatUnik()).ToArray();

        var hasil = await Serentak((s, i) =>
            s.SimpanAsync(new TransaksiInput(plats[i], 2, Masuk, Masuk.AddMinutes(30), Guid.NewGuid())));

        Assert.All(hasil, h => Assert.True(h.Ok, h.Pesan));
        Assert.Equal(User, hasil.Select(h => h.Data!.NoTransaksi).Distinct().Count());
    }

    [Fact]
    public async Task Edit_serentak_dari_versi_yang_sama_hanya_satu_menang()
    {
        var plat = PlatUnik();
        Transaksi awal;
        await using (var db = fx.NewDb())
            awal = (await new TransaksiService(db).SimpanAsync(new TransaksiInput(plat, 1, Masuk, Masuk.AddHours(1), Guid.NewGuid()))).Data!;

        // everyone loaded the same row (same RowVer) and saves a different exit time
        var hasil = await Serentak((s, i) =>
            s.UbahAsync(awal.TransaksiId, awal.RowVer, new TransaksiInput(plat, 1, Masuk, Masuk.AddHours(2).AddMinutes(i), Guid.NewGuid())));

        Assert.Equal(1, hasil.Count(h => h.Ok));
        Assert.All(hasil.Where(h => !h.Ok), h => Assert.Equal(StatusHasil.Konflik, h.Status));
    }

    [Fact]
    public async Task Hapus_dengan_versi_lama_ditolak()
    {
        var plat = PlatUnik();
        await using var db = fx.NewDb();
        var svc = new TransaksiService(db);
        var awal = (await svc.SimpanAsync(new TransaksiInput(plat, 1, Masuk, Masuk.AddHours(1), Guid.NewGuid()))).Data!;
        var versiLama = awal.RowVer;

        await using (var db2 = fx.NewDb()) // another user edits first
            Assert.True((await new TransaksiService(db2).UbahAsync(awal.TransaksiId, versiLama,
                new TransaksiInput(plat, 1, Masuk, Masuk.AddHours(3), Guid.NewGuid()))).Ok);

        db.ChangeTracker.Clear();
        var hapus = await svc.HapusAsync(awal.TransaksiId, versiLama);
        Assert.Equal(StatusHasil.Konflik, hapus.Status);
        Assert.Equal(1, await JumlahRow(plat));
    }

    [Fact]
    public async Task Tarif_disnapshot_di_transaksi()
    {
        var plat = PlatUnik();
        await using var db = fx.NewDb();
        var t = (await new TransaksiService(db).SimpanAsync(new TransaksiInput(plat, 1, Masuk, Masuk.AddMinutes(126), Guid.NewGuid()))).Data!;
        Assert.Equal((2000m, 500m, 2, 3, 2500m), (t.Tarif1, t.Tarif2, t.JamTarif1, t.JamDitagih, t.Bayar));
        Assert.Matches(@"^PRK-\d{9}$", t.NoTransaksi);
    }
}
