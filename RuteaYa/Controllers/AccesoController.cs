
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers
{
    public class AccesoController : Controller
    {
        private readonly IUsuariosApiServicio _usuariosApi;

        public AccesoController(IUsuariosApiServicio usuariosApi)
        {
            _usuariosApi = usuariosApi;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction(nameof(Inicio));

            return View(new LoginUsuarioViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(
            LoginUsuarioViewModel modelo)
        {
            if (!ModelState.IsValid)
                return View(modelo);

            try
            {
                var resultado = await _usuariosApi.LoginAsync(modelo);

                if (!resultado.Exito || resultado.Usuario == null)
                {
                    ModelState.AddModelError(string.Empty, resultado.Mensaje);
                    return View(modelo);
                }

                var usuario = resultado.Usuario;


                var claims = new List<Claim>
                {
                    new Claim(
                    ClaimTypes.NameIdentifier,
                    usuario.Id.ToString()
                    ),
                    new Claim(
                        ClaimTypes.Name,
                        usuario.Nombre
                    ),
                    new Claim(
                        ClaimTypes.GivenName,
                        usuario.Nombre
                    ),
                    new Claim(
                        ClaimTypes.Email,
                        usuario.Email
                    ),
                    new Claim(
                        "Apellido",
                        usuario.Apellido
                    )
                };


                var identidad = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identidad);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identidad));

                return RedirectToAction(nameof(Inicio));
            }
            catch (HttpRequestException)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo conectar con el servidor. Verificá que la API esté ejecutándose.");
                return View(modelo);
            }
            catch (TaskCanceledException)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "El servidor tardó demasiado en responder. Intentá nuevamente.");
                return View(modelo);
            }
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Registro()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction(nameof(Inicio));

            return View(new RegistroUsuarioViewModel());
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Registro(
            RegistroUsuarioViewModel modelo)
        {
            if (!ModelState.IsValid)
                return View(modelo);

            try
            {
                var resultado = await _usuariosApi.RegistrarAsync(modelo);

                if (!resultado.Exito)
                {
                    ModelState.AddModelError(string.Empty, resultado.Mensaje);
                    return View(modelo);
                }

                TempData["Mensaje"] =
                    "Cuenta creada. Ya podés iniciar sesión.";

                return RedirectToAction(nameof(Login));
            }
            catch (HttpRequestException)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "No se pudo conectar con el servidor. Verificá que la API esté ejecutándose.");
                return View(modelo);
            }
            catch (TaskCanceledException)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "El servidor tardó demasiado en responder. Intentá nuevamente.");
                return View(modelo);
            }
        }

        [Authorize]
        [HttpGet]
        public IActionResult Inicio()
        {
            return View();
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            return RedirectToAction(nameof(Login));
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Denegado()
        {
            return View();
        }
    }
}
