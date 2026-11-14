using Microsoft.AspNetCore.Mvc;
using RuangMeetingApi.Contracts;
using RuangMeetingApi.Infrastructure;
using RuangMeetingApi.Services;

namespace RuangMeetingApi.Controllers;

[Route("api/ruangan")]
public class RuanganController(RuanganService service) : ApiControllerBase
{
    /// <summary>List rooms; ?aktif=true for bookable rooms only.</summary>
    [HttpGet]
    public async Task<ActionResult<List<RuanganResponse>>> Daftar([FromQuery] bool? aktif, CancellationToken ct) =>
        await service.DaftarAsync(aktif, ct);

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Ambil(int id, CancellationToken ct) =>
        Jawab(await service.AmbilAsync(id, ct), r => { SetETag(r.RowVersion); return Ok(r); });

    [HttpPost]
    public async Task<IActionResult> Buat(RuanganRequest req, CancellationToken ct) =>
        Jawab(await service.BuatAsync(req, ct), r => { SetETag(r.RowVersion); return CreatedAtAction(nameof(Ambil), new { id = r.RuanganId }, r); });

    /// <summary>Update. Requires If-Match: "rowVersion".</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Ubah(int id, RuanganRequest req, CancellationToken ct)
    {
        if (!TryIfMatch(out var rowVer, out var err)) return err!;
        return Jawab(await service.UbahAsync(id, req, rowVer, ct), r => { SetETag(r.RowVersion); return Ok(r); });
    }

    /// <summary>Delete (only rooms without reservation history). Requires If-Match.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Hapus(int id, CancellationToken ct)
    {
        if (!TryIfMatch(out var rowVer, out var err)) return err!;
        return Jawab(await service.HapusAsync(id, rowVer, ct), _ => NoContent());
    }
}
