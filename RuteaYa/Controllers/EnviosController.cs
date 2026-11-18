using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using System;
using System.Data.Common;
using System.Threading.Tasks;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Envios")]
public class EnviosController : Controller
{
    private const string VistaListado = "~/Views/Panel/Envios.cshtml";
    private const string VistaFormulario = "~/Views/Panel/NuevoEnvio.cshtml";
    private const string VistaDetalle = "~/Views/Panel/DetalleEnvio.cshtml";
    private readonly EnviosLogica _enviosLogica;
    private readonly FormulariosDireccionesServicio? _formularios;

    public EnviosController(EnviosLogica enviosLogica, FormulariosDireccionesServicio? formularios = null)
    {
        ArgumentNullException.ThrowIfNull(enviosLogica);
        _enviosLogica = enviosLogica;
        _formularios = formularios;
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
    public async Task<IActionResult> Crear()
    {
        var usuarioId = ObtenerUsuarioIdActual(); if (usuarioId <= 0) return Forbid();
        // El retiro sin restricción es una elección visible del formulario: viene tildada y se destilda al indicar un horario.
        var modelo = new CrearEnvioViewModel { RetiroSinRestriccion = true };
        try {
            if (_formularios == null) throw new InvalidOperationException("El formulario de direcciones no está configurado.");
            await _formularios.PrepararEnvioAsync(usuarioId, modelo, HttpContext.RequestAborted);
        }
        catch (ErroresDireccionesFormularioException e) { foreach (var error in e.Errores) ModelState.AddModelError(error.Key, error.Value); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { ModelState.AddModelError("", e.Message); }
        return View(VistaFormulario, modelo);
    }

    // POST: /Envios/Crear (Procesa y guarda el nuevo envío)
    [HttpPost("Crear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(CrearEnvioViewModel dto)
    {
        var usuarioId = ObtenerUsuarioIdActual(); if (usuarioId <= 0) return Forbid();
        dto.Ubicaciones = null; // El endpoint anterior tampoco puede recibir contextos/FK/coordenadas libres.
        try {
            if (_formularios == null) throw new InvalidOperationException("Volvé a abrir el formulario y confirmá las dos direcciones.");
            _formularios.PrepararGuardadoEnvio(usuarioId, dto);
        }
        catch (ErroresDireccionesFormularioException e) { foreach (var error in e.Errores) ModelState.AddModelError(error.Key, error.Value); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { ModelState.AddModelError("", e.Message); }
        if (!ModelState.IsValid) { Rellenar(usuarioId, dto); return View(VistaFormulario, dto); }

        try
        {
            await _enviosLogica.CrearAsync(usuarioId, dto);
            TempData["Exito"] = "El envío fue publicado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ComprobanteUbicacionException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError(string.Empty, "No se pudo publicar el envío. Revisá la disponibilidad del servicio e intentá nuevamente.");
        }
        Rellenar(usuarioId, dto);
        return View(VistaFormulario, dto);
    }

    private void Rellenar(int usuarioId, CrearEnvioViewModel dto)
    {
        try { _formularios?.RellenarEnvio(usuarioId, dto); }
        catch (ArgumentException ex) { ModelState.AddModelError("", ex.Message); }
    }

    // GET: /Envios/Detalle/5 (Muestra el detalle del envío)
    [HttpGet("Detalle/{id:int}")]
    public async Task<IActionResult> Detalle(int id)
    {
        if (id <= 0) return NotFound();

        var usuarioId = ObtenerUsuarioIdActual();
        if (usuarioId <= 0) return Forbid();

        try
        {
            var envio = await _enviosLogica.ObtenerAsync(usuarioId, id);
            if (envio == null) return NotFound();

            return View(VistaDetalle, envio);
        }
        catch (Exception error) when (error is DbException or TimeoutException)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ViewBag.Error = "No se pudo cargar el detalle del envío. Intentá nuevamente más tarde.";
            return View(VistaDetalle, null);
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
        catch (ArgumentException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (Exception)
        {
            TempData["Error"] = "No se pudo cancelar el envío. Intentá nuevamente más tarde.";
        }

        return RedirectToAction(nameof(Index));
    }

    private int ObtenerUsuarioIdActual()
    {
        var usuarioIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return int.TryParse(usuarioIdClaim, out int id) && id > 0 ? id : 0;
    }
}
