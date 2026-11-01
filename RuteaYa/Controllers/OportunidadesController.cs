using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Controllers;

[Authorize]
public class OportunidadesController : Controller
{
    private const string Vista = "~/Views/Panel/Oportunidades.cshtml";
    private readonly IOportunidadesLogica _oportunidades;

    public OportunidadesController(IOportunidadesLogica oportunidades)
    {
        ArgumentNullException.ThrowIfNull(oportunidades);
        _oportunidades = oportunidades;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int? viajeId = null, int pagina = 1, int tamanoPagina = 10,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated != true) return Challenge();
        var identificador = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(identificador, out var conductorId) || conductorId <= 0) return Forbid();
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
}
