using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
        builder.Services.AddSingleton<IOportunidadesLogica>(logica.Object);
        builder.Services.AddAuthentication("Prueba")
            .AddScheme<AuthenticationSchemeOptions, UsuarioPruebaHandler>("Prueba", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Oportunidades}/{action=Index}/{id?}");
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
