using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace RuteaYa.Logica
{
    public class EnviosLogica
    {
        private readonly RuteaYaDbContext _db;
        private readonly IUbicacionesConfirmadasServicio _ubicaciones;

        public EnviosLogica(RuteaYaDbContext db, IGeocodificacionServicio geocodificacion, IUbicacionesConfirmadasServicio? ubicaciones = null)
        {
            _db = db;
            // El parámetro anterior se conserva para los consumidores existentes. Crear ya no geocodifica.
            _ubicaciones = ubicaciones ?? new UbicacionesConfirmadasServicio(db);
        }

        //lista de envios de un usuario(filtrado por estados)
        public async Task<List<EnvioDto>> ListarAsync(int usuarioId, EstadoEnvio? estado = null)
        {
            if (usuarioId <= 0)
                throw new ArgumentException("Usuario no válido.");

            var consulta = _db.Envios
                .AsNoTracking()
                .Where(e => e.UsuarioId == usuarioId);

            if (estado.HasValue)
            {
                consulta = consulta.Where(e => e.Estado == estado.Value);
            }
            var envios = await consulta
                .OrderByDescending(e => e.Id)
                .ToListAsync();
            return envios.Select(Convertir).ToList();
        }

        // Busca envío por ID para el usuario
        public async Task<EnvioDto?> ObtenerAsync(int usuarioId, int id)
        {
            if (usuarioId <= 0 || id <= 0)
                throw new ArgumentException("Los identificadores no son válidos.");

            var envio = await _db.Envios
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.UsuarioId == usuarioId);

            return envio == null ? null : Convertir(envio);
        }

        // Valida y guarda un nuevo envío
        public async Task<EnvioDto> CrearAsync(int usuarioId, CrearEnvioDto dto)
        {
            if (usuarioId <= 0)
                throw new ArgumentException("Usuario no válido.");

            if (dto == null)
                throw new ArgumentException("Los datos del envío son obligatorios.");

            var datos = dto.Ubicaciones;
            if (datos?.Retiro == null || datos.Entrega == null || datos.Retiro.TipoRecurso != TipoRecursoUbicacion.Envio ||
                datos.Entrega.TipoRecurso != TipoRecursoUbicacion.Envio || datos.Retiro.RecursoId.HasValue || datos.Entrega.RecursoId.HasValue ||
                datos.Retiro.Campo != CampoUbicacion.RetiroEnvio || datos.Entrega.Campo != CampoUbicacion.EntregaEnvio ||
                datos.Retiro.FormularioId != datos.Entrega.FormularioId || string.IsNullOrEmpty(datos.ComprobanteRetiro) || string.IsNullOrEmpty(datos.ComprobanteEntrega))
                throw new ComprobanteUbicacionException("ubicaciones_requeridas", "Elegí y confirmá las direcciones de retiro y entrega antes de publicar.");
            var origen = await _ubicaciones.ObtenerDireccionAsync(usuarioId, datos.Retiro, datos.ComprobanteRetiro);
            var destino = await _ubicaciones.ObtenerDireccionAsync(usuarioId, datos.Entrega, datos.ComprobanteEntrega);
            var canonico = new CrearEnvioDto { OrigenDir = origen, DestinoDir = destino, FechaLimite = dto.FechaLimite,
                PesoKg = dto.PesoKg, Tamano = dto.Tamano, Descripcion = dto.Descripcion, Monto = dto.Monto };
            Validar(canonico);

            var envio = new Envio
            {
                UsuarioId = usuarioId,
                OrigenDir = origen,
                DestinoDir = destino,
                FechaLimite = dto.FechaLimite,
                PesoKg = dto.PesoKg,
                Tamano = dto.Tamano,
                Descripcion = dto.Descripcion.Trim(),
                Monto = dto.Monto,
                Estado = EstadoEnvio.BuscandoConductor,
                CreadoEn = DateTime.Now
            };

            _db.Envios.Add(envio);
            await _ubicaciones.GuardarAsync(usuarioId, new[] {
                new AsignacionUbicacionDto(datos.Retiro, datos.ComprobanteRetiro, Envio: envio),
                new AsignacionUbicacionDto(datos.Entrega, datos.ComprobanteEntrega, Envio: envio) });

            return Convertir(envio);
        }

        // Cancela un envío (Regla TP: No se puede cancelar si ya fue entregado)
        public async Task<bool> CancelarAsync(int usuarioId, int id)
        {
            if (usuarioId <= 0 || id <= 0)
                throw new ArgumentException("Los identificadores no son válidos.");

            var envio = await _db.Envios
                .FirstOrDefaultAsync(e => e.Id == id && e.UsuarioId == usuarioId);

            if (envio == null) return false;

            if (envio.Estado == EstadoEnvio.Entregado)
                throw new ArgumentException("No se puede cancelar un envío que ya fue entregado.");

            if (envio.Estado == EstadoEnvio.Cancelado)
                return true;

            envio.Estado = EstadoEnvio.Cancelado;
            await _db.SaveChangesAsync();

            return true;
        }

        // Actualiza estado desde la perspectiva del transporte/conductor
        public async Task<bool> ActualizarEstadoAsync(int usuarioId, int envioId, EstadoEnvio nuevoEstado)
        {
            if (usuarioId <= 0)
                throw new ArgumentException("Usuario no válido.");

            if (envioId <= 0)
                throw new ArgumentException("Envío no válido.");

            if (!Enum.IsDefined(typeof(EstadoEnvio), nuevoEstado))
                throw new ArgumentException("Estado de envío no válido.");

            var envio = await _db.Envios.FirstOrDefaultAsync(
                e => e.Id == envioId && e.UsuarioId == usuarioId);

            if (envio == null) return false;

            // Operaciones finalizadas no se modifican
            if (envio.Estado == EstadoEnvio.Entregado || envio.Estado == EstadoEnvio.Cancelado)
                throw new ArgumentException("No se puede modificar un envío finalizado.");

            if (!TransicionValida(envio.Estado, nuevoEstado))
                throw new ArgumentException("La transición de estado solicitada no está permitida.");

            envio.Estado = nuevoEstado;
            await _db.SaveChangesAsync();

            return true;
        }

        // El conductor toma un envío que busca conductor, usando uno de sus viajes activos.
        public async Task<bool> AceptarAsync(int conductorId, int viajeId, int envioId)
        {
            if (conductorId <= 0)
                throw new ArgumentException("Usuario no válido.");

            if (viajeId <= 0 || envioId <= 0)
                throw new ArgumentException("Los identificadores no son válidos.");

            var viajeActivo = await _db.Viajes.AsNoTracking().AnyAsync(
                v => v.Id == viajeId && v.UsuarioId == conductorId && v.Activo);

            if (!viajeActivo)
                throw new ArgumentException("El viaje seleccionado no existe o no está activo.");

            var envio = await _db.Envios.FirstOrDefaultAsync(e => e.Id == envioId);

            if (envio == null || envio.Estado != EstadoEnvio.BuscandoConductor)
                throw new ArgumentException("El envío ya no está disponible.");

            if (envio.UsuarioId == conductorId)
                throw new ArgumentException("No podés transportar tus propios envíos.");

            envio.Estado = EstadoEnvio.ConductorAsignado;
            envio.ConductorId = conductorId;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ArgumentException("El envío fue tomado por otro conductor.");
            }

            return true;
        }

        // El conductor asignado avanza el envío: Retirado, EnCamino o Entregado.
        public async Task<bool> ActualizarEstadoConductorAsync(int conductorId, int envioId, EstadoEnvio nuevoEstado)
        {
            if (conductorId <= 0)
                throw new ArgumentException("Usuario no válido.");

            if (envioId <= 0)
                throw new ArgumentException("Envío no válido.");

            if (nuevoEstado is not (EstadoEnvio.Retirado or EstadoEnvio.EnCamino or EstadoEnvio.Entregado))
                throw new ArgumentException("El conductor solo puede marcar el envío como retirado, en camino o entregado.");

            var envio = await _db.Envios.FirstOrDefaultAsync(
                e => e.Id == envioId && e.ConductorId == conductorId);

            if (envio == null) return false;

            if (envio.Estado == EstadoEnvio.Entregado || envio.Estado == EstadoEnvio.Cancelado)
                throw new ArgumentException("No se puede modificar un envío finalizado.");

            if (!TransicionValida(envio.Estado, nuevoEstado))
                throw new ArgumentException("La transición de estado solicitada no está permitida.");

            envio.Estado = nuevoEstado;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ArgumentException("El envío fue modificado mientras lo actualizabas. Recargá e intentá nuevamente.");
            }

            return true;
        }

        // Envíos que el usuario transporta como conductor
        public async Task<List<EnvioDto>> ListarTransportesAsync(int conductorId)
        {
            if (conductorId <= 0)
                throw new ArgumentException("Usuario no válido.");

            var envios = await _db.Envios
                .AsNoTracking()
                .Where(e => e.ConductorId == conductorId)
                .OrderByDescending(e => e.Id)
                .ToListAsync();

            return envios.Select(Convertir).ToList();
        }

        private static bool TransicionValida(EstadoEnvio actual, EstadoEnvio nuevo) => (actual, nuevo) switch
        {
            (EstadoEnvio.BuscandoConductor, EstadoEnvio.ConductorAsignado) => true,
            (EstadoEnvio.ConductorAsignado, EstadoEnvio.Retirado) => true,
            (EstadoEnvio.Retirado, EstadoEnvio.EnCamino) => true,
            (EstadoEnvio.EnCamino, EstadoEnvio.Entregado) => true,
            (_, EstadoEnvio.Cancelado) => true,
            _ => false
        };


        //metodos 

        private static EnvioDto Convertir(Envio e) => new()
        {
            Id = e.Id,
            UsuarioId = e.UsuarioId,
            ConductorId = e.ConductorId,
            OrigenDir = e.OrigenDir,
            DestinoDir = e.DestinoDir,
            FechaLimite = e.FechaLimite,
            PesoKg = e.PesoKg,
            Tamano = e.Tamano,
            Descripcion = e.Descripcion,
            Monto = e.Monto,
            Estado = e.Estado
        };

        private static void Validar(CrearEnvioDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.OrigenDir) || string.IsNullOrWhiteSpace(dto.DestinoDir))
                throw new ArgumentException("El origen y destino son obligatorios.");

            if (dto.OrigenDir.Trim().Equals(dto.DestinoDir.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("El origen y destino deben ser diferentes.");

            if (dto.PesoKg <= 0)
                throw new ArgumentException("El peso debe ser mayor a 0 kg.");

            if (dto.Monto <= 0)
                throw new ArgumentException("El monto ofrecido debe ser mayor a 0.");

            if (string.IsNullOrWhiteSpace(dto.Descripcion))
                throw new ArgumentException("La descripción del envío es obligatoria.");

            if (dto.FechaLimite < DateTime.Now.Date)
                throw new ArgumentException("La fecha límite no puede ser anterior a hoy.");
        }

    }

}
