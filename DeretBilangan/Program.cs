var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews(o =>
{
    // Indonesian messages for input that isn't a number
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v, f) => $"Nilai '{v}' tidak valid untuk {f}.");
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(f => $"{f} harus berupa angka.");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
