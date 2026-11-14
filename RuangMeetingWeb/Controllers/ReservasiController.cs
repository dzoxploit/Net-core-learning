using Microsoft.AspNetCore.Mvc;
using RuangMeetingWeb.Models;
using RuangMeetingWeb.Services;

namespace RuangMeetingWeb.Controllers;

/// <summary>Reservation form + grid. All POSTs are CSRF-protected by the global AutoValidateAntiforgeryToken filter.</summary>
public class ReservasiController(RuangMeetingApiClient api) : AppController
{
    public async Task<IActionResult> Index(long? id, DateOnly? tanggal, CancellationToken ct)
    {
        ReservasiForm form;
        if (id is not null && await api.AmbilReservasiAsync(id.Value, ct) is { Ok: true, Data: { } r })
        {
            form = new ReservasiForm
            {
                ReservasiId = r.ReservasiId, KodeReservasi = r.KodeReservasi, RowVersion = r.RowVersion,
                IdempotencyKey = Guid.NewGuid(),
                RuanganId = r.RuanganId, NamaPemesan = r.NamaPemesan, JudulMeeting = r.JudulMeeting,
                Tanggal = r.Tanggal, JamMulai = r.JamMulai, JamSelesai = r.JamSelesai,
                JumlahPeserta = r.JumlahPeserta, Status = r.Status,
            };
        }
        else
        {
            var mulai = DateTime.Now.AddHours(1);
            form = new ReservasiForm
            {
                IdempotencyKey = Guid.NewGuid(),
                Tanggal = DateOnly.FromDateTime(mulai),
                JamMulai = new TimeOnly(mulai.Hour, 0),
                JamSelesai = new TimeOnly(mulai.Hour, 0).AddHours(1),
            };
        }
        return View(await BuildVm(form, tanggal, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Save(ReservasiForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, null, ct));

        var h = await api.BuatReservasiAsync(form.ToRequest(baru: true), form.IdempotencyKey, ct);
        if (h.Ok)
        {
            Sukses($"Reservasi {h.Data!.KodeReservasi} untuk {h.Data.NamaRuangan} berhasil disimpan.");
            return RedirectToAction(nameof(Index)); // PRG; the next form gets a fresh Idempotency-Key
        }
        TampilkanError(h);
        return View("Index", await BuildVm(form, null, ct)); // same key kept: a retry can't double-book
    }

    [HttpPost]
    public async Task<IActionResult> Edit(ReservasiForm form, CancellationToken ct)
    {
        if (form.ReservasiId is null || string.IsNullOrEmpty(form.RowVersion))
        {
            Gagal("Pilih reservasi pada tabel terlebih dahulu.");
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, null, ct));

        var h = await api.UbahReservasiAsync(form.ReservasiId.Value, form.ToRequest(baru: false), form.RowVersion, ct);
        return Hasil(h, form.ReservasiId.Value, $"Reservasi {form.KodeReservasi} berhasil diubah.")
               ?? View("Index", await BuildVm(form, null, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(ReservasiForm form, CancellationToken ct)
    {
        if (form.ReservasiId is null || string.IsNullOrEmpty(form.RowVersion))
        {
            Gagal("Pilih reservasi pada tabel terlebih dahulu.");
            return RedirectToAction(nameof(Index));
        }
        var h = await api.BatalkanReservasiAsync(form.ReservasiId.Value, form.RowVersion, ct);
        if (!h.Ok && !VersiUsang(h) && !TidakAda(h))
        {
            Gagal(Pesan(h));
            return RedirectToAction(nameof(Index), new { id = form.ReservasiId });
        }
        return Hasil(h, form.ReservasiId.Value, $"Reservasi {form.KodeReservasi} dibatalkan.")!;
    }

    private IActionResult? Hasil(ApiHasil<ReservasiDto> h, long id, string pesanSukses)
    {
        if (h.Ok) { Sukses(pesanSukses); return RedirectToAction(nameof(Index)); }
        if (VersiUsang(h)) { Gagal(h.Pesan!); return RedirectToAction(nameof(Index), new { id }); } // reload latest version
        if (TidakAda(h)) { Gagal(h.Pesan!); return RedirectToAction(nameof(Index)); }
        TampilkanError(h);
        return null;
    }

    private async Task<ReservasiPageVm> BuildVm(ReservasiForm form, DateOnly? filter, CancellationToken ct)
    {
        var ruang = await api.DaftarRuanganAsync(null, ct);
        var daftar = await api.DaftarReservasiAsync(filter, ct);
        if (!ruang.Ok) ModelState.AddModelError("", Pesan(ruang));
        else if (!daftar.Ok) ModelState.AddModelError("", Pesan(daftar));

        if (form.RuanganId == 0)
            form.RuanganId = ruang.Data?.FirstOrDefault(r => r.IsAktif)?.RuanganId ?? 0;

        return new ReservasiPageVm
        {
            Form = form,
            Ruangan = ruang.Data ?? [],
            Daftar = daftar.Data ?? [],
            Filter = filter,
        };
    }
}
