
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using RuteaYa.Api.Controllers;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class ViajesControllerTests
{
    private readonly Mock<IViajesLogica> _logica;
    private readonly ViajesController _controller;

    public ViajesControllerTests()
    {
        _logica = new Mock<IViajesLogica>();
        _controller = new ViajesController(_logica.Object);

        ConfigurarUsuario(7);
    }

    private void ConfigurarUsuario(int usuarioId)
    {
        var claims = usuarioId > 0 
            ? new[] {
                new Claim (ClaimTypes.NameIdentifier, usuarioId.ToString())
            }
            : Array.Empty<Claim>();

        var identity = new ClaimsIdentity(claims, "Bearer");

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    [Fact]
    public async Task CrearViaje_Valido_RetornaCreated()
    {
        var viaje = new ViajeDto
        {
            Id = 10,
            UsuarioId = 7,

            Origen = "Avellaneda",
            Destino = "La Plata",

            Activo = true
        };

        var dto = new CrearViajeDto
        {
            Origen = "Avellaneda",
            Destino = "La Plata",

            DiasSemana = new List<int> { 1, 3 },
            HoraSalida = new TimeSpan(8, 0, 0),
            HoraLlegada = new TimeSpan(10, 0, 0),

            CantMaxPaquetes = 10,
            PesoMax = 100,
            DesvioMaxKm = 5
        };

        _logica
            .Setup(x => x.CrearViajeAsync(7, dto))
            .ReturnsAsync((true, "Creado", viaje));

        var resultado = await _controller.CrearViaje(dto);

        Assert.IsType<CreatedAtActionResult>(resultado);

        _logica.Verify(x => x.CrearViajeAsync(7, dto), Times.Once);
    }

    [Fact]
    public async Task CrearViaje_DatosInvalidos_RetornaBadRequest()
    {
        var dto = new CrearViajeDto
        {
            Origen = "",
            Destino = "",
            DiasSemana = new()
        };

        _logica.Setup(x => x.CrearViajeAsync(7, dto)).ReturnsAsync((
            false,
            "El origen y destino son obligatorios.",
            (ViajeDto?)null
        ));

        var resultado = await _controller.CrearViaje(dto);

        Assert.IsType<BadRequestObjectResult>(resultado);
    }

    [Fact]
    public async Task ListarViajes_Activos_ConsultaSoloUsuarioAutenticado()
    {
        _logica
            .Setup(x => x.ListarViajesAsync(7, "activo"))
            .ReturnsAsync(new List<ViajeDto>());

        var resultado = await _controller.ListarViajes("activo");

        Assert.IsType<OkObjectResult>(resultado);

        _logica.Verify (
            x => x.ListarViajesAsync(7, "activo"),
            Times.Once
        );
    }

    [Fact]
    public async Task ListarViajes_Inactivos_ConsultaSoloUsuarioAutenticado()
    {
        _logica
            .Setup(x => x.ListarViajesAsync(7, "inactivo"))
            .ReturnsAsync(new List<ViajeDto>());

        var resultado = await _controller.ListarViajes("inactivo");

        Assert.IsType<OkObjectResult>(resultado);

        _logica.Verify (
            x => x.ListarViajesAsync(7, "inactivo"),
            Times.Once
        );
    }

    [Fact]
    public async Task EditarViaje_Valido_RetornaOk()
    {
        var dto = new EditarViajeDto
        {
            Origen = "Avellaneda",
            Destino = "Quilmes",

            DiasSemana = new() { 1 },
            HoraSalida = new TimeSpan(8, 0, 0),
            HoraLlegada = new TimeSpan(9, 0, 0),

            CantMaxPaquetes = 5,
            PesoMax = 50,
            DesvioMaxKm = 3
        };

        var viaje = new ViajeDto
        {
            Id = 10,
            UsuarioId = 7,

            Origen = dto.Origen,
            Destino = dto.Destino
        };

        _logica
            .Setup(x => x.EditarViajeAsync(7, 10, dto))
            .ReturnsAsync((true, "Editado", viaje));

        var resultado = await _controller.EditarViaje(10, dto);

        Assert.IsType<OkObjectResult>(resultado);
    }

    [Fact]
    public async Task DesactivarViaje_Valido_RetornaNoContent()
    {
        _logica
            .Setup(x => x.DesactivarViajeAsync(7, 10))
            .ReturnsAsync((true, "Desactivado"));

        var resultado = await _controller.DesactivarViaje(10);

        Assert.IsType<NoContentResult>(resultado);
    }

    [Fact]
    public async Task AgregarParada_Valida_RetornaOk()
    {
        var dto = new ViajesController.AgregarParadaRequest
        {
            Ubicacion = "Quilmes",
            Orden = 1
        };

        _logica
            .Setup(x => x.AgregarParadaAsync(7, 10, "Quilmes", 1))
            .ReturnsAsync((
                true,
                "Agregada",
                new ParadaDto {Id = 5, Ubicacion = "Quilmes", Orden = 1}
            ));

        var resultado = await _controller.AgregarParada(10, dto);

        Assert.IsType<OkObjectResult>(resultado);
    }

    [Fact]
    public async Task EliminarParada_Valida_RetornaNoContent()
    {
        _logica
            .Setup(x => x.EliminarParadaAsync(7, 10, 5))
            .ReturnsAsync((true, "Eliminada"));

        var resultado = await _controller.EliminarParada(10, 5);

        Assert.IsType<NoContentResult>(resultado);
    }

    [Fact]
    public async Task ReordenarParadas_Valido_RetornaOk()
    {
        var dto = new ViajesController.ReordenarParadasRequest
        {
            ParadasIds = new List<int> { 3, 2, 1 }
        };

        _logica
            .Setup(x => x.ReordenarParadasAsync(7, 10, dto.ParadasIds))
            .ReturnsAsync((true, "Ordenadas"));

        var resultado = await _controller.ReordenarParadas(10, dto);

        Assert.IsType<OkObjectResult>(resultado);
    }

    [Fact]
    public async Task ObtenerViaje_DeOtroUsuario_NoPermiteAcceso()
    {
        ConfigurarUsuario(7);

        _logica
            .Setup(x => x.ObtenerPorIdAsync(7, 99))
            .ReturnsAsync((ViajeDto?)null);

        var resultado = await _controller.ObtenerPorId(99);

        Assert.IsType<NotFoundObjectResult>(resultado);

        _logica.Verify (x => x.ObtenerPorIdAsync(7, 99), Times.Once);
    }

    [Fact]
    public async Task EditarViaje_IdInvalido_RetornaBadRequest()
    {
        var dto = new EditarViajeDto();

        var resultado = await _controller.EditarViaje(0, dto);

        Assert.IsType<BadRequestObjectResult>(resultado);

        _logica.Verify(
            x => x.EditarViajeAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<EditarViajeDto>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task ObtenerViaje_UsuarioNoAutenticado_RetornaUnauthorized()
    {
        ConfigurarUsuario(0);

        var resultado = await _controller.ObtenerPorId(10);

        Assert.IsType<UnauthorizedResult>(resultado);

        _logica.Verify(
            x => x.ObtenerPorIdAsync(
                It.IsAny<int>(),
                It.IsAny<int>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task EditarViaje_UtilizaUsuarioDelToken()
    {
        ConfigurarUsuario(7);

        var dto = new EditarViajeDto
        {
            Origen = "Avellaneda",
            Destino = "Quilmes",
            DiasSemana = new() { 1 },
            HoraSalida = new TimeSpan(8, 0, 0),
            HoraLlegada = new TimeSpan(9, 0, 0),
            CantMaxPaquetes = 5,
            PesoMax = 50,
            DesvioMaxKm = 3
        };

        _logica
            .Setup(x => x.EditarViajeAsync(7, 10, dto))
            .ReturnsAsync((
                true,
                "Editado",
                new ViajeDto { Id = 10, UsuarioId = 7 }
            ));

        var resultado = await _controller.EditarViaje(10, dto);

        Assert.IsType<OkObjectResult>(resultado);

        _logica.Verify(
            x => x.EditarViajeAsync(7, 10, dto),
            Times.Once
        );
    }

    [Fact]
    public async Task UsuarioSinIdentidad_NoPuedeCrearViaje()
    {
        ConfigurarUsuario(0);

        var resultado = await _controller.CrearViaje(new CrearViajeDto());

        Assert.IsType<UnauthorizedResult>(resultado);

        _logica.Verify(x => x.CrearViajeAsync(
            It.IsAny<int>(),
            It.IsAny<CrearViajeDto>()
        ), Times.Never);
    }
}
