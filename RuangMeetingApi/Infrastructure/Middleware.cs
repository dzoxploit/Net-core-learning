using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace RuangMeetingApi.Infrastructure;

/// <summary>
/// Takes X-Correlation-ID from the caller (the web app forwards its own request id) or creates one,
/// echoes it in the response and attaches it to every log line, so one user action can be
/// traced from the web log through the API log.
/// </summary>
public partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext ctx)
    {
        var incoming = ctx.Request.Headers[Header].ToString();
        var id = SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        ctx.TraceIdentifier = id;
        ctx.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id))
            await next(ctx);
    }

    // don't let callers inject arbitrary text into logs/headers
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex SafeId();
}

/// <summary>
/// Every /api call must carry X-Api-Key. The key lives only on servers (the web app), never in a browser,
/// and the API uses no cookies, so a malicious page can't make a victim's browser call it (no CSRF surface).
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> log)
{
    public const string Header = "X-Api-Key";
    private readonly byte[] _key = Encoding.UTF8.GetBytes(config["Api:Key"] ?? throw new InvalidOperationException("Api:Key belum diisi."));

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/health"))
        {
            var given = Encoding.UTF8.GetBytes(ctx.Request.Headers[Header].ToString());
            if (!CryptographicOperations.FixedTimeEquals(given, _key))
            {
                log.LogWarning("Ditolak: API key tidak valid dari {RemoteIp} untuk {Method} {Path}",
                    ctx.Connection.RemoteIpAddress, ctx.Request.Method, ctx.Request.Path);
                await Results.Problem(statusCode: 401, title: "Unauthorized", detail: "API key tidak valid.").ExecuteAsync(ctx);
                return;
            }
        }
        await next(ctx);
    }
}

/// <summary>Unhandled exceptions: full details go to the log, the client gets a ProblemDetails with the correlation id.</summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
        {
            log.LogInformation("Request dibatalkan oleh client: {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            return true;
        }
        log.LogError(ex, "Unhandled exception pada {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "Terjadi kesalahan pada server",
                Detail = "Silakan coba lagi. Sertakan correlationId saat melapor.",
            },
        });
    }
}
