using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Controllers;
using Xunit;

namespace RuteaYa.Test;

public class EnviosControllerTest
{
    private static EnviosController Controlador(Mock<EnviosLogica> logica,
        string? identificador = "7", bool autenticado = true)
    {
        var claims = identificador == null ? Array.Empty<Claim>() :
            new[] { new Claim(ClaimTypes.NameIdentifier, identificador) };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, autenticado ? "Cookies" : null))
        };

        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return new EnviosController(logica.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = tempData
        };
    }

    [Fact]
    public async Task Index_UsuarioAutenticado_DevuelveVistaConEnvios()
    {
        var enviosEsperados = new List<EnvioDto>
        {
            new EnvioDto { Id = 1, Descripcion = "Paquete 1", Monto = 1000 },
            new EnvioDto { Id = 2, Descripcion = "Paquete 2", Monto = 2000 }
        };

        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!) { CallBase = false };
        using var cancelacion = new CancellationTokenSource();
        logica.Setup(l => l.ListarAsync(7, null)).ReturnsAsync(enviosEsperados);

        var controlador = Controlador(logica);
        var resultado = await controlador.Index(null, cancelacion.Token);

        var vista = Assert.IsType<ViewResult>(resultado);
        Assert.Equal("~/Views/Panel/Envios.cshtml", vista.ViewName);
        var modelo = Assert.IsAssignableFrom<IEnumerable<EnvioDto>>(vista.Model);
        Assert.Equal(2, modelo.Count());
        logica.Verify(l => l.ListarAsync(7, null), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Index_IdentidadSinIdValido_ProhibeSinConsultar(string? identificador)
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);

        var resultado = await Controlador(logica, identificador).Index(null);

        Assert.IsType<ForbidResult>(resultado);
    }

    [Fact]
    public async Task Index_ErrorDeBaseDeDatos_MuestraMensajeSinExponerDatos()
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        logica.Setup(l => l.ListarAsync(7, null)).ThrowsAsync(new ErrorBaseDatosSimulado());

        var controlador = Controlador(logica);
        var resultado = await controlador.Index(null);

        var vista = Assert.IsType<ViewResult>(resultado);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, controlador.Response.StatusCode);
        Assert.NotNull(controlador.ViewBag.Error);
        Assert.Empty(Assert.IsType<EnvioDto[]>(vista.Model));
    }

    [Fact]
    public async Task Detalle_IdValido_DevuelveVistaConEnvio()
    {
        var envio = new EnvioDto { Id = 5, Descripcion = "Caja", Monto = 1500 };
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        logica.Setup(l => l.ObtenerAsync(7, 5)).ReturnsAsync(envio);

        var resultado = await Controlador(logica).Detalle(5);

        var vista = Assert.IsType<ViewResult>(resultado);
        Assert.Equal("~/Views/Panel/DetalleEnvio.cshtml", vista.ViewName);
        Assert.Same(envio, vista.Model);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Detalle_IdInvalido_DevuelveNotFoundSinConsultar(int idInvalido)
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);

        var resultado = await Controlador(logica).Detalle(idInvalido);

        Assert.IsType<NotFoundResult>(resultado);
    }

    [Fact]
    public async Task Detalle_EnvioInexistente_DevuelveNotFound()
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        logica.Setup(l => l.ObtenerAsync(7, 99)).ReturnsAsync((EnvioDto?)null);

        var resultado = await Controlador(logica).Detalle(99);

        Assert.IsType<NotFoundResult>(resultado);
    }

    [Fact]
    public async Task Crear_Get_DevuelveVistaFormulario()
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);

        var resultado = Controlador(logica).Crear();

        var vista = Assert.IsType<ViewResult>(resultado);
        Assert.Equal("~/Views/Panel/NuevoEnvio.cshtml", vista.ViewName);
    }

    [Fact]
    public async Task Crear_PostValido_RedirigeAIndexConMensajeExito()
    {
        var dto = new CrearEnvioDto { OrigenDir = "Origen", DestinoDir = "Destino", Descripcion = "Test" };
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        logica.Setup(l => l.CrearAsync(7, dto)).ReturnsAsync(new EnvioDto { Id = 1 });

        var controlador = Controlador(logica);
        var resultado = await controlador.Crear(dto);

        var redireccion = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redireccion.ActionName);
        Assert.True(controlador.TempData.ContainsKey("Exito"));
    }

    [Fact]
    public async Task Crear_ModeloInvalido_DevuelveVistaConDtoSinConsultar()
    {
        var dto = new CrearEnvioDto();
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        var controlador = Controlador(logica);
        controlador.ModelState.AddModelError("Descripcion", "Campo requerido");

        var resultado = await controlador.Crear(dto);

        var vista = Assert.IsType<ViewResult>(resultado);
        Assert.Same(dto, vista.Model);
    }

    [Fact]
    public async Task Cancelar_PostExitoso_RedirigeAIndexConMensajeExito()
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        logica.Setup(l => l.CancelarAsync(7, 10)).ReturnsAsync(true);

        var controlador = Controlador(logica);
        var resultado = await controlador.Cancelar(10);

        var redireccion = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redireccion.ActionName);
        Assert.True(controlador.TempData.ContainsKey("Exito"));
    }

    [Fact]
    public async Task Cancelar_ExcepcionLogica_RedirigeAIndexConMensajeError()
    {
        var logica = new Mock<EnviosLogica>((RuteaYaDbContext)null!);
        logica.Setup(l => l.CancelarAsync(7, 10)).ThrowsAsync(new InvalidOperationException("No se puede cancelar."));

        var controlador = Controlador(logica);
        var resultado = await controlador.Cancelar(10);

        var redireccion = Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal("Index", redireccion.ActionName);
        Assert.True(controlador.TempData.ContainsKey("Error"));
    }

    private sealed class ErrorBaseDatosSimulado : DbException
    {
        public ErrorBaseDatosSimulado() : base("error interno simulado") { }
    }
}