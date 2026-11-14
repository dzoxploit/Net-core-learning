using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Parkir.Data;
using Parkir.Models;
using Parkir.Services;

namespace Parkir.Controllers;

public class ParkirController(ParkirDbContext db, TransaksiService service) : Controller
{
    /// <summary>Form + grid. With id, the selected row is loaded into the form (Edit/Delete).</summary>
    public async Task<IActionResult> Index(long? id, CancellationToken ct)
    {
        TransaksiForm form;
        if (id is not null && await db.Transaksi.AsNoTracking().FirstOrDefaultAsync(t => t.TransaksiId == id, ct) is { } t)
        {
            form = new TransaksiForm
            {
                TransaksiId = t.TransaksiId,
                NoTransaksi = t.NoTransaksi,
                RowVer = Convert.ToBase64String(t.RowVer),
                IdempotencyKey = Guid.NewGuid(),
                NoPolisi = t.NoPolisi,
                TarifId = t.TarifId,
                TanggalMasuk = DateOnly.FromDateTime(t.JamMasuk),
                JamMasuk = TimeOnly.FromDateTime(t.JamMasuk),
                TanggalKeluar = DateOnly.FromDateTime(t.JamKeluar),
                JamKeluar = TimeOnly.FromDateTime(t.JamKeluar),
            };
        }
        else
        {
            var now = DateTime.Now;
            var keluar = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            var masuk = keluar.AddHours(-1);
            form = new TransaksiForm
            {
                IdempotencyKey = Guid.NewGuid(),
                TarifId = await db.Tarif.Where(x => x.IsAktif).OrderBy(x => x.TarifId).Select(x => x.TarifId).FirstOrDefaultAsync(ct),
                TanggalMasuk = DateOnly.FromDateTime(masuk), JamMasuk = TimeOnly.FromDateTime(masuk),
                TanggalKeluar = DateOnly.FromDateTime(keluar), JamKeluar = TimeOnly.FromDateTime(keluar),
            };
        }
        return View(await BuildVm(form, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TransaksiForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, ct));

        var hasil = await service.SimpanAsync(ToInput(form), ct);
        if (hasil.Ok)
        {
            TempData["Sukses"] = hasil.Pesan;
            return RedirectToAction(nameof(Index)); // PRG: refresh can't resubmit; new form gets a new key
        }
        ModelState.AddModelError("", hasil.Pesan);
        return View("Index", await BuildVm(form, ct)); // same IdempotencyKey kept for a safe retry
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(TransaksiForm form, CancellationToken ct)
    {
        if (form.TransaksiId is null || !TryRowVer(form.RowVer, out var rowVer))
        {
            TempData["Error"] = "Pilih data pada tabel terlebih dahulu.";
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, ct));

        var hasil = await service.UbahAsync(form.TransaksiId.Value, rowVer, ToInput(form), ct);
        return HasilKeRedirect(hasil, form.TransaksiId.Value) ?? View("Index", await BuildVm(form, ct, hasil.Pesan));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(TransaksiForm form, CancellationToken ct)
    {
        if (form.TransaksiId is null || !TryRowVer(form.RowVer, out var rowVer))
        {
            TempData["Error"] = "Pilih data pada tabel terlebih dahulu.";
            return RedirectToAction(nameof(Index));
        }
        var hasil = await service.HapusAsync(form.TransaksiId.Value, rowVer, ct);
        return HasilKeRedirect(hasil, form.TransaksiId.Value) ?? RedirectToAction(nameof(Index));
    }

    /// <summary>Live preview of Durasi / Bayar for the form (same rules as on save).</summary>
    [HttpGet]
    public async Task<IActionResult> Hitung(int tarifId, DateTime masuk, DateTime keluar, CancellationToken ct)
    {
        var tarif = await db.Tarif.AsNoTracking().FirstOrDefaultAsync(t => t.TarifId == tarifId, ct);
        if (tarif is null) return NotFound();
        if (keluar < masuk) return BadRequest(new { pesan = "Jam keluar lebih awal dari jam masuk" });
        var h = KalkulatorTarif.Hitung(masuk, keluar, tarif);
        return Json(new { durasi = h.DurasiTeks, jamDitagih = h.JamDitagih, bayar = h.Bayar });
    }

    // ------------------------------------------------------------------

    private IActionResult? HasilKeRedirect(HasilService hasil, long id)
    {
        switch (hasil.Status)
        {
            case StatusHasil.Ok:
                TempData["Sukses"] = hasil.Pesan;
                return RedirectToAction(nameof(Index));
            case StatusHasil.Konflik:
                TempData["Error"] = hasil.Pesan;
                return RedirectToAction(nameof(Index), new { id }); // reload the latest version
            case StatusHasil.TidakDitemukan:
                TempData["Error"] = hasil.Pesan;
                return RedirectToAction(nameof(Index));
            default:
                return null; // validation / duplicate: re-render with the user's input
        }
    }

    private async Task<ParkirPageVm> BuildVm(TransaksiForm form, CancellationToken ct, string? error = null)
    {
        if (error is not null) ModelState.AddModelError("", error);

        var vm = new ParkirPageVm
        {
            Form = form,
            DaftarTarif = await db.Tarif.AsNoTracking().OrderBy(t => t.TarifId).ToListAsync(ct),
            DaftarTransaksi = await db.Transaksi.AsNoTracking().Include(t => t.Tarif)
                .OrderByDescending(t => t.JamMasuk).ThenByDescending(t => t.TransaksiId).Take(200).ToListAsync(ct),
        };
        var tarif = vm.DaftarTarif.FirstOrDefault(t => t.TarifId == form.TarifId);
        if (tarif is not null && form.Keluar >= form.Masuk)
        {
            var h = KalkulatorTarif.Hitung(form.Masuk, form.Keluar, tarif);
            vm.Durasi = h.DurasiTeks;
            vm.Bayar = h.Bayar;
        }
        return vm;
    }

    private static TransaksiInput ToInput(TransaksiForm f) => new(f.NoPolisi, f.TarifId, f.Masuk, f.Keluar, f.IdempotencyKey);

    private static bool TryRowVer(string? s, out byte[] rowVer)
    {
        rowVer = [];
        if (string.IsNullOrEmpty(s)) return false;
        try { rowVer = Convert.FromBase64String(s); return rowVer.Length == 8; }
        catch (FormatException) { return false; }
    }
}
