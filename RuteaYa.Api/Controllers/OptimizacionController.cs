using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/optimizacion")]
public class OptimizacionController : ControllerBase
{
    private readonly IOptimizadorServicio _optimizador;

    public OptimizacionController(IOptimizadorServicio optimizador)
    {
        ArgumentNullException.ThrowIfNull(optimizador);
        _optimizador = optimizador;
    }

    /// <summary>Calcula la combinación de envíos que maximiza la ganancia de una salida concreta del conductor.</summary>
    [HttpGet("{viajeId:int}/{salidaId:int}")]
    [ProducesResponseType(typeof(ResultadoOptimizacionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calcular(int viajeId, int salidaId, CancellationToken cancellationToken)
    {
        var conductorId = UsuarioId();
        if (conductorId <= 0) return Forbid();
        if (viajeId <= 0 || salidaId <= 0) return NotFound();
        var resultado = await _optimizador.OptimizarAsync(conductorId, viajeId, salidaId, cancellationToken);
        if (resultado == null) return NotFound();
        return Ok(resultado);
    }

    private int UsuarioId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : 0;
}

[Authorize]
[ApiController]
[Route("api/asignaciones")]
public class AsignacionesController : ControllerBase
{
    private readonly EnviosLogica _envios;

    public AsignacionesController(EnviosLogica envios)
    {
        ArgumentNullException.ThrowIfNull(envios);
        _envios = envios;
    }

    public sealed record AceptarSolicitud(int ViajeId, int SalidaId, int EnvioId);
    public sealed record CombinacionSolicitud(int ViajeId, int SalidaId, int[] EnvioIds, string? Huella);

    /// <summary>Acepta un envío disponible para una salida concreta del conductor (aceptación individual).</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aceptar([FromBody] AceptarSolicitud solicitud, CancellationToken cancellationToken)
    {
        var conductorId = UsuarioId();
        if (conductorId <= 0) return Forbid();
        if (solicitud.ViajeId <= 0 || solicitud.SalidaId <= 0 || solicitud.EnvioId <= 0) return BadRequest();
        var r = await _envios.AceptarSalidaAsync(conductorId, solicitud.ViajeId, solicitud.SalidaId, solicitud.EnvioId, cancellationToken);
        return r.Estado switch
        {
            EstadoAceptacion.Aceptado or EstadoAceptacion.YaAceptado => Ok(r),
            EstadoAceptacion.NoEncontrado => NotFound(r),
            _ => Conflict(r)
        };
    }

    /// <summary>Reserva una combinación completa de envíos en una única transacción; si uno falla no queda ninguna asignación parcial.</summary>
    [HttpPost("combinacion")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Combinacion([FromBody] CombinacionSolicitud solicitud, CancellationToken cancellationToken)
    {
        var conductorId = UsuarioId();
        if (conductorId <= 0) return Forbid();
        if (solicitud.ViajeId <= 0 || solicitud.SalidaId <= 0 || solicitud.EnvioIds == null || solicitud.EnvioIds.Length == 0
            || solicitud.EnvioIds.Any(i => i <= 0) || solicitud.EnvioIds.Distinct().Count() != solicitud.EnvioIds.Length)
            return BadRequest();
        var r = await _envios.ReservarCombinacionAsync(conductorId, solicitud.ViajeId, solicitud.SalidaId,
            solicitud.EnvioIds, solicitud.Huella, cancellationToken);
        return r.Estado switch
        {
            EstadoReserva.Reservado or EstadoReserva.YaReservado => Ok(r),
            EstadoReserva.NoEncontrado => NotFound(r),
            _ => Conflict(r)
        };
    }

    private int UsuarioId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : 0;
}
