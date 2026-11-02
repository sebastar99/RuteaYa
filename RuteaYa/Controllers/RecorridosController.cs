using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers;

[Authorize]
[Route("Recorridos/{viajeId:int}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RecorridosController(PrevisualizacionRecorridoServicio recorridos, MapasHereServicio mapas) : Controller
{
    private const string Vista = "~/Views/Recorridos/Previsualizacion.cshtml";
    private int UsuarioId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        User.FindFirstValue("sub"), out var id) ? id : 0;

    [HttpGet("")]
    public Task<IActionResult> Index(int viajeId, string? sesion, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = await recorridos.ObtenerAsync(UsuarioId, viajeId, sesion, ct);
        return modelo == null ? NotFound() : View(Vista, modelo);
    });

    [HttpPost("Buscar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Buscar(int viajeId, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = await recorridos.BuscarAsync(UsuarioId, viajeId, ct);
        return modelo == null ? NotFound() : RedirectToAction(nameof(Index), new { viajeId, sesion = modelo.Sesion });
    });

    [HttpPost("Confirmar")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16384)]
    public Task<IActionResult> Confirmar(int viajeId, ConfirmarRecorridoFormulario formulario, CancellationToken ct)
        => Ejecutar(viajeId, async () =>
        {
            var modelo = await recorridos.ConfirmarAsync(UsuarioId, viajeId, formulario, ct);
            return modelo == null ? NotFound() : RedirectToAction(nameof(Index), new { viajeId, sesion = modelo.Sesion });
        });

    [HttpGet("Atribuciones")]
    public Task<IActionResult> Atribuciones(int viajeId, string sesion, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = await recorridos.ObtenerAsync(UsuarioId, viajeId, sesion, ct);
        if (modelo?.Ruta == null) return NotFound();
        return Json(await mapas.AtribucionesAsync(ct));
    }, recurso: true);

    [HttpGet("Tesela/{z:int}/{x:int}/{y:int}")]
    public Task<IActionResult> Tesela(int viajeId, string sesion, int z, int x, int y, CancellationToken ct)
        => Ejecutar(viajeId, async () =>
        {
            var modelo = await recorridos.ObtenerAsync(UsuarioId, viajeId, sesion, ct);
            if (modelo?.Ruta == null) return NotFound();
            return File(await mapas.TeselaAsync(z, x, y, ct), "image/png");
        }, recurso: true);

    private async Task<IActionResult> Ejecutar(int viajeId, Func<Task<IActionResult>> accion, bool recurso = false)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (UsuarioId <= 0) return Forbid();
        if (viajeId <= 0) return NotFound();
        if (!ModelState.IsValid) return BadRequest("Los parámetros no son válidos.");
        try { return await accion(); }
        catch (ArgumentException)
        {
            if (recurso) return NotFound();
            Response.StatusCode = 400;
            return View(Vista, new PrevisualizacionRecorridoViewModel { ViajeId = viajeId,
                Error = "Revisá la selección de todas las ubicaciones. Si la sesión venció o el viaje cambió, buscá nuevamente." });
        }
        catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException or DbException)
        {
            if (recurso) return StatusCode(503);
            Response.StatusCode = 503;
            return View(Vista, new PrevisualizacionRecorridoViewModel { ViajeId = viajeId,
                Error = "No se pudo preparar el recorrido. Revisá la configuración de HERE e intentá nuevamente." });
        }
    }
}
