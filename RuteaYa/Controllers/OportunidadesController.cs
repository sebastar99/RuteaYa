using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Oportunidades")]
public class OportunidadesController : Controller
{
    private const string Vista = "~/Views/Panel/Oportunidades.cshtml";
    private readonly IOportunidadesLogica _oportunidades;

    public OportunidadesController(IOportunidadesLogica oportunidades)
    {
        ArgumentNullException.ThrowIfNull(oportunidades);
        _oportunidades = oportunidades;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(
        int? viajeId = null, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Los parámetros de la consulta no son válidos.");
        if (viajeId.HasValue && viajeId.Value <= 0) return NotFound();

        try
        {
            var listado = await _oportunidades.ConsultarAsync(
                conductorId, viajeId, pagina, tamanoPagina, cancellationToken);
            if (listado == null) return NotFound();
            return View(Vista, new OportunidadesViewModel { Listado = listado });
        }
        catch (ArgumentOutOfRangeException)
        {
            return BadRequest("La página debe ser positiva y el tamaño de página debe estar entre 1 y 50.");
        }
        catch (Exception error) when (error is DbException or TimeoutException)
        {
            // No mostrar mensajes del proveedor, conexiones ni datos internos.
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View(Vista, new OportunidadesViewModel
            {
                Error = "No se pudieron cargar los candidatos. Intentá nuevamente más tarde."
            });
        }
    }

    [HttpGet("Detalle")]
    public async Task<IActionResult> Detalle(
        int viajeId, int envioId, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Los parámetros de la consulta no son válidos.");
        if (viajeId <= 0 || envioId <= 0) return NotFound();
        if (pagina < 1 || tamanoPagina < 1 || tamanoPagina > 50)
            return BadRequest("La página debe ser positiva y el tamaño de página debe estar entre 1 y 50.");

        var modelo = new DetalleOportunidadViewModel
        {
            ViajeId = viajeId, Pagina = pagina, TamanoPagina = tamanoPagina
        };
        try
        {
            modelo.Detalle = await _oportunidades.ObtenerDetalleAsync(
                conductorId, viajeId, envioId, cancellationToken);
            if (modelo.Detalle == null) return NotFound();
        }
        catch (Exception error) when (error is DbException or TimeoutException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            modelo.Error = "No se pudo cargar el detalle. Intentá nuevamente más tarde.";
        }
        return View("~/Views/Panel/DetalleOportunidad.cshtml", modelo);
    }

    [HttpPost("Aceptar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Aceptar(
        int viajeId, int envioId, [FromServices] EnviosLogica envios)
    {
        var conductorId = ObtenerConductorId();
        if (conductorId <= 0) return Forbid();
        if (viajeId <= 0 || envioId <= 0) return NotFound();

        try
        {
            await envios.AceptarAsync(conductorId, viajeId, envioId);
            TempData["Exito"] = "Aceptaste el envío. Ya podés gestionarlo desde Mis transportes.";
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction("Index", "Transportes");
    }

    private int ObtenerConductorId()
    {
        var identificador = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(identificador, out var conductorId) ? conductorId : 0;
    }
}
