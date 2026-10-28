
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers
{
    [Authorize]
    public class PanelController : Controller
    {
        private readonly IViajesApiServicio _viajesApiServicio;

        public PanelController(IViajesApiServicio viajesApiServicio)
        {
            _viajesApiServicio = viajesApiServicio;
        }

        private int ObtenerUsuarioId()
        {
            var identificador =
                User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            return int.TryParse(identificador, out var usuarioId) ? usuarioId : 0;
        }

        [HttpGet]
        public async Task<IActionResult> Viajes()
        {
            var usuarioId = ObtenerUsuarioId();

            if (usuarioId <= 0)
            {
                return Forbid();
            }

            var resultado = await _viajesApiServicio.ListarViajesAsync(usuarioId);

            ViewBag.Error = resultado.Exito ? null : resultado.Mensaje;

            return View(resultado.Viajes);
        }

        [HttpGet]
        public IActionResult NuevoViaje()
        {
            return View(new ViajeViewModel());
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NuevoViaje(ViajeViewModel modelo)
        {
            if (modelo.DiasSemana == null || modelo.DiasSemana.Count == 0)
            {
                ModelState.AddModelError(
                    nameof(modelo.DiasSemana),
                    "Seleccionar al menos un dia de circulacion."
                );
            }

            if (modelo.HoraLlegada <= modelo.HoraSalida)
            {
                ModelState.AddModelError(
                    nameof(modelo.HoraLlegada),
                    "La hora de llegada debe ser despues de la hora de salida."
                );
            }

            if (!ModelState.IsValid)
            {
                return View(modelo);
            }

            var usuarioId = ObtenerUsuarioId();

            if (usuarioId <= 0)
            {
                return Forbid();
            }

            var resultado = await _viajesApiServicio.CrearViajeAsync(usuarioId, modelo);

            if (!resultado.Exito)
            {
                ModelState.AddModelError(string.Empty, resultado.Mensaje);

                return View(modelo);
            }

            TempData["MensajeExito"] = resultado.Mensaje;

            return RedirectToAction(nameof(Viajes));
        }

        [HttpGet]
        public async Task<IActionResult> DetalleViaje(int id)
        {
            if (id <= 0)
            {
                return NotFound();
            }
            var usuarioId = ObtenerUsuarioId();

            if (usuarioId <= 0)
            {
                return Forbid();
            }

            var resultado = await _viajesApiServicio.ObtenerViajePorIdAsync(usuarioId, id);

            if (!resultado.Exito || resultado.Viaje is null)
            {
                ViewBag.Error = resultado.Mensaje;

                return View (new ViajeViewModel {Id = id});
            }

            return View(resultado.Viaje);
        }

        public IActionResult Envios()
        {
            return View();
        }

        public IActionResult NuevoEnvio()
        {
            return View();
        }

        public IActionResult DetalleEnvio()
        {
            return View();
        }

        public IActionResult Oportunidades()
        {
            return View();
        }

        public IActionResult DetalleOportunidad()
        {
            return View();
        }

        public IActionResult Optimizador()
        {
            return View();
        }

        public IActionResult ConfirmarCombinacion()
        {
            return View();
        }

        public IActionResult Transportes()
        {
            return View();
        }

        public IActionResult Historial()
        {
            return View();
        }

        public IActionResult Potencial()
        {
            return View();
        }
    }
}
