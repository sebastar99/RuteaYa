using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Optimizador")]
public class OptimizadorController : Controller
{
    private readonly IOportunidadesLogica _oportunidades;
    private readonly IOptimizadorServicio _optimizador;
    private readonly EnviosLogica _envios;
    private readonly RuteaYaDbContext _db;

    public OptimizadorController(IOportunidadesLogica oportunidades, IOptimizadorServicio optimizador,
        EnviosLogica envios, RuteaYaDbContext db)
    {
        ArgumentNullException.ThrowIfNull(oportunidades);
        ArgumentNullException.ThrowIfNull(optimizador);
        ArgumentNullException.ThrowIfNull(envios);
        ArgumentNullException.ThrowIfNull(db);
        _oportunidades = oportunidades; _optimizador = optimizador; _envios = envios; _db = db;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(int? viajeId = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        var modelo = await PrepararSelector(conductorId, viajeId, cancellationToken);
        if (modelo == null) return NotFound();
        return View(modelo);
    }

    private async Task<OptimizadorViewModel?> PrepararSelector(int conductorId, int? viajeId, CancellationToken ct)
    {
        var viajes = await _db.Viajes.AsNoTracking().Where(v => v.UsuarioId == conductorId).OrderByDescending(v => v.Id)
            .Select(v => new ViajeOportunidadesDto { Id = v.Id, Origen = v.Origen, Destino = v.Destino, Activo = v.Activo, DesvioMaxKm = v.DesvioMaxKm })
            .ToListAsync(ct);
        var modelo = new OptimizadorViewModel { Viajes = viajes, Seleccion = new() { ViajeId = viajeId ?? 0, LlegadaSinRestriccion = true } };
        if (!viajeId.HasValue) return modelo;
        modelo.ViajeSeleccionado = viajes.FirstOrDefault(v => v.Id == viajeId.Value);
        if (modelo.ViajeSeleccionado == null) return null;
        var v = await _db.Viajes.AsNoTracking().Include(v => v.Dias).Include(v => v.OrigenUbicacion)
            .SingleAsync(v => v.Id == viajeId.Value && v.UsuarioId == conductorId, ct);
        modelo.DiasHabilitados = v.Dias.Select(d => d.DiaSemana).ToArray();
        modelo.HoraSalida = v.HoraSalida;
        modelo.ZonaAutomatica = ZonaHorariaViaje.Automatica(v);
        return modelo;
    }

    [HttpPost("Salida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salida(SeleccionSalidaDto seleccion, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (seleccion.ViajeId <= 0) return NotFound();
        try
        {
            if (!ModelState.IsValid) return await Invalido();
            var id = await _oportunidades.SeleccionarSalidaAsync(conductorId, seleccion, cancellationToken);
            if (!id.HasValue) return NotFound();
            return RedirectToAction(nameof(Resultado), new { viajeId = seleccion.ViajeId, salidaId = id.Value });
        }
        catch (ArgumentException e)
        {
            ModelState.AddModelError(e.ParamName ?? "", e.Message.Split(" (Parameter", StringSplitOptions.None)[0]);
            return await Invalido();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is DbException or DbUpdateException or TimeoutException)
        { return StatusCode(503, "No se pudo guardar la salida. Recargá e intentá nuevamente."); }

        async Task<IActionResult> Invalido()
        {
            var modelo = await PrepararSelector(conductorId, seleccion.ViajeId, cancellationToken);
            if (modelo == null) return NotFound();
            modelo.Seleccion = seleccion;
            return View("Index", modelo);
        }
    }

    [HttpGet("Resultado")]
    public async Task<IActionResult> Resultado(int viajeId, int salidaId, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (viajeId <= 0 || salidaId <= 0) return NotFound();
        try
        {
            var resultado = await _optimizador.OptimizarAsync(conductorId, viajeId, salidaId, cancellationToken);
            if (resultado == null) return NotFound();
            return View(new OptimizadorResultadoViewModel { Resultado = resultado, Error = TempData["ErrorCombinacion"] as string });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is DbException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(new OptimizadorResultadoViewModel { Resultado = new() { ViajeId = viajeId, SalidaId = salidaId },
                Error = "No se pudo calcular la combinación. Revisá la disponibilidad del servicio de rutas e intentá nuevamente." });
        }
    }

    [HttpGet("Confirmar")]
    public async Task<IActionResult> Confirmar(int viajeId, int salidaId, [FromQuery] int[] envioIds,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (viajeId <= 0 || salidaId <= 0 || envioIds == null || envioIds.Length == 0 || envioIds.Any(i => i <= 0)
            || envioIds.Distinct().Count() != envioIds.Length) return NotFound();
        try
        {
            var evaluacion = await _optimizador.EvaluarSeleccionAsync(conductorId, viajeId, salidaId, envioIds, cancellationToken);
            if (evaluacion == null) return NotFound();
            return View(new ConfirmarCombinacionViewModel { Evaluacion = evaluacion, EnvioIds = envioIds,
                Error = TempData["ErrorCombinacion"] as string });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is DbException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(new ConfirmarCombinacionViewModel { EnvioIds = envioIds,
                Evaluacion = new() { ViajeId = viajeId, SalidaId = salidaId },
                Error = "No se pudo reevaluar la combinación. Intentá nuevamente." });
        }
    }

    [HttpPost("Confirmar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmar(int viajeId, int salidaId, [FromForm] int[] envioIds, string? huella,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (viajeId <= 0 || salidaId <= 0 || envioIds == null || envioIds.Length == 0 || envioIds.Any(i => i <= 0)
            || envioIds.Distinct().Count() != envioIds.Length) return NotFound();
        ResultadoReservaDto reserva;
        try
        {
            reserva = await _envios.ReservarCombinacionAsync(conductorId, viajeId, salidaId, envioIds, huella, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is DbException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            TempData["ErrorCombinacion"] = "No se pudo confirmar la combinación. Recargá e intentá nuevamente.";
            return RedirectToAction(nameof(Confirmar), new { viajeId, salidaId, envioIds });
        }

        if (reserva.Exito)
        {
            var eval = reserva.Evaluacion;
            var restantes = eval == null ? "" :
                $" En esa salida te quedan {Math.Max(0, eval.CuposDisponibles - envioIds.Length)} cupos.";
            TempData["Exito"] = reserva.Estado == EstadoReserva.YaReservado
                ? "La combinación ya estaba reservada." : $"Combinación reservada: {envioIds.Length} paquete(s) asignados a tu salida.{restantes}";
            return RedirectToAction("Index", "Transportes");
        }
        if (reserva.Estado == EstadoReserva.NoEncontrado) return NotFound();
        TempData["ErrorCombinacion"] = reserva.Estado switch
        {
            EstadoReserva.Cambio => "Los datos cambiaron desde el cálculo. Revisá el resumen o volvé a calcular.",
            EstadoReserva.EnvioNoDisponible => reserva.Mensaje ?? "Alguno de los envíos fue tomado por otro conductor.",
            EstadoReserva.SalidaNoValida => "La salida ya no está disponible.",
            EstadoReserva.ConflictoConcurrencia => "Hubo un conflicto con otra operación. Reintentá.",
            _ => reserva.Mensaje ?? "La combinación ya no cumple las condiciones."
        };
        return RedirectToAction(nameof(Confirmar), new { viajeId, salidaId, envioIds });
    }

    private int ObtenerConductorId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : 0;
}
