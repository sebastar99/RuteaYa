using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Controllers;
using RuteaYa.Web.Services;
using Fixture = RuteaYa.Test.CompatibilidadPersistidaTest.Fixture;

namespace RuteaYa.Test;

public class RecorridosVistaTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guardadas_CalculaAlAbrir_SinLookupNiCargaDuplicada_ConParadasOrdenadas(bool paradas)
    {
        using var f = new Fixture(paradas, snapshotAntiguo: true); var llamadas = 0;
        var rutas = Rutas((p, _) => { llamadas++; Assert.Equal(paradas ? 3 : 2, p.Count);
            Assert.Equal(-34.6m, p[0].Latitud); Assert.Equal(-35m, p[^1].Latitud);
            if (paradas) Assert.Equal(-34.7m, p[1].Latitud); });
        await using var app = Host(f, rutas.Object); await app.StartAsync();
        using var c = Cliente(app);
        var html = await c.GetStringAsync($"/Recorridos/{f.ViajeId}");
        Assert.Contains("datos-recorrido", html); Assert.Contains("Recalcular recorrido", html);
        Assert.DoesNotContain("data-direccion-here", html); Assert.DoesNotContain("Confirmar ubicación", html);
        Assert.DoesNotContain("Position", html); Assert.DoesNotContain("Identificador HERE", html);
        using var json = JsonDocument.Parse(Regex.Match(html, "id=\"datos-recorrido\"[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value);
        Assert.Equal(3210, json.RootElement.GetProperty("distanciaMetros").GetInt32());
        var tesela = WebUtility.HtmlDecode(Regex.Match(html, "data-teselas=\"([^\"]+)\"").Groups[1].Value);
        using var tile = await c.GetAsync(tesela); Assert.Equal(HttpStatusCode.OK, tile.StatusCode);
        Assert.Equal("image/png", tile.Content.Headers.ContentType!.MediaType);
        await c.GetStringAsync($"/Recorridos/{f.ViajeId}"); Assert.Equal(2, llamadas);
        c.DefaultRequestHeaders.Remove("X-Usuario-Prueba"); c.DefaultRequestHeaders.Add("X-Usuario-Prueba", "8");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(tesela)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/Recorridos/{f.ViajeId}")).StatusCode);
        Assert.Equal(2, llamadas);
    }

    [Theory]
    [InlineData("legado")]
    [InlineData("parada")]
    [InlineData("texto")]
    public async Task FaltantesOModificadas_IndicaEditarViaje_SinGeocodificarNiOmitirParadas(string defecto)
    {
        // texto afecta al envío en el fixture; se modifica también el texto de origen para este caso.
        using var f = new Fixture(true, defecto: defecto);
        if (defecto == "texto") { await using var db = f.Db(); var v = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Viajes); v.Origen = "Texto cambiado"; await db.SaveChangesAsync(); }
        var rutas = new Mock<IRutasGeometriaServicio>(MockBehavior.Strict);
        await using var app = Host(f, rutas.Object); await app.StartAsync(); using var c = Cliente(app);
        var html = WebUtility.HtmlDecode(await c.GetStringAsync($"/Recorridos/{f.ViajeId}"));
        Assert.Contains("Editar Viaje", html); Assert.Contains("guardalos una sola vez", html);
        Assert.DoesNotContain("datos-recorrido", html); Assert.DoesNotContain("data-direccion-here", html);
        rutas.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(401, "here_401")]
    [InlineData(403, "here_403")]
    [InlineData(429, "here_429")]
    [InlineData(500, "here_500")]
    [InlineData(0, "here_timeout")]
    [InlineData(-1, "error_interno")]
    [InlineData(-3, "here_no_configurado")]
    public async Task RoutingFallido_MuestraErrorSeguroYPermiteReintentar(int estado, string codigo)
    {
        using var f = new Fixture(false); var rutas = new Mock<IRutasGeometriaServicio>();
        Exception fallo = estado switch { 0 => new TimeoutException("privado"), -1 => new NullReferenceException("privado"),
            -3 => new HereNoConfiguradoException(), _ => new HttpRequestException("https://here/?apiKey=privado", null, (HttpStatusCode)estado) };
        rutas.Setup(r => r.CalcularGeometriaAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>())).ThrowsAsync(fallo);
        await using var app = Host(f, rutas.Object); await app.StartAsync(); using var c = Cliente(app);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json"); using var r = await c.GetAsync($"/Recorridos/{f.ViajeId}");
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.Equal(estado == 0 ? 504 : estado == -1 ? 500 : 503, (int)r.StatusCode);
        Assert.Contains(codigo, cuerpo); Assert.DoesNotContain("privado", cuerpo);
    }

    [Fact]
    public async Task AutorizaAntesDeLeer_YCancelacionNoProduceErrorSilencioso()
    {
        using var f = new Fixture(false); var rutas = new Mock<IRutasGeometriaServicio>(MockBehavior.Strict);
        await using var app = Host(f, rutas.Object); await app.StartAsync(); using var c = Cliente(app);
        c.DefaultRequestHeaders.Remove("X-Usuario-Prueba");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync($"/Recorridos/{f.ViajeId}")).StatusCode); rutas.VerifyNoOtherCalls();
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var servicio = new PrevisualizacionRecorridoServicio(Mock.Of<IViajesLogica>(), Mock.Of<IGeocodificacionServicio>(), rutas.Object, cache);
        var controller = new RecorridosController(servicio, Mapas()); using var ct = new CancellationTokenSource(); ct.Cancel();
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Prueba")), RequestAborted = ct.Token } };
        Assert.Equal(499, Assert.IsType<StatusCodeResult>(await controller.Index(f.ViajeId, null, ct.Token)).StatusCode);
    }

    [EdgeFact]
    public async Task Navegador_Guardadas_AbreAutomaticamenteYRecalcula_SinSolicitarDirecciones()
    {
        using var f = new Fixture(true); var llamadas = 0;
        var rutas = Rutas((_, _) => Interlocked.Increment(ref llamadas));
        await using var app = Host(f, rutas.Object, navegador: true); await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Assert.Equal("OK", await Helpers.EdgeNavegador.EjecutarAsync(url + $"/Recorridos/{f.ViajeId}"));
        Assert.Equal(2, llamadas);
    }

    static Mock<IRutasGeometriaServicio> Rutas(Action<IReadOnlyList<PuntoRutaDto>, CancellationToken> callback) {
        var m = new Mock<IRutasGeometriaServicio>();
        m.Setup(r => r.CalcularGeometriaAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>())).Callback(callback)
            .ReturnsAsync((IReadOnlyList<PuntoRutaDto> p, CancellationToken _) => new ResultadoRutaGeometriaDto { Totales = new() { DistanciaMetros = 3210, DuracionSegundos = 123 }, Secciones = new() { p.ToList() } }); return m;
    }
    static HttpClient Cliente(WebApplication app) { var c = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) }; c.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7"); return c; }
    static MapasHereServicio Mapas() => new(new HttpClient(new Handler()), Options.Create(new HereRoutingOpciones { ApiKey = "ficticia" }));
    static WebApplication Host(Fixture f, IRutasGeometriaServicio rutas, bool navegador = false) {
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(RecorridosController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        b.Logging.ClearProviders(); b.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0)); b.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        b.Services.AddControllersWithViews().AddApplicationPart(typeof(RecorridosController).Assembly); b.Services.AddMemoryCache();
        b.Services.AddScoped(_ => f.Db()); b.Services.AddScoped<IViajesLogica, ViajesLogica>();
        b.Services.AddSingleton(Mock.Of<IGeocodificacionServicio>(MockBehavior.Strict)); b.Services.AddSingleton(rutas); b.Services.AddSingleton(Mapas());
        b.Services.AddScoped<PrevisualizacionRecorridoServicio>(); b.Services.AddAuthentication("Prueba").AddScheme<AuthenticationSchemeOptions, Auth>("Prueba", _ => { }); b.Services.AddAuthorization();
        var app = b.Build();
        if (navegador) {
            app.Use(async (context, next) => {
                context.Request.Headers["X-Usuario-Prueba"] = "7";
                if (!Regex.IsMatch(context.Request.Path.Value ?? "", @"^/Recorridos/\d+$")) { await next(); return; }
                var salida = context.Response.Body; await using var buffer = new MemoryStream(); context.Response.Body = buffer;
                await next(); buffer.Position = 0; var html = await new StreamReader(buffer).ReadToEndAsync();
                html = Helpers.LeafletPrueba.Reescribir(html).Replace("</body>", "<script src='/prueba/browser.js'></script></body>");
                context.Response.Body = salida; context.Response.ContentLength = null; await context.Response.WriteAsync(html);
            });
            app.MapGet("/js/{nombre}", (string nombre) => Results.Text(File.ReadAllText(Path.Combine(Repo(), "RuteaYa", "wwwroot", "js", Path.GetFileName(nombre))), "application/javascript"));
            app.MapGet("/prueba/browser.js", () => Results.Text(File.ReadAllText(Path.Combine(Repo(), "RuteaYa.Test", "Fixtures", "direcciones-browser.js")), "application/javascript"));
            Helpers.LeafletPrueba.Mapear(app);
        }
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllerRoute("default", "{controller=Acceso}/{action=Login}/{id?}"); return app;
    }
    static string Repo([System.Runtime.CompilerServices.CallerFilePath] string archivo = "") => Path.GetDirectoryName(Path.GetDirectoryName(archivo))!;
    sealed class Handler : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) {
            HttpContent content;
            if (r.RequestUri!.AbsolutePath.Contains("copyright")) content = new StringContent("{\"resources\":{\"base\":{\"styles\":{\"explore.day\":[]}}},\"copyrights\":{}}", System.Text.Encoding.UTF8, "application/json");
            else { content = new ByteArrayContent(new byte[] {137,80,78,71,13,10,26,10}); content.Headers.ContentType = new("image/png"); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, logger, encoder) {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() { var id = Request.Headers["X-Usuario-Prueba"].ToString(); return Task.FromResult(id is "7" or "8" ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, Scheme.Name)), Scheme.Name)) : AuthenticateResult.NoResult()); }
    }
}
