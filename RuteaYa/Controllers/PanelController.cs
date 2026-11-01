
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Web.Controllers
{
    [Authorize]
    public class PanelController : Controller
    {
        private readonly RuteaYaDbContext _dbContext;
        private readonly IViajesApiServicio _viajesApiServicio;

        public PanelController(RuteaYaDbContext dbContext, IViajesApiServicio viajesApiServicio)
        {
            _dbContext = dbContext;
            _viajesApiServicio = viajesApiServicio;
        }

        public IActionResult Envios() => View();
        public IActionResult NuevoEnvio() => View();
        public IActionResult DetalleEnvio() => View();
        public IActionResult Oportunidades(int? viajeId = null, int pagina = 1, int tamanoPagina = 10) =>
            RedirectToAction("Index", "Oportunidades", new { viajeId, pagina, tamanoPagina });
        public IActionResult DetalleOportunidad() => View();
        public IActionResult Optimizador() => View();
        public IActionResult ConfirmarCombinacion() => View();
        public IActionResult Transportes() => View();

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
        public async Task<IActionResult> ReordenarParadas(int viajeId, List<int> paradasIds, List<int> ordenes)
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
                
            if (paradasIds == null || ordenes == null ||
                paradasIds.Count == 0 || paradasIds.Count != ordenes.Count ||
                paradasIds.Distinct().Count() != paradasIds.Count ||
                ordenes.Distinct().Count() != ordenes.Count ||
                ordenes.Any(o => o < 1 || o > paradasIds.Count)
            ) {
                TempData["MensajeError"] = "La lista de paradas o su orden, no es valido.";
                return RedirectToAction(nameof(DetalleViaje), new { id = viajeId });
            }

            var IdOrdenadas = paradasIds
                .Select((id, indice) => new 
                {
                    Id = id, 
                    Orden = ordenes[indice] 
                })
                .OrderBy(p => p.Orden)
                .Select(p => p.Id)
                .ToList();

            var resultado = await _viajesApiServicio.ReordenarParadasAsync(
                usuarioId,
                viajeId,
                IdOrdenadas
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

   // ============================= Historial usuario ==========================================
        [HttpGet]
        public async Task<IActionResult> Historial(
            string? buscar,
            string tipo = "Todos",
            string estado = "Todos",
            DateTime? desde = null,
            DateTime? hasta = null)
        {
            var claimUsuarioId = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!int.TryParse(claimUsuarioId, out var usuarioId))
                return Forbid();

            var modelo = new HistorialViewModel
            {
                Buscar = buscar?.Trim() ?? string.Empty,
                Tipo = NormalizarFiltro(tipo, "Todos", "Viaje", "Envio"),
                Estado = estado ?? "Todos",
                Desde = desde?.Date,
                Hasta = hasta?.Date
            };

            if (modelo.Desde.HasValue &&
                modelo.Hasta.HasValue &&
                modelo.Desde.Value > modelo.Hasta.Value)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "La fecha inicial no puede ser posterior a la fecha final.");

                (modelo.Desde, modelo.Hasta) =
                    (modelo.Hasta, modelo.Desde);
            }

            var viajesBase = _dbContext.Viajes
                .AsNoTracking()
                .Where(v => v.UsuarioId == usuarioId);

            var enviosBase = _dbContext.Envios
                .AsNoTracking()
                .Where(e => e.UsuarioId == usuarioId);

            // Indicadores generales del usuario, sin aplicar filtros de tabla.
            modelo.TotalViajes = await viajesBase.CountAsync();

            modelo.TotalEnvios = await enviosBase.CountAsync();

            modelo.EnviosEntregados = await enviosBase.CountAsync(
                e => e.Estado == EstadoEnvio.Entregado);

            modelo.MontoEnviosEntregados =
                await enviosBase
                    .Where(e => e.Estado == EstadoEnvio.Entregado)
                    .SumAsync(e => (decimal?)e.Monto) ?? 0m;

            if (modelo.Tipo is "Todos" or "Viaje")
            {
                var consultaViajes = viajesBase;

                if (modelo.Desde.HasValue)
                {
                    consultaViajes = consultaViajes.Where(
                        v => v.CreadoEn >= modelo.Desde.Value);
                }

                if (modelo.Hasta.HasValue)
                {
                    var limiteSuperior = modelo.Hasta.Value.AddDays(1);

                    consultaViajes = consultaViajes.Where(
                        v => v.CreadoEn < limiteSuperior);
                }

                if (!string.IsNullOrWhiteSpace(modelo.Buscar))
                {
                    consultaViajes = consultaViajes.Where(
                        v => v.Origen.Contains(modelo.Buscar) ||
                             v.Destino.Contains(modelo.Buscar));
                }

                if (modelo.Estado == "Activo")
                {
                    consultaViajes = consultaViajes.Where(v => v.Activo);
                }
                else if (modelo.Estado == "Inactivo")
                {
                    consultaViajes = consultaViajes.Where(v => !v.Activo);
                }
                else if (modelo.Estado != "Todos")
                {
                    // El estado seleccionado corresponde a envíos.
                    consultaViajes = consultaViajes.Where(v => false);
                }

                var viajes = await consultaViajes
                    .Select(v => new
                    {
                        v.Id,
                        v.Origen,
                        v.Destino,
                        v.CreadoEn,
                        v.HoraSalida,
                        v.HoraLlegada,
                        v.CantMaxPaquetes,
                        v.Activo
                    })
                    .ToListAsync();

                modelo.Operaciones.AddRange(
                    viajes.Select(v => new HistorialOperacionViewModel
                    {
                        Id = v.Id,
                        Tipo = "Viaje",
                        Fecha = v.CreadoEn,
                        Detalle = $"{v.Origen} → {v.Destino}",
                        Informacion =
                            $"Salida {v.HoraSalida:hh\\:mm} · " +
                            $"Llegada {v.HoraLlegada:hh\\:mm} · " +
                            $"Capacidad {v.CantMaxPaquetes} paquetes",
                        Estado = v.Activo ? "Activo" : "Inactivo",
                        EstadoCss = v.Activo ? "green" : "blue"
                    }));
            }

            if (modelo.Tipo is "Todos" or "Envio")
            {
                var consultaEnvios = enviosBase;

                if (modelo.Desde.HasValue)
                {
                    consultaEnvios = consultaEnvios.Where(
                        e => e.CreadoEn >= modelo.Desde.Value);
                }

                if (modelo.Hasta.HasValue)
                {
                    var limiteSuperior = modelo.Hasta.Value.AddDays(1);

                    consultaEnvios = consultaEnvios.Where(
                        e => e.CreadoEn < limiteSuperior);
                }

                if (!string.IsNullOrWhiteSpace(modelo.Buscar))
                {
                    consultaEnvios = consultaEnvios.Where(
                        e => e.OrigenDir.Contains(modelo.Buscar) ||
                             e.DestinoDir.Contains(modelo.Buscar) ||
                             e.Descripcion.Contains(modelo.Buscar));
                }

                if (modelo.Estado != "Todos")
                {
                    consultaEnvios = FiltrarEstadoEnvio(
                        consultaEnvios, modelo.Estado);
                }

                var envios = await consultaEnvios
                    .Select(e => new
                    {
                        e.Id,
                        e.OrigenDir,
                        e.DestinoDir,
                        e.Descripcion,
                        e.CreadoEn,
                        e.Monto,
                        e.Estado
                    })
                    .ToListAsync();

                modelo.Operaciones.AddRange(
                    envios.Select(e => new HistorialOperacionViewModel
                    {
                        Id = e.Id,
                        Tipo = "Envío",
                        Fecha = e.CreadoEn,
                        Detalle = $"{e.OrigenDir} → {e.DestinoDir}",
                        Informacion = $"{e.Descripcion} · {e.Monto:C}",
                        Estado = TextoEstadoEnvio(e.Estado),
                        EstadoCss = CssEstadoEnvio(e.Estado),
                        Monto = e.Monto
                    }));
            }

            modelo.Operaciones = modelo.Operaciones
                .OrderByDescending(o => o.Fecha)
                .ToList();

            return View(modelo);
        }

        private static string NormalizarFiltro(
            string? valor,
            params string[] permitidos)
        {
            return permitidos.FirstOrDefault(p =>
                string.Equals(
                    p,
                    valor,
                    StringComparison.OrdinalIgnoreCase))
                ?? permitidos[0];
        }

        private static IQueryable<Envio> FiltrarEstadoEnvio(
            IQueryable<Envio> consulta,
            string estado)
        {
            return estado switch
            {
                nameof(EstadoEnvio.BuscandoConductor) =>
                    consulta.Where(e =>
                        e.Estado == EstadoEnvio.BuscandoConductor),

                nameof(EstadoEnvio.ConductorAsignado) =>
                    consulta.Where(e =>
                        e.Estado == EstadoEnvio.ConductorAsignado),

                nameof(EstadoEnvio.Retirado) =>
                    consulta.Where(e =>
                        e.Estado == EstadoEnvio.Retirado),

                nameof(EstadoEnvio.EnCamino) =>
                    consulta.Where(e =>
                        e.Estado == EstadoEnvio.EnCamino),

                nameof(EstadoEnvio.Entregado) =>
                    consulta.Where(e =>
                        e.Estado == EstadoEnvio.Entregado),

                nameof(EstadoEnvio.Cancelado) =>
                    consulta.Where(e =>
                        e.Estado == EstadoEnvio.Cancelado),

                _ => consulta.Where(e => false)
            };
        }

        private static string TextoEstadoEnvio(EstadoEnvio estado) =>
            estado switch
            {
                EstadoEnvio.BuscandoConductor => "Buscando conductor",
                EstadoEnvio.ConductorAsignado => "Conductor asignado",
                EstadoEnvio.Retirado => "Retirado",
                EstadoEnvio.EnCamino => "En camino",
                EstadoEnvio.Entregado => "Entregado",
                EstadoEnvio.Cancelado => "Cancelado",
                _ => "Desconocido"
            };

        private static string CssEstadoEnvio(EstadoEnvio estado) =>
            estado switch
            {
                EstadoEnvio.Entregado => "green",
                EstadoEnvio.Cancelado => "red",
                EstadoEnvio.BuscandoConductor => "orange",
                EstadoEnvio.ConductorAsignado or
                EstadoEnvio.Retirado or
                EstadoEnvio.EnCamino => "blue",
                _ => string.Empty
            };

        public IActionResult Potencial() => View();
    }
}
