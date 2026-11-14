using Microsoft.AspNetCore.Mvc;
using RuangMeetingApi.Services;

namespace RuangMeetingApi.Infrastructure;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Maps a service outcome to HTTP (ProblemDetails for failures).</summary>
    protected IActionResult Jawab<T>(Hasil<T> h, Func<T, IActionResult> sukses)
    {
        if (h.Ok) return sukses(h.Data!);

        var status = h.Gagal switch
        {
            JenisGagal.Validasi => StatusCodes.Status422UnprocessableEntity,
            JenisGagal.TidakDitemukan => StatusCodes.Status404NotFound,
            JenisGagal.Konflik => StatusCodes.Status409Conflict,
            JenisGagal.VersiUsang => StatusCodes.Status412PreconditionFailed,
            _ => StatusCodes.Status400BadRequest,
        };
        if (h.Errors is { Count: > 0 })
        {
            var ms = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var (field, msgs) in h.Errors)
                foreach (var m in msgs) ms.AddModelError(field, m);
            // via the factory so correlationId is added like on every other problem response
            var vp = ProblemDetailsFactory.CreateValidationProblemDetails(HttpContext, ms, status, h.Pesan);
            return new ObjectResult(vp) { StatusCode = status };
        }
        return Problem(statusCode: status, title: h.Pesan);
    }

    /// <summary>
    /// Reads the row version from If-Match (ETag). Missing -> 428, so a client can't
    /// accidentally overwrite without saying which version it edited.
    /// </summary>
    protected bool TryIfMatch(out byte[] rowVer, out IActionResult? error)
    {
        rowVer = [];
        error = null;
        var raw = Request.Headers.IfMatch.ToString().Trim();
        if (raw.StartsWith("W/")) raw = raw[2..];
        raw = raw.Trim('"');
        try
        {
            if (raw.Length > 0) rowVer = Convert.FromBase64String(raw);
        }
        catch (FormatException) { }

        if (rowVer.Length == 8) return true;
        error = Problem(statusCode: StatusCodes.Status428PreconditionRequired,
            title: "Header If-Match (rowVersion) wajib diisi untuk mengubah data.");
        return false;
    }

    protected void SetETag(string rowVersion) => Response.Headers.ETag = $"\"{rowVersion}\"";
}
