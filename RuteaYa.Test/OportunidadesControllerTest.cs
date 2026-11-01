using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Test.Helpers;
using RuteaYa.Web.Controllers;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class OportunidadesControllerTest
{
    private static OportunidadesController Controlador(Mock<IOportunidadesLogica> logica,
        string? identificador = "7", bool autenticado = true, string? tipoClaim = null)
    {
        var claims = identificador == null ? Array.Empty<Claim>() :
            new[] { new Claim(tipoClaim ?? ClaimTypes.NameIdentifier, identificador) };
        return new OportunidadesController(logica.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, autenticado ? "Cookies" : null))
                }
            }
        };
    }

    [Fact]
    public async Task Index_UsuarioAutenticado_UsaClaimAunqueLaQueryIntenteCambiarConductor()
    {
        var listado = new ListadoCandidatosDto();
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        using var cancelacion = new CancellationTokenSource();
        logica.Setup(l => l.ConsultarAsync(7, 10, 2, 20, cancelacion.Token)).ReturnsAsync(listado);
        var controlador = Controlador(logica);
        controlador.Request.QueryString = new QueryString("?conductorId=99");

        var resultado = await controlador.Index(10, 2, 20, cancelacion.Token);

        var vista = Assert.IsType<ViewResult>(resultado);
        Assert.Equal("~/Views/Panel/Oportunidades.cshtml", vista.ViewName);
        Assert.Same(listado, Assert.IsType<OportunidadesViewModel>(vista.Model).Listado);
        logica.Verify(l => l.ConsultarAsync(7, 10, 2, 20, cancelacion.Token), Times.Once);
        logica.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Index_NoAutenticado_DesafiaAccesoSinConsultar()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);

        Assert.IsType<ChallengeResult>(await Controlador(logica, autenticado: false).Index(10));

        logica.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-numérico")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Index_IdentidadSinIdValido_ProhibeSinConsultar(string? identificador)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);

        Assert.IsType<ForbidResult>(await Controlador(logica, identificador).Index(10));

        logica.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Index_ClaimSub_RespetaConvencionExistente()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, null, 1, 10, default)).ReturnsAsync(new ListadoCandidatosDto());

        Assert.IsType<ViewResult>(await Controlador(logica, tipoClaim: "sub").Index());
    }

    [Theory]
    [InlineData(99)]
    [InlineData(999)]
    public async Task Index_ViajeAjenoOInexistente_RespondeIgualSinExponerDatos(int viajeId)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, viajeId, 1, 10, default)).ReturnsAsync((ListadoCandidatosDto?)null);

        Assert.IsType<NotFoundResult>(await Controlador(logica).Index(viajeId));
    }

    [Fact]
    public async Task Index_ParametroMalFormado_NoConsulta()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        var controlador = Controlador(logica);
        controlador.ModelState.AddModelError("pagina", "Formato inválido.");

        Assert.IsType<BadRequestObjectResult>(await controlador.Index(10));

        logica.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Index_IdNoPositivo_NoConsulta()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);

        Assert.IsType<NotFoundResult>(await Controlador(logica).Index(0));

        logica.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Index_PaginacionInvalida_RespondeBadRequestSeguro()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 51, default))
            .ThrowsAsync(new ArgumentOutOfRangeException("tamanoPagina"));

        Assert.IsType<BadRequestObjectResult>(await Controlador(logica).Index(10, tamanoPagina: 51));
    }

    [Fact]
    public async Task Index_SinCandidatos_DevuelveVistaRealConListadoPendienteVacio()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, default)).ReturnsAsync(new ListadoCandidatosDto
        {
            ViajeSeleccionado = new ViajeOportunidadesDto { Id = 10, Origen = "Morón", Destino = "Luján" }
        });

        var vista = Assert.IsType<ViewResult>(await Controlador(logica).Index(10));

        var modelo = Assert.IsType<OportunidadesViewModel>(vista.Model);
        Assert.Empty(modelo.Listado.Candidatos);
        Assert.True(modelo.Listado.EvaluacionPendiente);
        Assert.Null(modelo.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Index_ErrorDeConsulta_MuestraMensajeSinDatosInternos(bool timeout)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        Exception error = timeout ? new TimeoutException("dato interno sensible") : new ErrorBaseDatosSimulado();
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, default)).ThrowsAsync(error);
        var controlador = Controlador(logica);

        var vista = Assert.IsType<ViewResult>(await controlador.Index(10));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, controlador.Response.StatusCode);
        var modelo = Assert.IsType<OportunidadesViewModel>(vista.Model);
        Assert.NotNull(modelo.Error);
        Assert.DoesNotContain("sensible", modelo.Error);
        Assert.Empty(modelo.Listado.Candidatos);
    }

    [Fact]
    public async Task Index_Cancelacion_SePropagaSinConvertirlaEnListadoVacio()
    {
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, cancelacion.Token))
            .Returns(Task.FromCanceled<ListadoCandidatosDto?>(cancelacion.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Controlador(logica).Index(10, cancellationToken: cancelacion.Token));
    }

    [Fact]
    public void PanelOportunidades_RutaAnterior_RedirigeConLosParametrosSinTocarCrud()
    {
        var consultas = new ConsultasEfSimuladas();
        using var db = consultas.CrearContexto();
        var viajesApi = new Mock<IViajesApiServicio>(MockBehavior.Strict);
        var panel = new PanelController(db, viajesApi.Object);

        var redireccion = Assert.IsType<RedirectToActionResult>(panel.Oportunidades(10, 2, 20));

        Assert.Equal("Oportunidades", redireccion.ControllerName);
        Assert.Equal("Index", redireccion.ActionName);
        Assert.Equal(10, redireccion.RouteValues!["viajeId"]);
        Assert.Equal(2, redireccion.RouteValues["pagina"]);
        Assert.Equal(20, redireccion.RouteValues["tamanoPagina"]);
        Assert.Empty(consultas.Consultas);
        viajesApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Detalle_UsaClaimsYConservaContextoDelListado()
    {
        var detalle = new DetalleCandidatoDto
        {
            Viaje = new ViajeOportunidadesDto { Id = 10 }, Envio = new EnvioDto { Id = 25 }
        };
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        using var cancelacion = new CancellationTokenSource();
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 25, cancelacion.Token)).ReturnsAsync(detalle);
        var controlador = Controlador(logica);
        controlador.Request.QueryString = new QueryString("?conductorId=99");

        var vista = Assert.IsType<ViewResult>(await controlador.Detalle(10, 25, 2, 20, cancelacion.Token));

        Assert.Equal("~/Views/Panel/DetalleOportunidad.cshtml", vista.ViewName);
        var modelo = Assert.IsType<DetalleOportunidadViewModel>(vista.Model);
        Assert.Same(detalle, modelo.Detalle);
        Assert.Equal(10, modelo.ViajeId);
        Assert.Equal(2, modelo.Pagina);
        Assert.Equal(20, modelo.TamanoPagina);
        Assert.True(modelo.Detalle!.EvaluacionPendiente);
        logica.Verify(l => l.ObtenerDetalleAsync(7, 10, 25, cancelacion.Token), Times.Once);
        logica.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Detalle_NoAutenticado_DesafiaSinConsultar()
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);

        Assert.IsType<ChallengeResult>(await Controlador(logica, autenticado: false).Detalle(10, 25));

        logica.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrecto")]
    [InlineData("0")]
    public async Task Detalle_IdDeSesionInvalido_ProhibeSinConsultar(string? id)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);

        Assert.IsType<ForbidResult>(await Controlador(logica, id).Detalle(10, 25));

        logica.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(99, 25)]
    [InlineData(999, 25)]
    [InlineData(10, 999)]
    [InlineData(10, 26)]
    public async Task Detalle_ViajeAjenoOEnvioInexistenteONoDisponible_NoRevelaCausa(int viaje, int envio)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ObtenerDetalleAsync(7, viaje, envio, default)).ReturnsAsync((DetalleCandidatoDto?)null);

        Assert.IsType<NotFoundResult>(await Controlador(logica).Detalle(viaje, envio));
    }

    [Theory]
    [InlineData(0, 25, 1, 10)]
    [InlineData(10, 0, 1, 10)]
    [InlineData(10, 25, 0, 10)]
    [InlineData(10, 25, 1, 51)]
    public async Task Detalle_ParametrosInvalidos_NoConsulta(int viaje, int envio, int pagina, int tamano)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);

        var resultado = await Controlador(logica).Detalle(viaje, envio, pagina, tamano);

        if (viaje <= 0 || envio <= 0) Assert.IsType<NotFoundResult>(resultado);
        else Assert.IsType<BadRequestObjectResult>(resultado);
        logica.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Detalle_ErrorDeAlmacenamiento_NoMuestraDatosInternos(bool timeout)
    {
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        Exception error = timeout ? new TimeoutException("sensible") : new ErrorBaseDatosSimulado();
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 25, default)).ThrowsAsync(error);
        var controlador = Controlador(logica);

        var vista = Assert.IsType<ViewResult>(await controlador.Detalle(10, 25, 2, 20));

        var modelo = Assert.IsType<DetalleOportunidadViewModel>(vista.Model);
        Assert.Equal(503, controlador.Response.StatusCode);
        Assert.Null(modelo.Detalle);
        Assert.NotNull(modelo.Error);
        Assert.DoesNotContain("sensible", modelo.Error);
        Assert.Equal(10, modelo.ViajeId);
        Assert.Equal(2, modelo.Pagina);
    }

    [Fact]
    public async Task Detalle_Cancelacion_SePropaga()
    {
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();
        var logica = new Mock<IOportunidadesLogica>(MockBehavior.Strict);
        logica.Setup(l => l.ObtenerDetalleAsync(7, 10, 25, cancelacion.Token))
            .Returns(Task.FromCanceled<DetalleCandidatoDto?>(cancelacion.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Controlador(logica).Detalle(10, 25, cancellationToken: cancelacion.Token));
    }

    [Theory]
    [InlineData(10, 25, "Detalle")]
    [InlineData(10, null, "Index")]
    [InlineData(null, null, "Index")]
    public void PanelDetalle_RutaAnterior_NoDevuelveLaMaqueta(int? viajeId, int? envioId, string accion)
    {
        var consultas = new ConsultasEfSimuladas();
        using var db = consultas.CrearContexto();
        var viajesApi = new Mock<IViajesApiServicio>(MockBehavior.Strict);
        var panel = new PanelController(db, viajesApi.Object);

        var resultado = Assert.IsType<RedirectToActionResult>(panel.DetalleOportunidad(
            viajeId: viajeId, envioId: envioId, pagina: 2, tamanoPagina: 20));

        Assert.Equal("Oportunidades", resultado.ControllerName);
        Assert.Equal(accion, resultado.ActionName);
        Assert.Equal(viajeId, resultado.RouteValues!["viajeId"]);
        Assert.Equal(2, resultado.RouteValues["pagina"]);
        Assert.Equal(20, resultado.RouteValues["tamanoPagina"]);
        if (accion == "Detalle") Assert.Equal(envioId, resultado.RouteValues["envioId"]);
        Assert.Empty(consultas.Consultas);
        viajesApi.VerifyNoOtherCalls();
    }

    private sealed class ErrorBaseDatosSimulado : DbException
    {
        public ErrorBaseDatosSimulado() : base("dato interno sensible") { }
    }
}
