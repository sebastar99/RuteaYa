using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Test.Helpers;
using RuteaYa.Web.Controllers;
using RuteaYa.Web.Models;

namespace RuteaYa.Test;

public class EnviosControllerTest
{
    // Se prueba la lógica real sobre EF interceptado; no se mockean métodos no virtuales.
    private static EnviosController Controlador(EnviosContextoPrueba contexto, string? identificador = "7",
        IGeocodificacionServicio? geo = null, string tipoClaim = ClaimTypes.NameIdentifier)
    {
        var claims = identificador == null ? Array.Empty<Claim>() : new[] { new Claim(tipoClaim, identificador) };
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")) };
        return new EnviosController(new EnviosLogica(contexto.Db, geo ?? Mock.Of<IGeocodificacionServicio>()))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task Index_UsuarioAutenticado_DevuelveVistaConEnvios()
    {
        using var contexto = new EnviosContextoPrueba();
        contexto.Consultas.Responder(EnviosContextoPrueba.Envios(
            (1, 7, null, EstadoEnvio.BuscandoConductor, "Paquete 1"), (2, 7, null, EstadoEnvio.Entregado, "Paquete 2")));
        var vista = Assert.IsType<ViewResult>(await Controlador(contexto).Index(null));
        Assert.Equal("~/Views/Panel/Envios.cshtml", vista.ViewName);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<EnvioDto>>(vista.Model).Count());
        var consulta = Assert.Single(contexto.Consultas.Consultas);
        Assert.Matches(@"\[UsuarioId\]\s*=\s*@", consulta.Sql);
        Assert.Contains(consulta.Parametros.Values, v => Equals(v, 7));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("no-numérico")]
    public async Task Index_IdentidadSinIdValido_ProhibeSinConsultar(string? identificador)
    {
        using var contexto = new EnviosContextoPrueba();
        Assert.IsType<ForbidResult>(await Controlador(contexto, identificador).Index(null));
        Assert.Empty(contexto.Consultas.Consultas);
        Assert.Equal(0, contexto.Guardados);
    }

    [Fact]
    public async Task Index_ClaimSub_UsaUsuarioReal()
    {
        using var contexto = new EnviosContextoPrueba();
        contexto.Consultas.Responder(EnviosContextoPrueba.Envios());
        Assert.IsType<ViewResult>(await Controlador(contexto, "8", tipoClaim: "sub").Index(null));
        Assert.Contains(Assert.Single(contexto.Consultas.Consultas).Parametros.Values, v => Equals(v, 8));
    }

