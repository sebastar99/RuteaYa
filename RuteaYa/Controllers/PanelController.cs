
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

        [HttpGet]
        public async Task<IActionResult> EditarViaje(int id)
        {
            if(id <= 0)
            {
                return NotFound();
            }

            var usuarioId = ObtenerUsuarioId();
            if(usuarioId <= 0)
            {
                return Forbid();
            }

            var resultado = await _viajesApiServicio.ObtenerViajePorIdAsync(usuarioId, id);

            if(!resultado.Exito || resultado.Viaje is null)
            {
                TempData["MensajeError"] = resultado.Mensaje;
                return RedirectToAction(nameof(Viajes));
            }

            var modelo = resultado.Viaje;

            modelo.ParadasTexto = string.Join(
                ", ",
                (modelo.Paradas ?? new List<ParadaViewModel>())
                .OrderBy(p => p.Orden)
                .Select(p => p.Ubicacion)
            );

            return View(modelo);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarViaje(ViajeViewModel modelo)
        {
            if(modelo.DiasSemana == null || modelo.DiasSemana.Count == 0 ) 
            {
                ModelState.AddModelError(
                    nameof(modelo.DiasSemana),
                    "Selecciona al menos un dia de circulacion."
                );
            }

            if(modelo.HoraLlegada <= modelo.HoraSalida)
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
            if(usuarioId <= 0)
            {
                return Forbid();
            }

            var resultado = await _viajesApiServicio.EditarViajeAsync(
                usuarioId,
                modelo.Id,
                modelo
            );

            if (!resultado.Exito)
            {
                ModelState.AddModelError(string.Empty, resultado.Mensaje);
                return View(modelo);
            }

            TempData["MensajeExito"] = resultado.Mensaje;

            return RedirectToAction(nameof(DetalleViaje), new {id = modelo.Id});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DesactivarViaje(int id)
        {
            if(id <= 0)
            {
                return NotFound();
            }

            var usuarioId = ObtenerUsuarioId();
            if(usuarioId <= 0)
            {
                return Forbid();
            }

            var resultado = await _viajesApiServicio.DesactivarViajeAsync(usuarioId, id);

            if (!resultado.Exito)
            {
                TempData["MensajeError"] = resultado.Mensaje;
            }
            else
            {
                TempData["MensajeExito"] = resultado.Mensaje;
            }

            return RedirectToAction(nameof(DetalleViaje), new {id});
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgregarParada(int viajeId, string ubicacion, int orden) 
        {
            if (viajeId <= 0)
            {
                return NotFound();
            }

            var usuarioId = ObtenerUsuarioId();

            if (usuarioId <= 0)
            {
                return Forbid();
            }
           
            if (string.IsNullOrWhiteSpace(ubicacion))
            {
                TempData["MensajeError"] = "Ingresar la ubicacion de la parada.";
                return RedirectToAction(nameof(DetalleViaje), new { id = viajeId });
            }

            var resultado = await _viajesApiServicio.AgregarParadaAsync(
                usuarioId,
                viajeId,
                ubicacion.Trim(),
                orden
            );

            if (!resultado.Exito)
            {
                TempData["MensajeError"] = resultado.Mensaje;
            }
            else
            {
                TempData["MensajeExito"] = resultado.Mensaje;
            }
                
            return RedirectToAction(nameof(DetalleViaje), new { id = viajeId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EliminarParada(int viajeId, int paradaId)
        {
            if (viajeId <= 0 || paradaId <= 0)
            {
                return NotFound();
            }

            var usuarioId = ObtenerUsuarioId();

            if (usuarioId <= 0)
            {
                return Forbid();
            }
                
            var resultado = await _viajesApiServicio.EliminarParadaAsync(
                usuarioId,
                viajeId,
                paradaId
            );

            if (!resultado.Exito)
            {
                TempData["MensajeError"] = resultado.Mensaje;
            }
            else
            {
                TempData["MensajeExito"] = resultado.Mensaje;
            }
                
            return RedirectToAction(nameof(DetalleViaje), new { id = viajeId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReordenarParadas(int viajeId, List<int> paradasIds)
        {
            if (viajeId <= 0)
            {
                return NotFound();
            }
                
            var usuarioId = ObtenerUsuarioId();
            if (usuarioId <= 0)
            {
                return Forbid();
            }
                
            if (paradasIds == null || paradasIds.Count == 0)
            {
                TempData["MensajeError"] = "No hay paradas para reordenar.";
                return RedirectToAction(nameof(DetalleViaje), new { id = viajeId });
            }

            var resultado = await _viajesApiServicio.ReordenarParadasAsync(
                usuarioId,
                viajeId,
                paradasIds
            );

            if (!resultado.Exito)
            {
                TempData["MensajeError"] = resultado.Mensaje;
            }
            else
            {
                TempData["MensajeExito"] = resultado.Mensaje;
            }
                
            return RedirectToAction(nameof(DetalleViaje), new { id = viajeId });
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
