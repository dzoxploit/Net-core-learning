using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RuangMeetingWeb.Services;

// ---- API contracts (the web app is a separate deployable, so it keeps its own copy)
public record RuanganDto(int RuanganId, string KodeRuangan, string NamaRuangan, string Lokasi, int Kapasitas, bool IsAktif, string RowVersion);
public record RuanganRequest(string KodeRuangan, string NamaRuangan, string Lokasi, int Kapasitas, bool IsAktif);

public record ReservasiDto(
    long ReservasiId, string KodeReservasi, int RuanganId, string KodeRuangan, string NamaRuangan, int Kapasitas,
    string NamaPemesan, string JudulMeeting, DateOnly Tanggal, TimeOnly JamMulai, TimeOnly JamSelesai,
    int JumlahPeserta, string Status, string RowVersion);
public record ReservasiRequest(int RuanganId, string NamaPemesan, string JudulMeeting, DateOnly Tanggal,
    TimeOnly JamMulai, TimeOnly JamSelesai, int JumlahPeserta, string? Status);

public record ApiHasil<T>(HttpStatusCode Status, T? Data, string? Pesan = null,
    Dictionary<string, string[]>? Errors = null, string? CorrelationId = null)
{
    public bool Ok => (int)Status is >= 200 and < 300;
}

/// <summary>
/// Adds the API key and forwards the current request id as X-Correlation-ID, so a web request
/// and the API calls it caused share one id in the logs.
/// </summary>
public class ApiHeadersHandler(IHttpContextAccessor accessor, IConfiguration config) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("X-Api-Key", config["Api:Key"]);
        if (accessor.HttpContext?.TraceIdentifier is { } id)
            request.Headers.Add("X-Correlation-ID", id);
        return base.SendAsync(request, ct);
    }
}

/// <summary>Typed client for RuangMeetingApi. Never throws for HTTP errors; returns ApiHasil instead.</summary>
public class RuangMeetingApiClient(HttpClient http, ILogger<RuangMeetingApiClient> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- rooms
    public Task<ApiHasil<List<RuanganDto>>> DaftarRuanganAsync(bool? aktif = null, CancellationToken ct = default) =>
        Kirim<List<RuanganDto>>(new(HttpMethod.Get, aktif is null ? "api/ruangan" : $"api/ruangan?aktif={aktif}"), ct);

    public Task<ApiHasil<RuanganDto>> AmbilRuanganAsync(int id, CancellationToken ct = default) =>
        Kirim<RuanganDto>(new(HttpMethod.Get, $"api/ruangan/{id}"), ct);

    public Task<ApiHasil<RuanganDto>> BuatRuanganAsync(RuanganRequest req, CancellationToken ct = default) =>
        Kirim<RuanganDto>(new(HttpMethod.Post, "api/ruangan") { Content = JsonContent.Create(req, options: Json) }, ct);

    public Task<ApiHasil<RuanganDto>> UbahRuanganAsync(int id, RuanganRequest req, string rowVersion, CancellationToken ct = default) =>
        Kirim<RuanganDto>(IfMatch(new(HttpMethod.Put, $"api/ruangan/{id}") { Content = JsonContent.Create(req, options: Json) }, rowVersion), ct);

    public Task<ApiHasil<object>> HapusRuanganAsync(int id, string rowVersion, CancellationToken ct = default) =>
        Kirim<object>(IfMatch(new(HttpMethod.Delete, $"api/ruangan/{id}"), rowVersion), ct);

    // ---- reservations
    public Task<ApiHasil<List<ReservasiDto>>> DaftarReservasiAsync(DateOnly? tanggal = null, CancellationToken ct = default) =>
        Kirim<List<ReservasiDto>>(new(HttpMethod.Get, tanggal is null ? "api/reservasi" : $"api/reservasi?tanggal={tanggal:yyyy-MM-dd}"), ct);

    public Task<ApiHasil<ReservasiDto>> AmbilReservasiAsync(long id, CancellationToken ct = default) =>
        Kirim<ReservasiDto>(new(HttpMethod.Get, $"api/reservasi/{id}"), ct);

    public Task<ApiHasil<ReservasiDto>> BuatReservasiAsync(ReservasiRequest req, Guid idempotencyKey, CancellationToken ct = default)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "api/reservasi") { Content = JsonContent.Create(req, options: Json) };
        msg.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
        return Kirim<ReservasiDto>(msg, ct);
    }

    public Task<ApiHasil<ReservasiDto>> UbahReservasiAsync(long id, ReservasiRequest req, string rowVersion, CancellationToken ct = default) =>
        Kirim<ReservasiDto>(IfMatch(new(HttpMethod.Put, $"api/reservasi/{id}") { Content = JsonContent.Create(req, options: Json) }, rowVersion), ct);

    public Task<ApiHasil<ReservasiDto>> BatalkanReservasiAsync(long id, string rowVersion, CancellationToken ct = default) =>
        Kirim<ReservasiDto>(IfMatch(new(HttpMethod.Post, $"api/reservasi/{id}/cancel"), rowVersion), ct);

    // ---- plumbing

    private static HttpRequestMessage IfMatch(HttpRequestMessage msg, string rowVersion)
    {
        msg.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{rowVersion}\""));
        return msg;
    }

    private async Task<ApiHasil<T>> Kirim<T>(HttpRequestMessage msg, CancellationToken ct)
    {
        try
        {
            using var res = await http.SendAsync(msg, ct);
            var corr = res.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.FirstOrDefault() : null;

            if (res.IsSuccessStatusCode)
            {
                var data = res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<T>(Json, ct);
                return new(res.StatusCode, data, CorrelationId: corr);
            }

            var problem = await BacaProblemAsync(res, ct);
            log.LogWarning("API {Method} {Uri} -> {Status}: {Title} (correlation {CorrelationId})",
                msg.Method, msg.RequestUri, (int)res.StatusCode, problem?.Title, corr);
            return new(res.StatusCode, default, problem?.Title ?? $"API error {(int)res.StatusCode}", problem?.Errors, corr);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogError(ex, "API tidak dapat dihubungi: {Method} {Uri}", msg.Method, msg.RequestUri);
            return new(HttpStatusCode.ServiceUnavailable, default, "Layanan API tidak dapat dihubungi. Silakan coba lagi.");
        }
    }

    private record Problem(string? Title, Dictionary<string, string[]>? Errors);

    private static async Task<Problem?> BacaProblemAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return await res.Content.ReadFromJsonAsync<Problem>(Json, ct); }
        catch (JsonException) { return null; }
    }
}
