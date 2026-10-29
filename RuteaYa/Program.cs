
using Microsoft.AspNetCore.Authentication.Cookies;
using RuteaYa.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient<IUsuariosApiServicio, UsuariosApiServicio>(
    client =>
    {
        var url = builder.Configuration["Servicios:UsuariosApi"]
            ?? throw new InvalidOperationException(
                "No se configuró la URL de la API.");

        client.BaseAddress = new Uri(url);
        client.Timeout = TimeSpan.FromSeconds(15);
    });

builder.Services.AddHttpClient<IViajesApiServicio, ViajesApiServicio>(
    client =>
    {
        var url = builder.Configuration["Servicios:UsuariosApi"]
            ?? throw new InvalidOperationException(
                "No se configuró la URL de la API.");

        client.BaseAddress = new Uri(url);
        client.Timeout = TimeSpan.FromSeconds(15);
    });

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Acceso/Login";
        options.AccessDeniedPath = "/Acceso/Denegado";
        options.Cookie.Name = "RuteaYa.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Acceso}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();

