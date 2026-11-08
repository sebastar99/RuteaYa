using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Envios")]
public class EnviosController : Controller
{
    private const string VistaListado = "~/Views/Panel/Envios.cshtml";
    private const string VistaFormulario = "~/Views/Panel/NuevoEnvio.cshtml";
    private readonly EnviosLogica _enviosLogica;

    public EnviosController(EnviosLogica enviosLogica)
    {
        ArgumentNullException.ThrowIfNull(enviosLogica);
        _enviosLogica = enviosLogica;
    }

    // GET: /Envios (Pantalla 7 - Mis Envíos)
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(EstadoEnvio? estado, CancellationToken cancellationToken = default)
    {
        var usuarioId = ObtenerUsuarioIdActual();
        if (usuarioId <= 0) return Forbid();

        try
        {
            var envios = await _enviosLogica.ListarAsync(usuarioId, estado);
            return View(VistaListado, envios);
        }
        catch (Exception error) when (error is DbException or TimeoutException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ViewBag.Error = "No se pudieron cargar los envíos. Intentá nuevamente más tarde.";
            return View(VistaListado, Array.Empty<EnvioDto>());
        }
    }

    // GET: /Envios/Crear (Muestra la Pantalla 6)
    [HttpGet("Crear")]
    public IActionResult Crear()
    {
        return View(VistaFormulario);
    }

    // POST: /Envios/Crear (Procesa y guarda el nuevo envío)
    [HttpPost("Crear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(CrearEnvioDto dto)
    {
        if (!ModelState.IsValid)
            return View(VistaFormulario, dto);

        try
        {
            var usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId <= 0) return Forbid();

            await _enviosLogica.CrearAsync(usuarioId, dto);
            TempData["Exito"] = "El envío fue publicado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(VistaFormulario, dto);
        }
    }

    // POST: /Envios/Cancelar/5 (Cancelar paquete)
    [HttpPost("Cancelar/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar(int id)
    {
        if (id <= 0) return NotFound();

        try
        {
            var usuarioId = ObtenerUsuarioIdActual();
            if (usuarioId <= 0) return Forbid();

            await _enviosLogica.CancelarAsync(usuarioId, id);
            TempData["Exito"] = "El envío fue cancelado con éxito.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private int ObtenerUsuarioIdActual()
    {
        var usuarioIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(usuarioIdClaim, out int id) ? id : 0;
    }
}