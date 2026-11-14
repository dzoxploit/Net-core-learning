using Microsoft.EntityFrameworkCore;
using RuangMeetingApi.Data;
using RuangMeetingApi.Infrastructure;
using RuangMeetingApi.Services;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---- logging: Serilog (console + rolling text file + rolling JSON file), configured in appsettings.json
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default belum diisi.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RuanganService>();
builder.Services.AddScoped<ReservasiService>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// every error response is RFC 7807 ProblemDetails carrying the correlation id
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    c.ProblemDetails.Extensions["correlationId"] = c.HttpContext.TraceIdentifier;
    c.ProblemDetails.Instance ??= $"{c.HttpContext.Request.Method} {c.HttpContext.Request.Path}";
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

await DatabaseInitializer.EnsureAsync(connectionString, Path.Combine(AppContext.BaseDirectory, "Database", "RuangMeetingDb.sql"));

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(o =>
{
    o.MessageTemplate = "HTTP {RequestMethod} {RequestPath}{QueryString} -> {StatusCode} in {Elapsed:0.0} ms";
    o.EnrichDiagnosticContext = (diag, http) =>
    {
        diag.Set("QueryString", http.Request.QueryString.Value ?? "");
        diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString() ?? "");
        diag.Set("UserAgent", http.Request.Headers.UserAgent.ToString());
    };
    o.GetLevel = (http, _, ex) =>
        ex is not null || http.Response.StatusCode >= 500 ? LogEventLevel.Error
        : http.Response.StatusCode >= 400 ? LogEventLevel.Warning
        : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<ApiKeyMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Problem(statusCode: 503, title: "Database tidak tersedia"));
app.MapControllers();

app.Run();

public partial class Program; // for WebApplicationFactory in tests
