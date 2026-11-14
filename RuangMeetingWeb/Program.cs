using Microsoft.AspNetCore.Mvc;
using RuangMeetingWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- CSRF: every unsafe request (POST/PUT/DELETE) must carry a valid antiforgery token
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "__RM.Csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;              // not sent on cross-site requests at all
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.HeaderName = "X-CSRF-TOKEN";                         // for any future fetch()/AJAX POSTs
});
builder.Services.Configure<CookieTempDataProviderOptions>(o =>
{
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.HttpOnly = true;
});

// ---- API client: base address + API key + correlation id forwarding
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiHeadersHandler>();
builder.Services.AddHttpClient<RuangMeetingApiClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl belum diisi."));
    c.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<ApiHeadersHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// security headers: no framing (clickjacking), no MIME sniffing, limited referrer
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XFrameOptions = "DENY";
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "same-origin";
    h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'; " +
        "style-src 'self' https://fonts.googleapis.com https://cdnjs.cloudflare.com; " +
        "font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; script-src 'self'";
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Reservasi}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
