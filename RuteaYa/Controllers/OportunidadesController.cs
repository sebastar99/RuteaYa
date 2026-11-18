using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Oportunidades")]
public class OportunidadesController : Controller
{
    private const string Vista = "~/Views/Panel/Oportunidades.cshtml";
    private readonly IOportunidadesLogica _oportunidades;
    private readonly EnviosLogica? _envios;
    private readonly RuteaYaDbContext? _db;

    public OportunidadesController(IOportunidadesLogica oportunidades, EnviosLogica? envios = null, RuteaYaDbContext? db = null)
    {
        ArgumentNullException.ThrowIfNull(oportunidades);
        _oportunidades = oportunidades;
        _envios = envios;
        _db = db;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        int? viajeId = null, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default, int? salidaId = null)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Los parámetros de la consulta no son válidos.");
        if (viajeId.HasValue && viajeId.Value <= 0) return NotFound();
        if (salidaId.HasValue && (!viajeId.HasValue || salidaId <= 0)) return NotFound();

        try
        {
            var listado = salidaId.HasValue
                ? await _oportunidades.ConsultarSalidaAsync(conductorId, viajeId!.Value, salidaId.Value, pagina, tamanoPagina, cancellationToken)
                : await _oportunidades.ConsultarAsync(conductorId, viajeId, pagina, tamanoPagina, cancellationToken);
            if (listado == null) return NotFound();
            var modelo = new OportunidadesViewModel { Listado = listado };
            if (_db != null && listado.ViajeSeleccionado != null)
            {
                var v = await _db.Viajes.AsNoTracking().Include(v => v.Dias).Include(v => v.OrigenUbicacion)
                    .SingleAsync(v => v.Id == listado.ViajeSeleccionado.Id && v.UsuarioId == conductorId, cancellationToken);
                modelo.DiasHabilitados = v.Dias.Select(d => d.DiaSemana).ToArray(); modelo.HoraSalida = v.HoraSalida;
                modelo.ZonaAutomatica = ZonaHorariaViaje.Automatica(v);
            }
            return View(Vista, modelo);
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest("La página debe ser positiva y el tamaño de página debe estar entre 1 y 50.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception error) when (error is DbException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            // No mostrar mensajes del proveedor, conexiones ni datos internos.
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(Vista, new OportunidadesViewModel
            {
                Error = "No se pudieron cargar o evaluar los candidatos. Revisá la disponibilidad de datos y del servicio de rutas e intentá nuevamente."
            });
        }
    }

    [HttpGet("Detalle")]
    public async Task<IActionResult> Detalle(
        int viajeId, int envioId, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default, int? salidaId = null)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Los parámetros de la consulta no son válidos.");
        if (viajeId <= 0 || envioId <= 0) return NotFound();
        if (salidaId <= 0) return NotFound();
        if (pagina < 1 || tamanoPagina < 1 || tamanoPagina > 50)
            return BadRequest("La página debe ser positiva y el tamaño de página debe estar entre 1 y 50.");

        var modelo = new DetalleOportunidadViewModel
        {
            ViajeId = viajeId, Pagina = pagina, TamanoPagina = tamanoPagina
        };
        try
        {
            modelo.Detalle = salidaId.HasValue
                ? await _oportunidades.ObtenerDetalleSalidaAsync(conductorId, viajeId, salidaId.Value, envioId, cancellationToken)
                : await _oportunidades.ObtenerDetalleAsync(conductorId, viajeId, envioId, cancellationToken);
            if (modelo.Detalle == null) return NotFound();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception error) when (error is DbException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            modelo.Error = "No se pudo cargar el detalle. Intentá nuevamente más tarde.";
        }
        return View("~/Views/Panel/DetalleOportunidad.cshtml", modelo);
    }

    [HttpPost("SeleccionarSalida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeleccionarSalida(SeleccionSalidaDto seleccion, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest("La fecha y los horarios de salida no son válidos.");
        try
        {
            var id = await _oportunidades.SeleccionarSalidaAsync(conductorId, seleccion, cancellationToken);
            if (!id.HasValue) return NotFound();
            return RedirectToAction(nameof(Index), new { viajeId = seleccion.ViajeId, salidaId = id.Value });
        }
        catch (ArgumentException e) { TempData["ErrorSalida"] = e.Message; return RedirectToAction(nameof(Index), new { viajeId = seleccion.ViajeId }); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is DbException or Microsoft.EntityFrameworkCore.DbUpdateException or TimeoutException)
        { return StatusCode(503, "No se pudo guardar la salida. Recargá e intentá nuevamente."); }
    }

    [HttpPost("Aceptar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aceptar(int viajeId, int envioId, int? salidaId = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Los parámetros no son válidos.");
        if (viajeId <= 0 || envioId <= 0) return NotFound();
        if (!salidaId.HasValue || salidaId <= 0) return BadRequest("Seleccioná una salida concreta antes de aceptar.");
        if (_envios == null) return StatusCode(503, "No se pudo preparar la aceptación.");
        try
        {
            var resultado = await _envios.AceptarSalidaAsync(conductorId, viajeId, salidaId.Value, envioId, cancellationToken);
            if (resultado.Estado == EstadoAceptacion.NoEncontrado) return NotFound();
            if (resultado.Exito)
            {
                TempData["Exito"] = resultado.Estado == EstadoAceptacion.YaAceptado
                    ? $"El envío #{envioId} ya estaba asignado a esta salida. Podés consultarlo en tus transportes."
                    : $"El envío #{envioId} quedó asignado correctamente a tu salida.";
                return RedirectToAction("Index", "Transportes");
            }
            var mensaje = resultado.Estado switch
            {
                EstadoAceptacion.EnvioNoDisponible => "El envío ya fue asignado o dejó de estar disponible.",
                EstadoAceptacion.SalidaNoValida => "La salida ya no es válida, futura o disponible. Revisá el viaje y la fecha.",
                EstadoAceptacion.CapacidadInsuficiente => "No hay capacidad suficiente de peso o cupos para aceptar este envío.",
                EstadoAceptacion.Incompatible => "El envío no es compatible con esta salida.",
                EstadoAceptacion.DatosPendientes => "No se puede aceptar: quedan datos pendientes para confirmar la compatibilidad.",
                _ => "Hubo un conflicto de concurrencia al confirmar. Recargá los datos y volvé a intentar."
            };
            var motivos = (resultado.Evaluacion?.DatosPendientes ?? Array.Empty<DatoPendienteOportunidad>()).Select(MensajesEvaluacionOportunidad.DatoPendiente)
                .Concat((resultado.Evaluacion?.ResultadoMotor?.Motivos ?? Array.Empty<MotivoCompatibilidad>()).Select(MensajesEvaluacionOportunidad.Motivo));
            TempData["ErrorAceptacion"] = string.Join(" ", new[] { mensaje }.Concat(motivos));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException or DbException)
        { TempData["ErrorAceptacion"] = "No se pudo confirmar la asignación. Revisá la disponibilidad del servicio y recargá antes de reintentar."; }
        return RedirectToAction(nameof(Index), new { viajeId, salidaId });
    }

    private int ObtenerConductorId()
    {
        var identificador = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(identificador, out var conductorId) ? conductorId : 0;
    }
}
