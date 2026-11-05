
using Microsoft.AspNetCore.Authentication.Cookies;
using RuteaYa.Logica;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<HereRoutingOpciones>(builder.Configuration.GetSection("HereRouting"));
builder.Services.AddHttpClient<IRutasServicio, RutasHereServicio>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    // La API Key viaja en la query según el contrato de HERE; no registrar la URL.
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddHttpClient<IGeocodificacionServicio, GeocodificacionHereServicio>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .RemoveAllLoggers()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

var connectionString = builder.Configuration.GetConnectionString("RuteaYa")
    ?? throw new InvalidOperationException(
        "No se configuró la conexión RuteaYa para MVC.");

builder.Services.AddDbContext<RuteaYaDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<IOportunidadesLogica, OportunidadesLogica>();
builder.Services.AddScoped<EnviosLogica>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddTransient<JwtTokenHandler>();

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
    })
    .AddHttpMessageHandler<JwtTokenHandler>();


builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Acceso/Login";
        options.AccessDeniedPath = "/Acceso/Denegado";
        options.Cookie.Name = "RuteaYa.Auth";
        options.Cookie.HttpOnly = true;
        // En producción la cookie debe viajar exclusivamente por HTTPS. En
        // desarrollo también se permite el perfil HTTP definido en
        // launchSettings.json; con Always el navegador descarta la cookie y
        // el token no llega nunca a la API.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
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
