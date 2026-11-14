using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using RuangMeetingApi.Contracts;

namespace RuangMeetingApi.Tests;

/// <summary>
/// Hosts the real API in-process against a real SQL Server database (RuangMeetingDb_Test).
/// Locks, triggers, unique indexes and ROWVERSION can only be verified against the real engine.
/// Override the server with env var RUANGMEETING_TEST_DB.
/// </summary>
public class ApiFixture : IDisposable
{
    public const string ApiKey = "test-key";
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("RUANGMEETING_TEST_DB")
        ?? "Server=localhost,1433;Database=RuangMeetingDb_Test;User Id=sa;Password=user@12345;TrustServerCertificate=True;Max Pool Size=300;";
    public WebApplicationFactory<Program> Factory { get; }

    public ApiFixture()
    {
        // env vars are read by WebApplication.CreateBuilder, before Program reads the connection string
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
        Environment.SetEnvironmentVariable("Api__Key", ApiKey);
        Factory = new WebApplicationFactory<Program>();
    }

    public HttpClient Client(bool withKey = true)
    {
        var c = Factory.CreateClient();
        if (withKey) c.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return c;
    }

    public void Dispose() => Factory.Dispose();
}

public class ApiTests(ApiFixture fx) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Hari = DateOnly.FromDateTime(DateTime.Today.AddYears(2));

    // ---------------------------------------------------------------- helpers

    private async Task<RuanganResponse> RuanganBaru(int kapasitas = 10, bool aktif = true)
    {
        var kode = "T-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var res = await fx.Client().PostAsJsonAsync("api/ruangan", new RuanganRequest(kode, "Test " + kode, "Lantai T", kapasitas, aktif));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RuanganResponse>(Json))!;
    }

    private static ReservasiRequest Req(int ruanganId, string mulai, string selesai, int peserta = 5, DateOnly? tgl = null, string? status = null) =>
        new(ruanganId, "Budi", "Sprint Planning", tgl ?? Hari, TimeOnly.Parse(mulai), TimeOnly.Parse(selesai), peserta, status);

    private async Task<HttpResponseMessage> Pesan(ReservasiRequest req, Guid? key = null, HttpClient? client = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "api/reservasi") { Content = JsonContent.Create(req) };
        msg.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return await (client ?? fx.Client()).SendAsync(msg);
    }

    private static async Task<T> Baca<T>(HttpResponseMessage res) => (await res.Content.ReadFromJsonAsync<T>(Json))!;

    private static HttpRequestMessage DenganIfMatch(HttpMethod m, string url, string rowVersion, object? body = null)
    {
        var msg = new HttpRequestMessage(m, url);
        if (body is not null) msg.Content = JsonContent.Create(body);
        msg.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        return msg;
    }

    private async Task<int> HitungBooked(int ruanganId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM RESERVASI WHERE RuanganId = @id AND Status = 'BOOKED'", conn);
        cmd.Parameters.AddWithValue("@id", ruanganId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Fires all requests at the same moment, each on its own HttpClient.</summary>
    private static async Task<T[]> Serentak<T>(int n, Func<int, Task<T>> aksi)
    {
        using var start = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, n).Select(i => Task.Run(async () => { start.Wait(); return await aksi(i); })).ToArray();
        await Task.Delay(200);
        start.Set();
        return await Task.WhenAll(tasks);
    }

    // ---------------------------------------------------------------- security & logging plumbing

    [Fact]
    public async Task Tanpa_api_key_ditolak_401_dengan_correlation_id()
    {
        var res = await fx.Client(withKey: false).GetAsync("api/ruangan");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("correlationId", body);
    }

    [Fact]
    public async Task Correlation_id_dari_caller_dipakai_dan_dikembalikan()
    {
        var c = fx.Client();
        c.DefaultRequestHeaders.Add("X-Correlation-ID", "test-corr-123");
        var res = await c.GetAsync("api/ruangan/999999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("test-corr-123", res.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Contains("test-corr-123", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correlation_id_berbahaya_diganti()
    {
        var c = fx.Client();
        c.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-ID", "x\" onload=alert(1)");
        var res = await c.GetAsync("api/ruangan");
        Assert.Matches("^[a-f0-9]{32}$", res.Headers.GetValues("X-Correlation-ID").Single());
    }

    // ---------------------------------------------------------------- business rules

    [Fact]
    public async Task Ruangan_tidak_aktif_tidak_bisa_direservasi()
    {
        var r = await RuanganBaru(aktif: false);
        var res = await Pesan(Req(r.RuanganId, "08:00", "09:00"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Contains("tidak aktif", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Peserta_melebihi_kapasitas_ditolak()
    {
        var r = await RuanganBaru(kapasitas: 10);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pesan(Req(r.RuanganId, "08:00", "09:00", peserta: 11))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Pesan(Req(r.RuanganId, "08:00", "09:00", peserta: 10))).StatusCode);
    }

    [Theory]
    [InlineData("09:00", "09:00")]
    [InlineData("10:00", "09:00")]
    [InlineData("09:00", "09:29")]
    [InlineData("08:00", "16:01")]
    public async Task Jam_dan_durasi_tidak_valid_ditolak(string mulai, string selesai)
    {
        var r = await RuanganBaru();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pesan(Req(r.RuanganId, mulai, selesai))).StatusCode);
    }

    [Fact]
    public async Task Batas_durasi_30_menit_dan_8_jam_diterima()
    {
        var r = await RuanganBaru();
        Assert.Equal(HttpStatusCode.Created, (await Pesan(Req(r.RuanganId, "07:00", "07:30"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Pesan(Req(r.RuanganId, "08:00", "16:00"))).StatusCode);
    }

    [Fact]
    public async Task Waktu_lampau_ditolak() =>
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await Pesan(Req((await RuanganBaru()).RuanganId, "08:00", "09:00", tgl: DateOnly.FromDateTime(DateTime.Today.AddDays(-1))))).StatusCode);

    [Fact]
    public async Task Tanpa_idempotency_key_ditolak()
    {
        var r = await RuanganBaru();
        var res = await fx.Client().PostAsJsonAsync("api/reservasi", Req(r.RuanganId, "08:00", "09:00"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }

    [Fact]
    public async Task Overlap_ditolak_back_to_back_boleh_dan_cancel_membebaskan_slot()
    {
        var r = await RuanganBaru();
        var pertama = await Baca<ReservasiResponse>(await Pesan(Req(r.RuanganId, "08:00", "10:00")));

        var bentrok = await Pesan(Req(r.RuanganId, "09:30", "11:00"));
        Assert.Equal(HttpStatusCode.Conflict, bentrok.StatusCode);
        Assert.Contains(pertama.KodeReservasi, await bentrok.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, (await Pesan(Req(r.RuanganId, "10:00", "11:00"))).StatusCode);

        var cancel = await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Post, $"api/reservasi/{pertama.ReservasiId}/cancel", pertama.RowVersion));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("CANCELLED", (await Baca<ReservasiResponse>(cancel)).Status);

        Assert.Equal(HttpStatusCode.Created, (await Pesan(Req(r.RuanganId, "09:00", "10:00"))).StatusCode);
    }

    [Fact]
    public async Task Ubah_tanpa_if_match_428_dan_versi_lama_412()
    {
        var r = await RuanganBaru();
        var res = await Baca<ReservasiResponse>(await Pesan(Req(r.RuanganId, "08:00", "09:00")));

        var tanpa = await fx.Client().PutAsJsonAsync($"api/reservasi/{res.ReservasiId}", Req(r.RuanganId, "08:00", "09:30"));
        Assert.Equal((HttpStatusCode)428, tanpa.StatusCode);

        var ok = await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Put, $"api/reservasi/{res.ReservasiId}", res.RowVersion, Req(r.RuanganId, "08:00", "09:30")));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var usang = await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Put, $"api/reservasi/{res.ReservasiId}", res.RowVersion, Req(r.RuanganId, "08:00", "10:00")));
        Assert.Equal(HttpStatusCode.PreconditionFailed, usang.StatusCode);
    }

    [Fact]
    public async Task Ruangan_tidak_bisa_dinonaktifkan_atau_dikecilkan_jika_ada_reservasi_mendatang()
    {
        var r = await RuanganBaru(kapasitas: 10);
        await Pesan(Req(r.RuanganId, "08:00", "09:00", peserta: 8));

        var nonaktif = await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Put, $"api/ruangan/{r.RuanganId}", r.RowVersion,
            new RuanganRequest(r.KodeRuangan, r.NamaRuangan, r.Lokasi, r.Kapasitas, false)));
        Assert.Equal(HttpStatusCode.Conflict, nonaktif.StatusCode);

        var kecil = await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Put, $"api/ruangan/{r.RuanganId}", r.RowVersion,
            new RuanganRequest(r.KodeRuangan, r.NamaRuangan, r.Lokasi, 7, true)));
        Assert.Equal(HttpStatusCode.Conflict, kecil.StatusCode);

        var hapus = await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Delete, $"api/ruangan/{r.RuanganId}", r.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, hapus.StatusCode); // has reservation history
    }

    [Fact]
    public async Task Kode_ruangan_duplikat_409()
    {
        var r = await RuanganBaru();
        var res = await fx.Client().PostAsJsonAsync("api/ruangan", new RuanganRequest(r.KodeRuangan.ToLowerInvariant(), "X", "Y", 5));
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // ---------------------------------------------------------------- race conditions

    [Fact]
    public async Task Race_banyak_user_pesan_slot_bentrok_hanya_satu_berhasil()
    {
        var r = await RuanganBaru();
        // 30 users, each a different (but overlapping) slot, all at the same instant
        var hasil = await Serentak(30, i => Pesan(Req(r.RuanganId, $"09:{i % 30:00}", $"10:{i % 30:00}")));

        Assert.Equal(1, hasil.Count(h => h.StatusCode == HttpStatusCode.Created));
        Assert.All(hasil.Where(h => h.StatusCode != HttpStatusCode.Created), h => Assert.Equal(HttpStatusCode.Conflict, h.StatusCode));
        Assert.Equal(1, await HitungBooked(r.RuanganId));
    }

    [Fact]
    public async Task Race_double_submit_key_sama_hanya_satu_reservasi()
    {
        var r = await RuanganBaru();
        var key = Guid.NewGuid();
        var hasil = await Serentak(20, _ => Pesan(Req(r.RuanganId, "13:00", "14:00"), key));

        Assert.All(hasil, h => Assert.True(h.IsSuccessStatusCode, h.StatusCode.ToString()));
        Assert.Equal(1, hasil.Count(h => h.StatusCode == HttpStatusCode.Created));
        var ids = await Task.WhenAll(hasil.Select(async h => (await Baca<ReservasiResponse>(h)).ReservasiId));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await HitungBooked(r.RuanganId));
    }

    [Fact]
    public async Task Race_slot_berbeda_di_ruangan_sama_semua_berhasil()
    {
        var r = await RuanganBaru();
        // 16 non-overlapping 30-minute slots from 08:00: the lock serializes them but none may be lost
        var hasil = await Serentak(16, i =>
        {
            var mulai = new TimeOnly(8, 0).AddMinutes(30 * i);
            return Pesan(Req(r.RuanganId, mulai.ToString("HH:mm"), mulai.AddMinutes(30).ToString("HH:mm")));
        });
        Assert.All(hasil, h => Assert.Equal(HttpStatusCode.Created, h.StatusCode));
        Assert.Equal(16, await HitungBooked(r.RuanganId));
    }

    [Fact]
    public async Task Race_nonaktifkan_ruangan_bersamaan_dengan_reservasi_tidak_meninggalkan_booking_di_ruangan_nonaktif()
    {
        for (var ulang = 0; ulang < 5; ulang++)
        {
            var r = await RuanganBaru();
            var hasil = await Serentak(11, async i =>
            {
                if (i == 5)
                    return await fx.Client().SendAsync(DenganIfMatch(HttpMethod.Put, $"api/ruangan/{r.RuanganId}", r.RowVersion,
                        new RuanganRequest(r.KodeRuangan, r.NamaRuangan, r.Lokasi, r.Kapasitas, false)));
                var mulai = new TimeOnly(8, 0).AddHours(i);
                return await Pesan(Req(r.RuanganId, mulai.ToString("HH:mm"), mulai.AddMinutes(30).ToString("HH:mm")));
            });

            var nonaktifBerhasil = hasil[5].StatusCode == HttpStatusCode.OK;
            var booked = await HitungBooked(r.RuanganId);
            // invariant: an inactive room never ends up with an active booking
            Assert.False(nonaktifBerhasil && booked > 0, $"ruangan nonaktif tapi punya {booked} booking aktif");
            Assert.Equal(booked, hasil.Count(h => h.StatusCode == HttpStatusCode.Created));
        }
    }

    [Fact]
    public async Task Race_dua_admin_edit_reservasi_yang_sama_hanya_satu_menang()
    {
        var r = await RuanganBaru();
        var res = await Baca<ReservasiResponse>(await Pesan(Req(r.RuanganId, "08:00", "09:00")));
        var hasil = await Serentak(10, i => fx.Client().SendAsync(DenganIfMatch(HttpMethod.Put, $"api/reservasi/{res.ReservasiId}",
            res.RowVersion, Req(r.RuanganId, "08:00", $"09:{i:00}"))));

        Assert.Equal(1, hasil.Count(h => h.StatusCode == HttpStatusCode.OK));
        Assert.All(hasil.Where(h => h.StatusCode != HttpStatusCode.OK), h => Assert.Equal(HttpStatusCode.PreconditionFailed, h.StatusCode));
    }

    [Fact]
    public async Task Trigger_database_menolak_overlap_walau_aplikasi_dilewati()
    {
        var r = await RuanganBaru();
        await Pesan(Req(r.RuanganId, "08:00", "10:00"));

        await using var conn = new SqlConnection(fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT RESERVASI (RuanganId, NamaPemesan, JudulMeeting, Tanggal, JamMulai, JamSelesai, JumlahPeserta, IdempotencyKey)
            VALUES (@id, 'Bypass', 'Langsung ke DB', @tgl, '09:00', '11:00', 2, NEWID())
            """, conn);
        cmd.Parameters.AddWithValue("@id", r.RuanganId);
        cmd.Parameters.AddWithValue("@tgl", Hari.ToDateTime(TimeOnly.MinValue));
        var ex = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(50010, ex.Number);
    }
}