    [Fact]
    public async Task Index_ErrorDeBaseDeDatos_MuestraMensajeSinExponerDatos()
    {
        using var contexto = new EnviosContextoPrueba(new ErrorBaseDatosSimulado());
        var controlador = Controlador(contexto);
        var vista = Assert.IsType<ViewResult>(await controlador.Index(null));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, controlador.Response.StatusCode);
        Assert.DoesNotContain("interno", (string)controlador.ViewBag.Error);
        Assert.Empty(Assert.IsType<EnvioDto[]>(vista.Model));
    }

    [Fact]
    public async Task Detalle_IdValido_DevuelveVistaConEnvio()
    {
        using var contexto = new EnviosContextoPrueba();
        contexto.Consultas.Responder(EnviosContextoPrueba.Envios((5, 7, null, EstadoEnvio.BuscandoConductor, "Caja")));
        var vista = Assert.IsType<ViewResult>(await Controlador(contexto).Detalle(5));
        Assert.Equal("~/Views/Panel/DetalleEnvio.cshtml", vista.ViewName);
        Assert.Equal("Caja", Assert.IsType<EnvioDto>(vista.Model).Descripcion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Detalle_IdInvalido_DevuelveNotFoundSinConsultar(int idInvalido)
    {
        using var contexto = new EnviosContextoPrueba();
        Assert.IsType<NotFoundResult>(await Controlador(contexto).Detalle(idInvalido));
        Assert.Empty(contexto.Consultas.Consultas);
    }

    [Fact]
    public async Task Detalle_EnvioInexistente_DevuelveNotFound()
    {
        using var contexto = new EnviosContextoPrueba();
        contexto.Consultas.Responder(EnviosContextoPrueba.Envios());
        Assert.IsType<NotFoundResult>(await Controlador(contexto).Detalle(99));
    }

    [Fact]
    public async Task Crear_Get_DevuelveVistaFormulario()
    {
        using var contexto = new EnviosContextoPrueba();
        var vista = Assert.IsType<ViewResult>(await Controlador(contexto).Crear());
        Assert.Equal("~/Views/Panel/NuevoEnvio.cshtml", vista.ViewName);
    }

    [Fact]
    public async Task Crear_PostConTextoPeroSinFormularioVerificado_NoPublicaNiGeocodifica()
    {
        using var contexto = new EnviosContextoPrueba();
        var geo = new Mock<IGeocodificacionServicio>(MockBehavior.Strict);
        geo.Setup(g => g.GeocodificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ResultadoGeocodificacionDto { Posicion = new() { Latitud = -34, Longitud = -58 } } });
        var controlador = Controlador(contexto, geo: geo.Object);
        Assert.IsType<ViewResult>(await controlador.Crear(DtoValido()));
        Assert.False(controlador.ModelState.IsValid);
        Assert.False(controlador.TempData.ContainsKey("Exito"));
        Assert.Equal(0, contexto.Guardados);
        geo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Crear_ModeloInvalido_DevuelveVistaConDtoSinConsultar()
    {
        using var contexto = new EnviosContextoPrueba();
        var controlador = Controlador(contexto);
        controlador.ModelState.AddModelError("Descripcion", "Campo requerido");
        var dto = new CrearEnvioViewModel();
        Assert.Same(dto, Assert.IsType<ViewResult>(await controlador.Crear(dto)).Model);
        Assert.Empty(contexto.Consultas.Consultas);
        Assert.Equal(0, contexto.Guardados);
    }

    [Fact]
    public async Task Crear_SinFormulario_NoConsultaProveedorNiExponeDatos()
    {
        using var contexto = new EnviosContextoPrueba();
        var geo = new Mock<IGeocodificacionServicio>(MockBehavior.Strict);
        geo.Setup(g => g.GeocodificarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("URL y credencial ficticia que no deben verse"));
        var controlador = Controlador(contexto, geo: geo.Object);
        Assert.IsType<ViewResult>(await controlador.Crear(DtoValido()));
        Assert.Equal(200, controlador.Response.StatusCode);
        Assert.DoesNotContain("credencial", controlador.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
        Assert.Equal(0, contexto.Guardados);
        Assert.Empty(contexto.Db.ChangeTracker.Entries());
        geo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("malformado")]
    public async Task CrearYCancelar_SinClaimValido_NoOperanComoUsuarioUno(string? identificador)
    {
        using var contexto = new EnviosContextoPrueba();
        var controlador = Controlador(contexto, identificador);
        Assert.IsType<ForbidResult>(await controlador.Crear(DtoValido()));
        Assert.IsType<ForbidResult>(await controlador.Cancelar(10));
        Assert.IsType<ForbidResult>(await controlador.Detalle(10));
        Assert.Empty(contexto.Consultas.Consultas);
        Assert.Equal(0, contexto.Guardados);
    }

    [Fact]
    public async Task Cancelar_PostExitoso_RedirigeAIndexConMensajeExito()
    {
        using var contexto = new EnviosContextoPrueba();
        contexto.Consultas.Responder(EnviosContextoPrueba.Envios((10, 7, null, EstadoEnvio.BuscandoConductor, "Test")));
        var controlador = Controlador(contexto);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await controlador.Cancelar(10)).ActionName);
        Assert.True(controlador.TempData.ContainsKey("Exito"));
        Assert.Equal(1, contexto.Guardados);
        Assert.Equal(EstadoEnvio.Cancelado, contexto.Db.Envios.Local.Single().Estado);
    }

    [Fact]
    public async Task Cancelar_ExcepcionLogica_RedirigeAIndexConMensajeError()
    {
        using var contexto = new EnviosContextoPrueba();
        contexto.Consultas.Responder(EnviosContextoPrueba.Envios((10, 7, null, EstadoEnvio.Entregado, "Test")));
        var controlador = Controlador(contexto);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await controlador.Cancelar(10)).ActionName);
        Assert.True(controlador.TempData.ContainsKey("Error"));
        Assert.Equal(0, contexto.Guardados);
    }

    private static CrearEnvioViewModel DtoValido() => new() { OrigenDir = "Origen", DestinoDir = "Destino",
        Descripcion = "Test", PesoKg = 1, Monto = 100, FechaLimite = DateTime.Today.AddDays(1) };

    private sealed class ErrorBaseDatosSimulado() : DbException("error interno simulado");
}
