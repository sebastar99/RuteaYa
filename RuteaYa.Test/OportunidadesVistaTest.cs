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
    [Fact]
    public async Task Salida_SeleccionConAntiforgeryYEnlacesConservanElContextoEnListadoYDetalle()
    {
        var salida = new SalidaViajeDto { Id = 87, ViajeId = 10, FechaHora = new DateTimeOffset(2030, 1, 7, 8, 0, 0, TimeSpan.FromHours(-3)),
            Disponible = true, CuposDisponibles = 1, PesoDisponibleKg = 7.5m, PaquetesComprometidos = 1, PesoComprometidoKg = 2.5m };
        var viaje = new ViajeOportunidadesDto { Id = 10, Origen = "Origen", Destino = "Destino", Activo = true, SalidaSeleccionada = salida };
        var envio = new EnvioDto { Id = 25, OrigenDir = "Retiro", DestinoDir = "Entrega" };
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new ListadoCandidatosDto {
            Viajes = new[] { viaje }, ViajeSeleccionado = viaje });
        logica.Setup(l => l.SeleccionarSalidaAsync(7, It.Is<SeleccionSalidaDto>(s => s.ViajeId == 10 && s.Fecha == new DateOnly(2030, 1, 7) && s.OffsetUtcMinutos == -180), It.IsAny<CancellationToken>())).ReturnsAsync(87);
        logica.Setup(l => l.ConsultarSalidaAsync(7, 10, 87, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new ListadoCandidatosDto {
            Viajes = new[] { viaje }, ViajeSeleccionado = viaje, Candidatos = new[] { envio }, TotalCandidatos = 20 });
        logica.Setup(l => l.ObtenerDetalleSalidaAsync(7, 10, 87, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new DetalleCandidatoDto { Viaje = viaje, Envio = envio });
        await using var app = CrearHost(logica.Object); await app.StartAsync();
        try {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            var inicial = await cliente.GetStringAsync("/Oportunidades?viajeId=10");
            var token = WebUtility.HtmlDecode(Regex.Match(inicial, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            var datos = new Dictionary<string, string> { ["ViajeId"] = "10", ["Fecha"] = "2030-01-07", ["OffsetUtcMinutos"] = "-180" };
            using var sinToken = await cliente.PostAsync("/Oportunidades/SeleccionarSalida", new FormUrlEncodedContent(datos));
            Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
            datos["__RequestVerificationToken"] = token;
            using var seleccion = await cliente.PostAsync("/Oportunidades/SeleccionarSalida", new FormUrlEncodedContent(datos));
            Assert.Equal(HttpStatusCode.Redirect, seleccion.StatusCode);
            var listado = WebUtility.HtmlDecode(await cliente.GetStringAsync(seleccion.Headers.Location));
            Assert.Contains("Salida #87", listado); Assert.Contains("-03:00", listado); Assert.Contains("Capacidad restante", listado);
            var detalleUrl = Enlaces(listado).Single(e => e.Contains("/Oportunidades/Detalle"));
            Assert.Contains("salidaId=87", detalleUrl);
            Assert.Contains(Enlaces(listado), e => e.Contains("pagina=2") && e.Contains("salidaId=87"));
            var detalle = WebUtility.HtmlDecode(await cliente.GetStringAsync(detalleUrl));
            Assert.Contains("Salida #87", detalle);
            Assert.Contains(Enlaces(detalle), e => e.Contains("/Oportunidades") && e.Contains("salidaId=87") && !e.Contains("Detalle"));
            Assert.DoesNotContain("/Oportunidades/Aceptar", detalle);
            logica.Verify(l => l.SeleccionarSalidaAsync(7, It.IsAny<SeleccionSalidaDto>(), It.IsAny<CancellationToken>()), Times.Once);
        } finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ListadoYDetalle_ExponenDesvioRealYDatosPendientesSinPresentarCompatibilidadFalsa()
    {
        var viaje = new ViajeOportunidadesDto { Id = 10, Origen = "Origen", Destino = "Destino", Activo = true };
        var envio = new EnvioDto { Id = 25, UsuarioId = 8, OrigenDir = "Retiro", DestinoDir = "Entrega", PesoKg = 2 };
        var evaluacion = new EvaluacionOportunidadDto { DatosPendientes = new[] { DatoPendienteOportunidad.SalidaConcretaDesconocida },
            ResultadoMotor = new ResultadoCompatibilidadDto { ViajeId = 10, EnvioId = 25, Estado = EstadoCompatibilidad.Indeterminado,
                Motivos = new[] { MotivoCompatibilidad.DesvioAprobado }, InsercionSeleccionada = new() { Desvio = new() {
                    DistanciaOriginalMetros = 10000, DistanciaConEnvioMetros = 11250, DistanciaAdicionalMetros = 1250,
                    DuracionConEnvioSegundos = 1500, TiempoAdicionalSegundos = 300 } } } };
        var logica = new Mock<IOportunidadesLogica>();
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new ListadoCandidatosDto {
            Viajes = new[] { viaje }, ViajeSeleccionado = viaje, Candidatos = new[] { envio }, TotalCandidatos = 1,
            Evaluaciones = new Dictionary<int, EvaluacionOportunidadDto> { [25] = evaluacion } });
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new DetalleCandidatoDto { Viaje = viaje, Envio = envio, Evaluacion = evaluacion });
        await using var app = CrearHost(logica.Object); await app.StartAsync();
        try {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient { BaseAddress = new Uri(url) }; cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            var listado = WebUtility.HtmlDecode(await cliente.GetStringAsync("/Oportunidades?viajeId=10"));
            Assert.Contains("Indeterminado", listado); Assert.Contains("Adicional:", listado); Assert.Contains("Falta una salida concreta", listado);
            var detalle = WebUtility.HtmlDecode(await cliente.GetStringAsync("/Oportunidades/Detalle?viajeId=10&envioId=25"));
            Assert.Contains("Distancia adicional calculada", detalle); Assert.Contains("Datos pendientes para evaluar", detalle);
            Assert.DoesNotContain("No se ejecutó el motor", detalle); Assert.DoesNotContain("Tomar este envío", detalle);
        } finally { await app.StopAsync(); }
    }

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
            Assert.DoesNotContain("Tomar este envío", detalleHtml);
            Assert.DoesNotContain("/Oportunidades/Aceptar", detalleHtml);
            Assert.Contains("La aceptación de paquetes todavía no está habilitada.", WebUtility.HtmlDecode(detalleHtml));
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

    [Theory]
    [InlineData(null)]
    [InlineData(EstadoCompatibilidad.Compatible)]
    [InlineData(EstadoCompatibilidad.Incompatible)]
    [InlineData(EstadoCompatibilidad.Indeterminado)]
    public async Task DetalleRazor_EstadosYMetricas_SoloMuestraResultadosCuandoHuboMotor(EstadoCompatibilidad? estado)
    {
        var evaluacion = new EvaluacionOportunidadDto();
        if (!estado.HasValue)
        {
            evaluacion.DatosPendientes = new[]
            {
                DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas,
                DatoPendienteOportunidad.PesoDisponibleDesconocido,
                DatoPendienteOportunidad.VencimientoEntregaNoInterpretado
            };
        }
        else
        {
            evaluacion.ResultadoMotor = new ResultadoCompatibilidadDto
            {
                ViajeId = 10, EnvioId = 25, Estado = estado.Value,
                Motivos = new[] { estado == EstadoCompatibilidad.Incompatible ? MotivoCompatibilidad.PesoExcedido :
                    estado == EstadoCompatibilidad.Compatible ? MotivoCompatibilidad.HorarioAprobado : MotivoCompatibilidad.HorarioRetiroNoVerificable },
                InsercionSeleccionada = estado == EstadoCompatibilidad.Incompatible ? null : new InsercionCompatibilidadDto
                {
                    SegmentoRetiro = 0, SegmentoEntrega = 1,
                    FinalizacionEstimada = new DateTimeOffset(2026, 11, 2, 8, 11, 0, TimeSpan.Zero),
                    Desvio = new ResultadoDesvioDto
                    {
                        DistanciaOriginalMetros = 10000, DistanciaConEnvioMetros = 10500, DistanciaAdicionalMetros = 500,
                        DuracionConEnvioSegundos = 660, TiempoAdicionalSegundos = 60
                    }
                }
            };
        }
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 25, It.IsAny<CancellationToken>())).ReturnsAsync(new DetalleCandidatoDto
        {
            Viaje = new ViajeOportunidadesDto { Id = 10, Origen = "Origen publicado", Destino = "Destino publicado" },
            Envio = new EnvioDto { Id = 25, OrigenDir = "Retiro publicado", DestinoDir = "Entrega publicada" },
            Evaluacion = evaluacion
        });
        await using var app = CrearHost(logica.Object);
        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await app.StartAsync(limite.Token);
        try
        {
            var direccion = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient { BaseAddress = new Uri(direccion) };
            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            using var respuesta = await cliente.GetAsync("/Oportunidades/Detalle?viajeId=10&envioId=25", limite.Token);
            var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync(limite.Token));

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.DoesNotContain(">Aceptar", html);
            if (!estado.HasValue)
            {
                Assert.Contains("Pendiente de evaluación", html);
                Assert.Contains("Falta el peso restante real", html);
                Assert.Contains("vencimiento obligatorio", html);
                Assert.Contains("No se ejecutó el motor", html);
                Assert.DoesNotContain("Distancia adicional calculada", html);
                Assert.DoesNotContain("Tiempo adicional calculado", html);
            }
            else
            {
                Assert.Contains(estado == EstadoCompatibilidad.Compatible ? "Compatible según la evaluación" :
                    estado == EstadoCompatibilidad.Incompatible ? "No compatible" : "Compatibilidad indeterminada", html);
                if (estado != EstadoCompatibilidad.Incompatible)
                {
                    Assert.Contains("Distancia adicional calculada", html);
                    Assert.Contains("Tiempo adicional calculado", html);
                    Assert.Contains("Segmento de retiro", html);
                    Assert.Contains("Segmento de entrega", html);
                    Assert.Contains("Finalización estimada del recorrido completo", html);
                    Assert.Contains("no es una hora de entrega", html);
                }
                else Assert.DoesNotContain("Distancia adicional calculada", html);
            }
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Aceptar_PostDirectoConAntiforgeryValido_EstaBloqueadoSinInvocarServicios()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListadoCandidatosDto());
        // No se registra EnviosLogica, EF ni HERE: el POST bloqueado no debe resolverlos.
        await using var app = CrearHost(logica.Object);
        await app.StartAsync();
        try
        {
            var direccion = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { BaseAddress = new Uri(direccion), Timeout = TimeSpan.FromSeconds(20) };
            using var anonimo = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            using var pagina = await cliente.GetAsync("/Oportunidades");
            var html = await pagina.Content.ReadAsStringAsync();
            var token = WebUtility.HtmlDecode(Regex.Match(html,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(token);
            using var sinToken = await cliente.PostAsync("/Oportunidades/Aceptar",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["viajeId"] = "10", ["envioId"] = "25" }));
            Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
            using var respuesta = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["viajeId"] = "10", ["envioId"] = "25", ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.Contains("deshabilitada", await respuesta.Content.ReadAsStringAsync());
            logica.Verify(l => l.ConsultarAsync(7, null, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
            logica.VerifyNoOtherCalls();
        }
        finally { await app.StopAsync(); }
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
