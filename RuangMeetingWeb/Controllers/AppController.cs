using System.Net;
using Microsoft.AspNetCore.Mvc;
using RuangMeetingWeb.Services;

namespace RuangMeetingWeb.Controllers;

public abstract class AppController : Controller
{
    /// <summary>Puts API errors into ModelState: field errors next to the fields, the title in the summary.</summary>
    protected void TampilkanError<T>(ApiHasil<T> h)
    {
        var pesanField = new HashSet<string>();
        foreach (var (field, msgs) in h.Errors ?? [])
        {
            foreach (var m in msgs)
            {
                // "$.jamMulai" style keys come from JSON binding: show them in the summary
                ModelState.AddModelError(field.StartsWith('$') ? "" : $"Form.{field}", m);
                pesanField.Add(m);
            }
        }
        if (h.Pesan is not null && !pesanField.Contains(h.Pesan))
            ModelState.AddModelError("", Pesan(h));
    }

    /// <summary>For server errors show the correlation id so the user can quote it and we can find the log lines.</summary>
    protected static string Pesan<T>(ApiHasil<T> h) =>
        (int)h.Status >= 500 && h.CorrelationId is not null ? $"{h.Pesan} (ID: {h.CorrelationId})" : h.Pesan ?? "Terjadi kesalahan.";

    protected void Sukses(string pesan) => TempData["Sukses"] = pesan;
    protected void Gagal(string pesan) => TempData["Error"] = pesan;

    protected static bool VersiUsang<T>(ApiHasil<T> h) => h.Status == HttpStatusCode.PreconditionFailed;
    protected static bool TidakAda<T>(ApiHasil<T> h) => h.Status == HttpStatusCode.NotFound;
}
