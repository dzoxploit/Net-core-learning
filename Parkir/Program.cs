using Microsoft.EntityFrameworkCore;
using Parkir.Data;
using Parkir.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default belum diisi.");

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ParkirDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddScoped<TransaksiService>();

var app = builder.Build();

// Create the database / tables / seed rates if they don't exist yet.
await DatabaseInitializer.EnsureAsync(connectionString, Path.Combine(app.Environment.ContentRootPath, "Database", "ParkirDb.sql"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Parkir}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program; // for integration tests
