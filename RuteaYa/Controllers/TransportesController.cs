using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Transportes")]
public class TransportesController : Controller
{
    private const string Vista = "~/Views/Panel/Transportes.cshtml";
    private readonly EnviosLogica _enviosLogica;

    public TransportesController(EnviosLogica enviosLogica)
    {
        ArgumentNullException.ThrowIfNull(enviosLogica);
        _enviosLogica = enviosLogica;
    }

    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index()
    {
        var conductorId = ObtenerUsuarioId();
        if (conductorId <= 0) return Forbid();

        try
        {
            return View(Vista, await _enviosLogica.ListarTransportesAsync(conductorId));
        }
        catch (Exception error) when (error is DbException or TimeoutException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ViewBag.Error = "No se pudieron cargar los transportes. Intentá nuevamente más tarde.";
            return View(Vista, new List<EnvioDto>());
        }
    }

    [HttpPost("Avanzar/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Avanzar(int id, EstadoEnvio estado)
    {
        if (id <= 0) return NotFound();

        var conductorId = ObtenerUsuarioId();
        if (conductorId <= 0) return Forbid();

        try
        {
            var actualizado = await _enviosLogica.ActualizarEstadoConductorAsync(conductorId, id, estado);

            if (!actualizado) return NotFound();

            TempData["Exito"] = "El estado del envío se actualizó correctamente.";
        }
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private int ObtenerUsuarioId()
    {
        var identificador = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return int.TryParse(identificador, out var id) ? id : 0;
    }
}
