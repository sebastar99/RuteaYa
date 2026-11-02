using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Controllers;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class RecorridosVistaTest
{
    [Fact]
    public async Task FlujoMvc_AutorizaValidaAntiforgeryYConfirmaAntesDeMostrarGeometria()
    {
        const string clave = "solo-ficticia-no-exponer";
        const string malicioso = "Origen </script><script>alert('prueba')</script>";
        var viajes = new Mock<IViajesLogica>(MockBehavior.Strict);
        viajes.Setup(v => v.ObtenerPorIdAsync(7, 10)).ReturnsAsync(new ViajeDto
            { Id = 10, UsuarioId = 7, Origen = malicioso, Destino = "Destino" });
        viajes.Setup(v => v.ObtenerPorIdAsync(8, 10)).ReturnsAsync((ViajeDto?)null);
        var geo = new Mock<IGeocodificacionServicio>(MockBehavior.Strict);
        geo.Setup(g => g.GeocodificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string direccion, CancellationToken _) => new[] { new ResultadoGeocodificacionDto
            { DireccionNormalizada = direccion, TipoResultado = "houseNumber", TipoNumeroPuerta = "PA",
                Posicion = new() { Latitud = 50.10228m, Longitud = 8.69821m },
                Accesos = new() { new() { Latitud = 50.09878m, Longitud = 8.68752m } } } });
        var llamadasRutas = 0;
        var llamadasMapas = 0;
        using var handler = new Handler(request =>
        {
            if (request.RequestUri!.Host == "router.hereapi.com")
            {
                llamadasRutas++;
                return Json("""
                    {"routes":[{"sections":[{"summary":{"length":12345,"duration":678},
                        "polyline":"BFoz5xJ67i1B1B7PzIhaxL7Y"}]}]}
                    """);
            }
            Assert.Equal("maps.hereapi.com", request.RequestUri.Host);
            llamadasMapas++;
            if (request.RequestUri.AbsolutePath == "/v3/copyright")
                return Json("""{"resources":{"base":{"styles":{"explore.day":[]}}},"copyrights":{}}""");
            var content = new ByteArrayContent(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            content.Headers.ContentType = new("image/png");
            return new(HttpStatusCode.OK) { Content = content };
        });
        using var httpHere = new HttpClient(handler);
        var opciones = Options.Create(new HereRoutingOpciones { ApiKey = clave });
        await using var app = CrearHost(viajes.Object, geo.Object,
            new RutasHereServicio(httpHere, opciones), new MapasHereServicio(httpHere, opciones));
        await app.StartAsync();
        var direccionHost = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var cliente = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(direccionHost), Timeout = TimeSpan.FromSeconds(20) };

        using var anonimo = await cliente.GetAsync("/Recorridos/10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        viajes.VerifyNoOtherCalls();
        cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
        using var inicial = await cliente.GetAsync("/Recorridos/10");
        var html = await inicial.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, inicial.StatusCode);
        Assert.Contains("no-store", inicial.Headers.CacheControl!.ToString());
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("datos-recorrido", html);
        geo.VerifyNoOtherCalls();
        using var sinAntiforgery = await cliente.PostAsync("/Recorridos/10/Buscar", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, sinAntiforgery.StatusCode);
        geo.VerifyNoOtherCalls();
        var csrf = Valor(html, "__RequestVerificationToken");
        using var buscar = await cliente.PostAsync("/Recorridos/10/Buscar", Form(("__RequestVerificationToken", csrf)));
        Assert.Equal(HttpStatusCode.Redirect, buscar.StatusCode);
        var ubicacion = buscar.Headers.Location!.ToString();
        using var candidatos = await cliente.GetAsync(ubicacion);
        var candidatosHtml = await candidatos.Content.ReadAsStringAsync();
        var sesion = Valor(candidatosHtml, "Sesion");
        Assert.DoesNotContain("checked", candidatosHtml);
        Assert.Contains("Seleccioná un punto", WebUtility.HtmlDecode(candidatosHtml));
        Assert.DoesNotContain("datos-recorrido", candidatosHtml);
        Assert.Equal(0, llamadasRutas);
        using var mapaPrematuro = await cliente.GetAsync($"/Recorridos/10/Atribuciones?sesion={sesion}");
        Assert.Equal(HttpStatusCode.NotFound, mapaPrematuro.StatusCode);
        Assert.Equal(0, llamadasMapas);

        csrf = Valor(candidatosHtml, "__RequestVerificationToken");
        using var incompleto = await cliente.PostAsync("/Recorridos/10/Confirmar", Form(
            ("__RequestVerificationToken", csrf), ("Sesion", sesion), ("Selecciones[0].Candidato", "0")));
        Assert.Equal(HttpStatusCode.BadRequest, incompleto.StatusCode);
        Assert.Equal(0, llamadasRutas);
        using var confirmar = await cliente.PostAsync("/Recorridos/10/Confirmar", Form(
            ("__RequestVerificationToken", csrf), ("Sesion", sesion),
            ("Selecciones[0].Candidato", "0"), ("Selecciones[0].Acceso", "-1"),
            ("Selecciones[1].Candidato", "0"), ("Selecciones[1].Acceso", "0")));
        Assert.Equal(HttpStatusCode.Redirect, confirmar.StatusCode);
        Assert.Equal(1, llamadasRutas);
        using var resultado = await cliente.GetAsync(confirmar.Headers.Location);
        var resultadoHtml = await resultado.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, resultado.StatusCode);
        Assert.Matches(@"12[.,]345", resultadoHtml);
        Assert.Contains("datos-recorrido", resultadoHtml);
        Assert.Contains("/Recorridos/10/Tesela/0/0/0?sesion=", resultadoHtml);
        Assert.DoesNotContain(clave, resultadoHtml);
        Assert.DoesNotContain("<script>alert", resultadoHtml);
        Assert.DoesNotContain("router.hereapi.com", resultadoHtml);
        using var atribuciones = await cliente.GetAsync($"/Recorridos/10/Atribuciones?sesion={sesion}");
        Assert.Equal(HttpStatusCode.OK, atribuciones.StatusCode);
        Assert.Equal("[]", await atribuciones.Content.ReadAsStringAsync());
        using var tesela = await cliente.GetAsync($"/Recorridos/10/Tesela/1/0/0?sesion={sesion}");
        Assert.Equal("image/png", tesela.Content.Headers.ContentType!.MediaType);
        Assert.Equal(2, llamadasMapas);

        cliente.DefaultRequestHeaders.Remove("X-Usuario-Prueba");
        cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "8");
        using var ajeno = await cliente.GetAsync(ubicacion);
        using var teselaAjena = await cliente.GetAsync($"/Recorridos/10/Tesela/1/0/0?sesion={sesion}");
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, teselaAjena.StatusCode);
        Assert.Equal(2, llamadasMapas);
        await app.StopAsync();
    }

    private static string Valor(string html, string nombre) => WebUtility.HtmlDecode(
        Regex.Match(html, $"name=\"{Regex.Escape(nombre)}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Clave, string Valor)[] valores) => new(
        valores.Select(v => new KeyValuePair<string, string>(v.Clave, v.Valor)));
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static WebApplication CrearHost(IViajesLogica viajes, IGeocodificacionServicio geo,
        IRutasGeometriaServicio rutas, MapasHereServicio mapas)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(RecorridosController).Assembly.GetName().Name, EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(RecorridosController).Assembly);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(viajes);
        builder.Services.AddSingleton(geo);
        builder.Services.AddSingleton(rutas);
        builder.Services.AddSingleton(mapas);
        builder.Services.AddScoped<PrevisualizacionRecorridoServicio>();
        builder.Services.AddAuthentication("Prueba").AddScheme<AuthenticationSchemeOptions, UsuarioHandler>("Prueba", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Acceso}/{action=Login}/{id?}");
        return app;
    }

    private sealed class UsuarioHandler(IOptionsMonitor<AuthenticationSchemeOptions> opciones,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var usuario = Request.Headers["X-Usuario-Prueba"].ToString();
            if (usuario is not ("7" or "8")) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, usuario) }, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
