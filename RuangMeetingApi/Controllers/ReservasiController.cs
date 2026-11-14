using Microsoft.AspNetCore.Mvc;
using RuangMeetingApi.Contracts;
using RuangMeetingApi.Infrastructure;
using RuangMeetingApi.Services;

namespace RuangMeetingApi.Controllers;

[Route("api/reservasi")]
public class ReservasiController(ReservasiService service) : ApiControllerBase
{
    /// <summary>Reservation history; optional filters ?tanggal=2026-11-20&amp;ruanganId=1.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReservasiResponse>>> Daftar([FromQuery] DateOnly? tanggal, [FromQuery] int? ruanganId, CancellationToken ct) =>
        await service.DaftarAsync(tanggal, ruanganId, ct);

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Ambil(long id, CancellationToken ct) =>
        Jawab(await service.AmbilAsync(id, ct), r => { SetETag(r.RowVersion); return Ok(r); });

    /// <summary>
    /// Create. Requires header Idempotency-Key (GUID): repeating the request with the same key
    /// returns the reservation already created (200) instead of creating a second one (201).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Buat(ReservasiRequest req, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, CancellationToken ct)
    {
        var hasil = await service.BuatAsync(req, idempotencyKey ?? Guid.Empty, ct);
        return Jawab(hasil, r =>
        {
            SetETag(r.RowVersion);
            if (hasil.Replay) { Response.Headers["Idempotent-Replayed"] = "true"; return Ok(r); }
            return CreatedAtAction(nameof(Ambil), new { id = r.ReservasiId }, r);
        });
    }

    /// <summary>Update (incl. status). Requires If-Match.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Ubah(long id, ReservasiRequest req, CancellationToken ct)
    {
        if (!TryIfMatch(out var rowVer, out var err)) return err!;
        return Jawab(await service.UbahAsync(id, req, rowVer, ct), r => { SetETag(r.RowVersion); return Ok(r); });
    }

    /// <summary>Cancel (status CANCELLED, history kept). Requires If-Match.</summary>
    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Batalkan(long id, CancellationToken ct)
    {
        if (!TryIfMatch(out var rowVer, out var err)) return err!;
        return Jawab(await service.BatalkanAsync(id, rowVer, ct), r => { SetETag(r.RowVersion); return Ok(r); });
    }
}
