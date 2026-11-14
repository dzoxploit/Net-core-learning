using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Parkir.Data;
using Parkir.Models;

namespace Parkir.Controllers;

/// <summary>Rate/policy maintenance (TARIF). Updates use ROWVERSION so two admins can't overwrite each other.</summary>
public class TarifController(ParkirDbContext db) : Controller
{
    public async Task<IActionResult> Index(int? id, CancellationToken ct)
    {
        var form = new TarifForm();
        if (id is not null && await db.Tarif.AsNoTracking().FirstOrDefaultAsync(t => t.TarifId == id, ct) is { } t)
        {
            form = new TarifForm
            {
                TarifId = t.TarifId, RowVer = Convert.ToBase64String(t.RowVer),
                JenisKendaraan = t.JenisKendaraan, Tarif1 = t.Tarif1, Tarif2 = t.Tarif2,
                JamTarif1 = t.JamTarif1, MenitGratis = t.MenitGratis, MenitToleransi = t.MenitToleransi, IsAktif = t.IsAktif,
            };
        }
        return View(await BuildVm(form, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Simpan(TarifForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Index", await BuildVm(form, ct));

        Tarif entity;
        if (form.TarifId is null)
        {
            entity = new Tarif();
            db.Tarif.Add(entity);
        }
        else
        {
            var found = await db.Tarif.FirstOrDefaultAsync(t => t.TarifId == form.TarifId, ct);
            if (found is null)
            {
                TempData["Error"] = "Tarif tidak ditemukan.";
                return RedirectToAction(nameof(Index));
            }
            entity = found;
            db.Entry(entity).Property(t => t.RowVer).OriginalValue = Convert.FromBase64String(form.RowVer ?? "");
        }

        entity.JenisKendaraan = form.JenisKendaraan.Trim();
        entity.Tarif1 = form.Tarif1;
        entity.Tarif2 = form.Tarif2;
        entity.JamTarif1 = form.JamTarif1;
        entity.MenitGratis = form.MenitGratis;
        entity.MenitToleransi = form.MenitToleransi;
        entity.IsAktif = form.IsAktif;
        entity.UpdatedAt = DateTime.Now;

        try
        {
            await db.SaveChangesAsync(ct);
            TempData["Sukses"] = $"Tarif {entity.JenisKendaraan} berhasil disimpan.";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] = "Tarif ini sudah diubah oleh pengguna lain. Data terbaru sudah dimuat, silakan ulangi.";
            return RedirectToAction(nameof(Index), new { id = form.TarifId });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            ModelState.AddModelError(nameof(TarifForm.JenisKendaraan), "Jenis kendaraan sudah ada.");
            return View("Index", await BuildVm(form, ct));
        }
    }

    private async Task<TarifPageVm> BuildVm(TarifForm form, CancellationToken ct) => new()
    {
        Form = form,
        DaftarTarif = await db.Tarif.AsNoTracking().OrderBy(t => t.TarifId).ToListAsync(ct),
    };
}
