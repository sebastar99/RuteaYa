using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Controllers;

namespace RuteaYa.Test;

public class OportunidadesVistaTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PantallaRazor_ProtegidaYConDatosONinguno_NoMuestraMetricasFicticias(bool conCandidatos)
    {
        var viaje = new ViajeOportunidadesDto { Id = 10, Origen = "Morón", Destino = "Luján", Activo = true };
        var listado = new ListadoCandidatosDto
        {
            Viajes = new[] { viaje }, ViajeSeleccionado = viaje,
            TotalCandidatos = conCandidatos ? 1 : 0,
            Candidatos = conCandidatos ? new[]
            {
                new EnvioDto
                {
                    Id = 25, OrigenDir = "Retiro de prueba", DestinoDir = "Entrega de prueba",
                    Descripcion = "<script>alert('prueba')</script>", FechaLimite = new DateTime(2026, 11, 15),
                    PesoKg = 2.5m, Monto = 4000, Tamano = TamanoEnvio.Mediano,
                    Estado = EstadoEnvio.BuscandoConductor
                }
            } : Array.Empty<EnvioDto>()
        };
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(listado);

        await using var app = CrearHost(logica.Object);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(limite.Token);
        try
        {
            var direccion = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var cliente = new HttpClient(handler) { BaseAddress = new Uri(direccion) };
            using var anonimo = await cliente.GetAsync("/Oportunidades?viajeId=10", limite.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
            logica.VerifyNoOtherCalls();

            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            using var respuesta = await cliente.GetAsync("/Oportunidades?viajeId=10", limite.Token);
            var html = await respuesta.Content.ReadAsStringAsync(limite.Token);

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Contains("Seleccion", html);
            Assert.DoesNotContain("Muy compatible", html);
            Assert.DoesNotContain("Desvío promedio", html);
            Assert.DoesNotContain("/Panel/Optimizador", html.Split("<main")[1].Split("</main>")[0]);
            Assert.DoesNotContain("<script>alert", html);
            if (conCandidatos)
            {
                Assert.Contains("Pendiente de evaluaci", html);
                Assert.Contains("Retiro de prueba", html);
                Assert.Contains("&lt;script&gt;", html);
            }
            else
            {
                Assert.Contains("No hay env", html);
                Assert.DoesNotContain("Retiro de prueba", html);
            }
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ListadoYDetalleRazor_EnlacesConservanViajeYPaginacionYSoloMuestranDatosDelModelo()
    {
        var viaje = new ViajeOportunidadesDto { Id = 10, Origen = "Morón", Destino = "Luján", Activo = true };
        var envio = new EnvioDto
        {
            Id = 25, UsuarioId = 99, OrigenDir = "Retiro publicado para prueba",
            DestinoDir = "Entrega publicada para prueba", Descripcion = "<script>alert('prueba')</script>",
            PesoKg = 2.5m, Tamano = TamanoEnvio.Mediano, FechaLimite = new DateTime(2026, 11, 15, 18, 30, 0),
            Monto = 4000, Estado = EstadoEnvio.BuscandoConductor
        };
        var listado = new ListadoCandidatosDto
        {
            Viajes = new[] { viaje }, ViajeSeleccionado = viaje, Candidatos = new[] { envio },
            Pagina = 2, TamanoPagina = 20, TotalCandidatos = 21
        };
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, 10, 2, 20, It.IsAny<CancellationToken>())).ReturnsAsync(listado);
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DetalleCandidatoDto { Viaje = viaje, Envio = envio });
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 26, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DetalleCandidatoDto?)null);
        await using var app = CrearHost(logica.Object);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(limite.Token);
        try
        {
            var direccion = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var cliente = new HttpClient(handler) { BaseAddress = new Uri(direccion) };
            using var anonimo = await cliente.GetAsync("/Oportunidades/Detalle?viajeId=10&envioId=25", limite.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
            logica.VerifyNoOtherCalls();

            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            using var pagina = await cliente.GetAsync("/Oportunidades?viajeId=10&pagina=2&tamanoPagina=20", limite.Token);
            Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
            var listadoHtml = await pagina.Content.ReadAsStringAsync(limite.Token);
            var enlaceDetalle = Enlaces(listadoHtml).Single(e => e.StartsWith("/Oportunidades/Detalle?", StringComparison.Ordinal));
            var queryDetalle = QueryHelpers.ParseQuery(new Uri(cliente.BaseAddress, enlaceDetalle).Query);
            Assert.Equal("10", queryDetalle["viajeId"].ToString());
            Assert.Equal("25", queryDetalle["envioId"].ToString());
            Assert.Equal("2", queryDetalle["pagina"].ToString());
            Assert.Equal("20", queryDetalle["tamanoPagina"].ToString());

            using var detalle = await cliente.GetAsync(enlaceDetalle, limite.Token);
            var detalleHtml = await detalle.Content.ReadAsStringAsync(limite.Token);
            Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
            Assert.Contains("Retiro publicado para prueba", detalleHtml);
            Assert.Contains("Entrega publicada para prueba", detalleHtml);
            Assert.Contains("Mediano", detalleHtml);
            Assert.Contains("15/11/2026 18:30", detalleHtml);
            Assert.Contains("Pendiente de evaluaci", detalleHtml);
            Assert.Contains("&lt;script&gt;", detalleHtml);
            Assert.DoesNotContain("<script>alert", detalleHtml);
            Assert.DoesNotContain("UsuarioId", detalleHtml);
            Assert.DoesNotContain("Desvío estimado", detalleHtml);
            Assert.DoesNotContain("Caballito", detalleHtml);
            Assert.DoesNotContain(">Aceptar", detalleHtml);
            var enlaceVolver = Enlaces(detalleHtml).Single(e =>
            {
                var uri = new Uri(cliente.BaseAddress, e);
                return (uri.AbsolutePath == "/Oportunidades" || uri.AbsolutePath == "/Oportunidades/Index") &&
                    QueryHelpers.ParseQuery(uri.Query).ContainsKey("viajeId");
            });
            var queryVolver = QueryHelpers.ParseQuery(new Uri(cliente.BaseAddress, enlaceVolver).Query);
            Assert.Equal("10", queryVolver["viajeId"].ToString());
            Assert.Equal("2", queryVolver["pagina"].ToString());
            Assert.Equal("20", queryVolver["tamanoPagina"].ToString());
            using var vuelta = await cliente.GetAsync(enlaceVolver, limite.Token);
            Assert.Equal(HttpStatusCode.OK, vuelta.StatusCode);

            using var noDisponible = await cliente.GetAsync("/Oportunidades/Detalle?viajeId=10&envioId=26", limite.Token);
            Assert.Equal(HttpStatusCode.NotFound, noDisponible.StatusCode);
            Assert.DoesNotContain("Retiro publicado para prueba", await noDisponible.Content.ReadAsStringAsync(limite.Token));
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static IEnumerable<string> Enlaces(string html) =>
        Regex.Matches(html, "href=\"([^\"]+)\"")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value));

    private static WebApplication CrearHost(IOportunidadesLogica logica)
    {
        // Host exclusivo de prueba: solo loopback, autenticación ficticia y lógica simulada.
        // No ejecuta Program.cs ni carga User Secrets, EF, la API propia o servicios HERE.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(OportunidadesController).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(opciones => opciones.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(OportunidadesController).Assembly);
        builder.Services.AddSingleton(logica);
        builder.Services.AddAuthentication("Prueba")
            .AddScheme<AuthenticationSchemeOptions, UsuarioPruebaHandler>("Prueba", _ => { });
        builder.Services.AddAuthorization();
        var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Acceso}/{action=Login}/{id?}");
        return app;
    }

    private sealed class UsuarioPruebaHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public UsuarioPruebaHandler(IOptionsMonitor<AuthenticationSchemeOptions> opciones,
            ILoggerFactory logger, UrlEncoder codificador) : base(opciones, logger, codificador) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["X-Usuario-Prueba"] != "7")
                return Task.FromResult(AuthenticateResult.NoResult());
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identidad), Scheme.Name)));
        }
    }
}
