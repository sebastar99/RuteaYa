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
public class RecorridosController(PrevisualizacionRecorridoServicio recorridos, MapasHereServicio mapas,
    ILogger<RecorridosController>? logger = null) : Controller
{
    private const string Vista = "~/Views/Recorridos/Previsualizacion.cshtml";
    private int UsuarioId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        User.FindFirstValue("sub"), out var id) ? id : 0;

    [HttpGet("")]
    public Task<IActionResult> Index(int viajeId, string? sesion, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = string.IsNullOrEmpty(sesion)
            ? await recorridos.IniciarAutocompletadoAsync(UsuarioId, viajeId, ct)
            : await recorridos.ObtenerAsync(UsuarioId, viajeId, sesion, ct);
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

    [HttpPost("Autocompletar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Autocompletar(int viajeId, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = await recorridos.IniciarAutocompletadoAsync(UsuarioId, viajeId, ct);
        return modelo == null ? NotFound() : RedirectToAction(nameof(Index), new { viajeId, sesion = modelo.Sesion });
    });

    [HttpGet("Direcciones/Sugerencias")]
    public Task<IActionResult> Sugerencias(int viajeId, string sesion, int ubicacion, string texto,
        [FromServices] SeleccionDireccionServicio direcciones, CancellationToken ct) =>
        EjecutarDireccion(viajeId, sesion, ubicacion, async ambito =>
            Json(await direcciones.SugerirAsync(UsuarioId, ambito, texto, ct)), ct);

    [HttpPost("Direcciones/Resolver")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ResolverDireccion(int viajeId, string sesion, int ubicacion, string token,
        [FromServices] SeleccionDireccionServicio direcciones, CancellationToken ct) =>
        EjecutarDireccion(viajeId, sesion, ubicacion, async ambito =>
            Json(await direcciones.ResolverAsync(UsuarioId, ambito, token, ct)), ct);

    [HttpPost("Direcciones/Confirmar")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmarDireccion(int viajeId, string sesion, int ubicacion, string token, int? acceso,
        [FromServices] SeleccionDireccionServicio direcciones, CancellationToken ct) =>
        EjecutarDireccion(viajeId, sesion, ubicacion, ambito =>
            Task.FromResult<IActionResult>(Json(direcciones.Confirmar(UsuarioId, ambito, token, acceso))), ct);

    [HttpPost("ConfirmarDirecciones")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(16384)]
    public Task<IActionResult> ConfirmarDirecciones(int viajeId, ConfirmarDireccionesRecorridoFormulario formulario,
        [FromServices] SeleccionDireccionServicio direcciones, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = await recorridos.ConfirmarDireccionesAsync(UsuarioId, viajeId, formulario, direcciones, ct);
        if (modelo == null) return NotFound();
        if (SolicitaJson)
            return Json(MapaRecorridoViewModel.Crear(modelo,
                Url.Action(nameof(Tesela), new { viajeId, sesion = modelo.Sesion, z = 0, x = 0, y = 0 }),
                Url.Action(nameof(Atribuciones), new { viajeId, sesion = modelo.Sesion })));
        return RedirectToAction(nameof(Index), new { viajeId, sesion = modelo.Sesion });
    });

    private Task<IActionResult> EjecutarDireccion(int viajeId, string sesion, int ubicacion,
        Func<string, Task<IActionResult>> accion, CancellationToken ct) => Ejecutar(viajeId, async () =>
    {
        var modelo = await recorridos.ObtenerAsync(UsuarioId, viajeId, sesion, ct);
        if (modelo == null || !modelo.UsaAutocompletado || ubicacion < 0 || ubicacion >= modelo.Ubicaciones.Count) return NotFound();
        try { return await accion(PrevisualizacionRecorridoServicio.AmbitoDireccion(sesion, ubicacion)); }
        catch (SeleccionTemporalNoDisponibleException) { throw; }
        catch (ArgumentException) { return ErrorPublico(viajeId, 400, "seleccion_invalida", "Revisá el texto, la sugerencia y el punto elegido.", true); }
    }, recurso: true);

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

    private bool SolicitaJson => Request.Headers.Accept.Any(v => v?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

    private IActionResult ErrorPublico(int viajeId, int estado, string codigo, string mensaje, bool recurso)
    {
        if (recurso || SolicitaJson)
            return StatusCode(estado, new { codigo, mensaje, referencia = HttpContext.TraceIdentifier });
        Response.StatusCode = estado;
        return View(Vista, new PrevisualizacionRecorridoViewModel { ViajeId = viajeId,
            Error = mensaje + $" Referencia: {HttpContext.TraceIdentifier}" });
    }

    private void Diagnosticar(Exception error, int? estado = null) => logger?.LogWarning(
        "Recorridos: operación {Operacion}, tipo {Tipo}, HTTP proveedor {Estado}, referencia {Referencia}. Sin URL/cuerpo/excepción remota.",
        ControllerContext.ActionDescriptor.ActionName, error.GetType().Name, estado, HttpContext.TraceIdentifier);

    private async Task<IActionResult> Ejecutar(int viajeId, Func<Task<IActionResult>> accion, bool recurso = false)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        if (UsuarioId <= 0) return Forbid();
        if (viajeId <= 0) return NotFound();
        if (!ModelState.IsValid) return ErrorPublico(viajeId, 400, "parametros_invalidos", "Los parámetros no son válidos. Revisá las ubicaciones.", recurso);
        try { return await accion(); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // AbortController, navegación o desconexión: no es un error del proveedor ni una pantalla de error.
            logger?.LogDebug("Solicitud de recorridos cancelada por el cliente. Referencia {Referencia}.", HttpContext.TraceIdentifier);
            return StatusCode(499);
        }
        catch (SeleccionTemporalNoDisponibleException error)
        {
            return ErrorPublico(viajeId, 409, "seleccion_vencida", error.Message, recurso);
        }
        catch (ArgumentException)
        {
            return ErrorPublico(viajeId, 400, "ubicaciones_pendientes", "Elegí y confirmá todas las ubicaciones y sus puntos de navegación antes de calcular.", recurso);
        }
        catch (HttpRequestException error)
        {
            var estado = (int?)error.StatusCode;
            Diagnosticar(error, estado);
            var mensaje = estado switch {
                401 => "HERE rechazó la autenticación (401). Revisá la configuración privada de la clave.",
                403 => "HERE no autorizó este servicio (403). Revisá los permisos de la aplicación HERE.",
                429 => "HERE alcanzó el límite de solicitudes (429). Esperá un momento antes de reintentar.",
                >= 500 => "HERE no está disponible temporalmente. Intentá nuevamente más tarde.",
                null => "No se pudo conectar con HERE. Revisá la conexión del servidor.",
                _ => $"HERE rechazó la consulta (HTTP {estado}). Revisá las direcciones elegidas."
            };
            return ErrorPublico(viajeId, 503, estado.HasValue ? $"here_{estado}" : "here_conexion", mensaje, recurso);
        }
        catch (TimeoutException error)
        {
            Diagnosticar(error);
            return ErrorPublico(viajeId, 504, "here_timeout", "HERE no respondió a tiempo. Reintentá el cálculo o la búsqueda.", recurso);
        }
        catch (OperationCanceledException error)
        {
            Diagnosticar(error);
            return ErrorPublico(viajeId, 500, "cancelacion_inesperada", "El servicio canceló la operación inesperadamente. Reintentá y conservá la referencia si vuelve a ocurrir.", recurso);
        }
        catch (HereNoConfiguradoException error)
        {
            Diagnosticar(error);
            return ErrorPublico(viajeId, 503, "here_no_configurado", "HERE no está configurado en el servidor. Configurá HereRouting:ApiKey en los secretos de MVC.", recurso);
        }
        catch (InvalidOperationException error)
        {
            Diagnosticar(error);
            return ErrorPublico(viajeId, 503, "here_respuesta_invalida", "HERE no devolvió una ubicación o ruta utilizable. Revisá las direcciones y consultá la referencia del error si vuelve a ocurrir.", recurso);
        }
        catch (DbException error)
        {
            Diagnosticar(error);
            return ErrorPublico(viajeId, 503, "viaje_no_disponible", "No se pudo leer el viaje. Intentá nuevamente más tarde.", recurso);
        }
        catch (Exception error)
        {
            logger?.LogError("Fallo interno de recorridos, tipo {Tipo}, referencia {Referencia}. Sin datos del proveedor.", error.GetType().Name, HttpContext.TraceIdentifier);
            return ErrorPublico(viajeId, 500, "error_interno", "Ocurrió un error interno. Conservá la referencia y consultá los registros del servidor.", recurso);
        }
    }
}
