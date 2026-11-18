using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public partial class EnviosLogica
{
    public async Task<ResultadoAceptacionDto> AceptarSalidaAsync(int conductorId, int viajeId, int salidaId, int envioId,
        CancellationToken ct = default)
    {
        if (conductorId <= 0 || viajeId <= 0 || salidaId <= 0 || envioId <= 0)
            throw new ArgumentException("Los identificadores deben ser positivos.");
        if (_oportunidades == null) throw new InvalidOperationException("Falta configurar la evaluación de oportunidades.");
        // No incluir cambios ajenos en la transacción ni reutilizar entidades previamente cargadas.
        if (_db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Hay cambios pendientes antes de aceptar.");
        _db.ChangeTracker.Clear();
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var salida = await _db.SalidasViaje.SingleOrDefaultAsync(s => s.Id == salidaId && s.ViajeId == viajeId && s.Viaje.UsuarioId == conductorId, ct);
            if (salida == null) return new() { Estado = EstadoAceptacion.NoEncontrado };

            // Actualización con Version original: toma el bloqueo de escritura de esta salida
            // ANTES de leer sus compromisos. Dos aceptaciones no pueden reservar el mismo saldo.
            salida.Version = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);

            var envio = await _db.Envios.SingleOrDefaultAsync(e => e.Id == envioId, ct);
            if (envio == null || envio.UsuarioId == conductorId) return new() { Estado = EstadoAceptacion.NoEncontrado };
            if (envio.ConductorId == conductorId && envio.SalidaViajeId == salidaId &&
                envio.Estado is EstadoEnvio.ConductorAsignado or EstadoEnvio.Retirado or EstadoEnvio.EnCamino or EstadoEnvio.Entregado)
                return new() { Estado = EstadoAceptacion.YaAceptado }; // Sin volver a reservar ni cambiar estados.
            if (envio.Estado != EstadoEnvio.BuscandoConductor || envio.ConductorId.HasValue || envio.SalidaViajeId.HasValue)
                return new() { Estado = EstadoAceptacion.EnvioNoDisponible };

            // Usa datos actuales de EF y el mismo motor de listado/detalle, dentro de la transacción.
            var detalle = await _oportunidades.ObtenerDetalleSalidaAsync(conductorId, viajeId, salidaId, envioId, ct);
            if (detalle == null) return new() { Estado = EstadoAceptacion.EnvioNoDisponible };
            var evaluacion = detalle.Evaluacion;
            if (detalle.Viaje.SalidaSeleccionada?.Disponible != true || salida.FechaHora <= DateTimeOffset.UtcNow)
                return new() { Estado = EstadoAceptacion.SalidaNoValida, Evaluacion = evaluacion };
            if (evaluacion.Estado != EstadoCompatibilidad.Compatible || !evaluacion.MotorEjecutado || evaluacion.DatosPendientes.Count > 0)
            {
                var capacidad = evaluacion.ResultadoMotor?.Motivos.Any(m => m is MotivoCompatibilidad.SinCupo or MotivoCompatibilidad.PesoExcedido or MotivoCompatibilidad.PesoMaximoPublicadoExcedido) == true;
                return new() { Evaluacion = evaluacion, Estado = capacidad ? EstadoAceptacion.CapacidadInsuficiente :
                    evaluacion.Estado == EstadoCompatibilidad.Incompatible ? EstadoAceptacion.Incompatible : EstadoAceptacion.DatosPendientes };
            }

            envio.ConductorId = conductorId;
            envio.SalidaViajeId = salidaId;
            envio.Estado = EstadoEnvio.ConductorAsignado;
            // EF incluye RowVersion en el UPDATE del envío. Los tres campos y Version
            // de la salida se confirman juntos; cualquier fallo revierte toda la operación.
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new() { Estado = EstadoAceptacion.Aceptado };
        }
        catch (DbUpdateConcurrencyException) { return new() { Estado = EstadoAceptacion.ConflictoConcurrencia }; }
        catch (DbUpdateException e) when (e.InnerException is DbException) { return new() { Estado = EstadoAceptacion.ConflictoConcurrencia }; }
        catch (DbException) { return new() { Estado = EstadoAceptacion.ConflictoConcurrencia }; }
        finally { _db.ChangeTracker.Clear(); }
    }
}
