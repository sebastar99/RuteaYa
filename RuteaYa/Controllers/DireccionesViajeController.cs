using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers;

[Authorize, Route("DireccionesViaje/{formulario}"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DireccionesViajeController(FormulariosViajeServicio formularios, ILogger<DireccionesViajeController> logger) : Controller
{
    private int Usuario => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var id) ? id : 0;
    [HttpGet("Sugerencias")]
    public Task<IActionResult> Sugerencias(string formulario, string campo, string texto, CancellationToken ct) => Ejecutar(async () => Json(await formularios.SugerirAsync(Usuario, formulario, campo, texto, ct)));
    [HttpPost("Resolver"), ValidateAntiForgeryToken]
    public Task<IActionResult> Resolver(string formulario, string campo, string token, CancellationToken ct) => Ejecutar(async () => Json(await formularios.ResolverAsync(Usuario, formulario, campo, token, ct)));
    [HttpPost("Confirmar"), ValidateAntiForgeryToken]
    public Task<IActionResult> Confirmar(string formulario, string campo, string token, int? acceso, CancellationToken ct) => Ejecutar(async () => Json(await formularios.ConfirmarAsync(Usuario, formulario, campo, token, acceso, ct)));
    [HttpPost("Paradas"), ValidateAntiForgeryToken]
    public Task<IActionResult> Parada(string formulario, CancellationToken ct) => Ejecutar(async () => PartialView("~/Views/Shared/_ParadaViajeDireccion.cshtml", await formularios.AgregarParadaAsync(Usuario, formulario, ct)));
    private async Task<IActionResult> Ejecutar(Func<Task<IActionResult>> accion)
    {
        if (Usuario <= 0) return Forbid();
        if (!ModelState.IsValid) return BadRequest(new { mensaje = "Los parámetros no son válidos." });
        try { return await accion(); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception e) when (e is ArgumentException or ComprobanteUbicacionException) { return StatusCode(409, new { mensaje = e.Message }); }
        catch (Exception e) {
            logger.LogWarning("Direcciones de Viaje: tipo {Tipo}, HTTP {Estado}, referencia {Referencia}. Sin URL ni datos remotos.", e.GetType().Name, (int?)(e as HttpRequestException)?.StatusCode, HttpContext.TraceIdentifier);
            var mensaje = e switch {
                HttpRequestException http => MensajesHerePublicos.Http((int?)http.StatusCode),
                TimeoutException => "HERE no respondió a tiempo. Reintentá la búsqueda.",
                HereNoConfiguradoException => "HERE no está configurado en los secretos de MVC.",
                _ => "No se pudo preparar la ubicación. Revisá la API y los servicios HERE e intentá nuevamente."
            };
            return StatusCode(e is TimeoutException ? 504 : 503, new { mensaje, referencia = HttpContext.TraceIdentifier });
        }
    }
}
