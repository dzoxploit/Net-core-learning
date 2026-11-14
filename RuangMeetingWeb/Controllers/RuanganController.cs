using Microsoft.AspNetCore.Mvc;
using RuangMeetingWeb.Models;
using RuangMeetingWeb.Services;

namespace RuangMeetingWeb.Controllers;

/// <summary>Room master data form + grid.</summary>
public class RuanganController(RuangMeetingApiClient api) : AppController
{
    public async Task<IActionResult> Index(int? id, CancellationToken ct)
    {
        var form = new RuanganForm();
        if (id is not null && await api.AmbilRuanganAsync(id.Value, ct) is { Ok: true, Data: { } r })
        {
            form = new RuanganForm
            {
                RuanganId = r.RuanganId, RowVersion = r.RowVersion, KodeRuangan = r.KodeRuangan,
                NamaRuangan = r.NamaRuangan, Lokasi = r.Lokasi, Kapasitas = r.Kapasitas, IsAktif = r.IsAktif,
            };
        }
        return View(await BuildVm(form, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Save(RuanganForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, ct));
        var h = await api.BuatRuanganAsync(form.ToRequest(), ct);
        if (h.Ok)
        {
            Sukses($"Ruangan {h.Data!.KodeRuangan} berhasil disimpan.");
            return RedirectToAction(nameof(Index));
        }
        TampilkanError(h);
        return View("Index", await BuildVm(form, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(RuanganForm form, CancellationToken ct)
    {
        if (form.RuanganId is null || string.IsNullOrEmpty(form.RowVersion))
        {
            Gagal("Pilih ruangan pada tabel terlebih dahulu.");
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, ct));

        var h = await api.UbahRuanganAsync(form.RuanganId.Value, form.ToRequest(), form.RowVersion, ct);
        return Hasil(h, form.RuanganId.Value, $"Ruangan {form.KodeRuangan} berhasil diubah.")
               ?? View("Index", await BuildVm(form, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(RuanganForm form, CancellationToken ct)
    {
        if (form.RuanganId is null || string.IsNullOrEmpty(form.RowVersion))
        {
            Gagal("Pilih ruangan pada tabel terlebih dahulu.");
            return RedirectToAction(nameof(Index));
        }
        var h = await api.HapusRuanganAsync(form.RuanganId.Value, form.RowVersion, ct);
        if (!h.Ok && !VersiUsang(h) && !TidakAda(h))
        {
            Gagal(Pesan(h));
            return RedirectToAction(nameof(Index), new { id = form.RuanganId });
        }
        return Hasil(h, form.RuanganId.Value, $"Ruangan {form.KodeRuangan} dihapus.")!;
    }

    private IActionResult? Hasil<T>(ApiHasil<T> h, int id, string pesanSukses)
    {
        if (h.Ok) { Sukses(pesanSukses); return RedirectToAction(nameof(Index)); }
        if (VersiUsang(h)) { Gagal(h.Pesan!); return RedirectToAction(nameof(Index), new { id }); }
        if (TidakAda(h)) { Gagal(h.Pesan!); return RedirectToAction(nameof(Index)); }
        TampilkanError(h);
        return null;
    }

    private async Task<RuanganPageVm> BuildVm(RuanganForm form, CancellationToken ct)
    {
        var daftar = await api.DaftarRuanganAsync(null, ct);
        if (!daftar.Ok) ModelState.AddModelError("", Pesan(daftar));
        return new RuanganPageVm { Form = form, Daftar = daftar.Data ?? [] };
    }
}
