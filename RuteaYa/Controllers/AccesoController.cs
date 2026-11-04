
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers
{
    public class AccesoController : Controller
    {
        private const string AccessTokenCookieName = "RuteaYa.AccessToken";
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

            if (!resultado.Exito ||
                resultado.Usuario == null ||
                string.IsNullOrWhiteSpace(resultado.Token))
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

                var propiedades = new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true,
                    // Debe coincidir con Jwt:ExpirationMinutes para que la
                    // sesión MVC no termine antes que el token usado por los
                    // endpoints protegidos de viajes.
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
                };

                    propiedades.StoreTokens(new[]
                    {
                    new AuthenticationToken
                    {
                        Name = "access_token",
                        Value = resultado.Token
                    },
                    new AuthenticationToken
                    {
                        Name = "token_type",
                        Value = "Bearer"
                    }
                });

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    propiedades);

                // El token también se conserva en una cookie HTTP-only. Es
                // el respaldo usado por JwtTokenHandler al llamar a la API;
                // así no depende de que el proveedor de cookies exponga los
                // tokens serializados en AuthenticationProperties.
                Response.Cookies.Append(
                    AccessTokenCookieName,
                    resultado.Token,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        IsEssential = true,
                        Expires = DateTimeOffset.UtcNow.AddHours(2)
                    });

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
        public async Task<IActionResult> Inicio([FromServices] RuteaYaDbContext db)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId) || usuarioId <= 0)
                return Forbid();

            var hoy = DateTime.Now.Date;

            var viajesActivos = db.Viajes
                .AsNoTracking()
                .Where(v => v.UsuarioId == usuarioId && v.Activo);

            var enviosPendientes = db.Envios
                .AsNoTracking()
                .Where(e => e.UsuarioId == usuarioId &&
                            e.Estado != EstadoEnvio.Entregado &&
                            e.Estado != EstadoEnvio.Cancelado);

            // Envios de otros usuarios que buscan conductor (solo si el usuario tiene viajes activos).
            var candidatos = db.Envios
                .AsNoTracking()
                .Where(e => e.UsuarioId != usuarioId &&
                            e.Estado == EstadoEnvio.BuscandoConductor &&
                            e.FechaLimite >= hoy);

            var modelo = new DashboardViewModel
            {
                ProximosViajes = await viajesActivos.CountAsync(),
                EnviosPendientes = await enviosPendientes.CountAsync()
            };

            if (modelo.ProximosViajes > 0)
            {
                modelo.OportunidadesDisponibles = await candidatos.CountAsync();
                modelo.PotencialMensual = await candidatos.SumAsync(e => (decimal?)e.Monto) ?? 0m;
            }

            var viajes = await viajesActivos
                .OrderByDescending(v => v.Id)
                .Take(3)
                .Select(v => new
                {
                    v.Id,
                    v.Origen,
                    v.Destino,
                    v.HoraSalida,
                    v.HoraLlegada,
                    Dias = v.Dias.Select(d => d.DiaSemana).ToList()
                })
                .ToListAsync();

            modelo.ViajesRecientes = viajes.Select(v => new ViajeDashboardViewModel
            {
                Id = v.Id,
                Origen = v.Origen,
                Destino = v.Destino,
                Dias = string.Join(", ", v.Dias.OrderBy(d => d).Select(NombreDia)),
                HorarioSalida = v.HoraSalida.ToString(@"hh\:mm"),
                HorarioLlegada = v.HoraLlegada.ToString(@"hh\:mm"),
                Estado = "Activo"
            }).ToList();

            var envios = await enviosPendientes
                .OrderByDescending(e => e.Id)
                .Take(3)
                .Select(e => new { e.Id, e.OrigenDir, e.DestinoDir, e.Descripcion, e.Monto, e.Estado })
                .ToListAsync();

            modelo.EnviosPendientesLista = envios.Select(e => new EnvioDashboardViewModel
            {
                Id = e.Id,
                Origen = e.OrigenDir,
                Destino = e.DestinoDir,
                Descripcion = e.Descripcion,
                Importe = e.Monto,
                Estado = TextoEstado(e.Estado),
                EstadoCss = e.Estado == EstadoEnvio.BuscandoConductor ? "orange" : "blue"
            }).ToList();

            return View(modelo);
        }

        private static string NombreDia(int dia) => dia switch
        {
            1 => "Lun",
            2 => "Mar",
            3 => "Mié",
            4 => "Jue",
            5 => "Vie",
            6 => "Sáb",
            7 => "Dom",
            _ => dia.ToString()
        };

        private static string TextoEstado(EstadoEnvio estado) => estado switch
        {
            EstadoEnvio.BuscandoConductor => "Buscando conductor",
            EstadoEnvio.ConductorAsignado => "Conductor asignado",
            EstadoEnvio.Retirado => "Retirado",
            EstadoEnvio.EnCamino => "En camino",
            _ => estado.ToString()
        };

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            Response.Cookies.Delete(AccessTokenCookieName);

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
