using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;
using System;
using System.Collections.Generic;
using System.Text;

namespace RuteaYa.Logica
{
    public class EnviosLogica
    {
        private readonly RuteaYaDbContext _db;

        public EnviosLogica(RuteaYaDbContext db)
        {
            _db = db;
        }

        //lista de envios de un usuario(filtrado por estados)
        public async Task<List<EnvioDto>> ListarAsync(int usuarioId, EstadoEnvio? estado = null)
        {

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
            var envio = await _db.Envios
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.UsuarioId == usuarioId);

            return envio == null ? null : Convertir(envio);
        }

        // Valida y guarda un nuevo envío
        public async Task<EnvioDto> CrearAsync(int usuarioId, CrearEnvioDto dto)
        {
            Validar(dto);

            var envio = new Envio
            {
                UsuarioId = usuarioId,
                OrigenDir = dto.OrigenDir.Trim(),
                DestinoDir = dto.DestinoDir.Trim(),
                FechaLimite = dto.FechaLimite,
                PesoKg = dto.PesoKg,
                Tamano = dto.Tamano,
                Descripcion = dto.Descripcion.Trim(),
                Monto = dto.Monto,
                Estado = EstadoEnvio.BuscandoConductor,
                CreadoEn = DateTime.Now
            };

            _db.Envios.Add(envio);
            await _db.SaveChangesAsync();

            return Convertir(envio);
        }

        // Cancela un envío (Regla TP: No se puede cancelar si ya fue entregado)
        public async Task<bool> CancelarAsync(int usuarioId, int id)
        {
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
        public async Task<bool> ActualizarEstadoAsync(int envioId, EstadoEnvio nuevoEstado)
        {
            var envio = await _db.Envios.FindAsync(envioId);

            if (envio == null) return false;

            // Operaciones finalizadas no se modifican
            if (envio.Estado == EstadoEnvio.Entregado || envio.Estado == EstadoEnvio.Cancelado)
                throw new ArgumentException("No se puede modificar un envío finalizado.");

            // Validaciones de transición de estados
            if (nuevoEstado == EstadoEnvio.EnCamino && envio.Estado != EstadoEnvio.Retirado)
                throw new ArgumentException("El paquete debe ser retirado antes de estar en camino.");

            if (nuevoEstado == EstadoEnvio.Entregado && envio.Estado != EstadoEnvio.EnCamino)
                throw new ArgumentException("El paquete debe estar en camino antes de marcarse como entregado.");

            envio.Estado = nuevoEstado;
            await _db.SaveChangesAsync();

            return true;
        }


        //metodos 

        private static EnvioDto Convertir(Envio e) => new()
        {
            Id = e.Id,
            UsuarioId = e.UsuarioId,
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
