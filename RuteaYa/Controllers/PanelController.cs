
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RuteaYa.Web.Controllers
{
    [Authorize]
    public class PanelController : Controller
    {
        public IActionResult Viajes()
        {
            return View();
        }

        public IActionResult NuevoViaje()
        {
            return View();
        }

        public IActionResult DetalleViaje()
        {
            return View();
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
