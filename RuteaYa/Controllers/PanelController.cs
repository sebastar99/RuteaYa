
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using RuteaYa.Entidades;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Controllers
{
    [Authorize]
    public class PanelController : Controller
    {
        private readonly RuteaYaDbContext _dbContext;

        public PanelController(RuteaYaDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IActionResult Viajes() => View();
        public IActionResult NuevoViaje() => View();
        public IActionResult DetalleViaje() => View();
        public IActionResult Envios() => View();
        public IActionResult NuevoEnvio() => View();
        public IActionResult DetalleEnvio() => View();
        public IActionResult Oportunidades() => View();
        public IActionResult DetalleOportunidad() => View();
        public IActionResult Optimizador() => View();
        public IActionResult ConfirmarCombinacion() => View();
        public IActionResult Transportes() => View();

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
