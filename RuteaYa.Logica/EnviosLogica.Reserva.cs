using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public partial class EnviosLogica
{
    /// <summary>
    /// Reserva una combinación de envíos en una única transacción. Reevalúa todo en el servidor antes de
    /// abrirla (sin consultas externas bajo bloqueo) y dentro comprueba que ni la salida ni los envíos
    /// cambiaron: cualquier diferencia aborta sin asignaciones parciales.
    /// </summary>
    public async Task<ResultadoReservaDto> ReservarCombinacionAsync(int conductorId, int viajeId, int salidaId,
        IReadOnlyList<int> envioIds, string? huella, CancellationToken ct = default)
    {
        if (conductorId <= 0 || viajeId <= 0 || salidaId <= 0)
            throw new ArgumentException("Los identificadores deben ser positivos.");
        ArgumentNullException.ThrowIfNull(envioIds);
        if (envioIds.Count == 0 || envioIds.Count > 50 || envioIds.Any(i => i <= 0) || envioIds.Distinct().Count() != envioIds.Count)
            throw new ArgumentException("La selección de envíos no es válida.");
        if (_optimizador == null) throw new InvalidOperationException("Falta configurar el optimizador de oportunidades.");
        if (_db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Hay cambios pendientes antes de reservar.");
        _db.ChangeTracker.Clear();

        // Evaluación completa fuera de la transacción: las consultas al proveedor de rutas no retienen bloqueos.
        var eval = await _optimizador.EvaluarSeleccionAsync(conductorId, viajeId, salidaId, envioIds, ct);
        if (eval == null) return new() { Estado = EstadoReserva.NoEncontrado };
        if (eval.YaReservada) return new() { Estado = EstadoReserva.YaReservado, Evaluacion = eval, Mensaje = eval.Motivo };
        if (!eval.Valida)
            return new() { Evaluacion = eval, EnviosEnConflicto = eval.EnviosNoDisponibles, Mensaje = eval.Motivo,
                Estado = eval.EnviosNoDisponibles.Count > 0 ? EstadoReserva.EnvioNoDisponible :
                    eval.Motivos.Any(m => m is MotivoCompatibilidad.SinCupo or MotivoCompatibilidad.PesoExcedido or MotivoCompatibilidad.PesoMaximoPublicadoExcedido)
                        ? EstadoReserva.EnvioNoDisponible : EstadoReserva.Incompatible };
        if (huella != null && !string.Equals(huella, eval.Huella, StringComparison.Ordinal))
            return new() { Estado = EstadoReserva.Cambio, Evaluacion = eval,
                Mensaje = "Los datos cambiaron desde el cálculo. Volvé a calcular la combinación." };
        try
        {
            await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var salida = await _db.SalidasViaje.Include(s => s.Viaje)
                .SingleOrDefaultAsync(s => s.Id == salidaId && s.ViajeId == viajeId && s.Viaje.UsuarioId == conductorId, ct);
            if (salida == null) return new() { Estado = EstadoReserva.NoEncontrado };
            var envios = await _db.Envios.Where(e => envioIds.Contains(e.Id)).ToListAsync(ct);
            var comprometidos = await _db.Envios
                .Where(e => e.SalidaViajeId == salidaId && e.Estado != EstadoEnvio.Cancelado && !envioIds.Contains(e.Id))
                .Select(e => new { e.Id, e.RowVersion }).ToListAsync(ct);
            // Huella actual contra la evaluada: si algo cambió, se aborta sin tocar nada.
            var huellaActual = IOptimizadorServicio.Huella(salidaId, salida.Version,
                envios.Select(e => (e.Id, e.RowVersion)).Concat(comprometidos.Select(e => (e.Id, e.RowVersion))));
            if (huellaActual != eval.Huella)
                return new() { Estado = EstadoReserva.Cambio, Mensaje = "Los datos cambiaron desde el cálculo. Volvé a calcular la combinación." };

            // Reintento idempotente: todos ya asignados a este conductor en esta salida.
            if (envios.Count == envioIds.Count && envios.All(e => e.ConductorId == conductorId && e.SalidaViajeId == salidaId &&
                e.Estado is EstadoEnvio.ConductorAsignado or EstadoEnvio.Retirado or EstadoEnvio.EnCamino or EstadoEnvio.Entregado))
                return new() { Estado = EstadoReserva.YaReservado };
            if (envios.Count != envioIds.Count) return new() { Estado = EstadoReserva.NoEncontrado };
            var noDisponibles = envios.Where(e => e.Estado != EstadoEnvio.BuscandoConductor || e.ConductorId.HasValue ||
                e.SalidaViajeId.HasValue || e.UsuarioId == conductorId).Select(e => e.Id).ToList();
            if (noDisponibles.Count > 0)
                return new() { Estado = EstadoReserva.EnvioNoDisponible, EnviosEnConflicto = noDisponibles,
                    Mensaje = "Alguno de los envíos fue tomado por otro conductor." };
            if (!salida.Disponible || salida.FechaHora <= DateTimeOffset.UtcNow || !salida.Viaje.Activo)
                return new() { Estado = EstadoReserva.SalidaNoValida, Mensaje = "La salida ya no está disponible." };

            // Bloqueo de escritura de la salida antes de verificar su capacidad, igual que la aceptación individual.
            salida.Version = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);
            var ocupados = await _db.Envios.Where(e => e.SalidaViajeId == salidaId && e.Estado != EstadoEnvio.Cancelado)
                .Select(e => e.PesoKg).ToListAsync(ct);
            if (ocupados.Count + envios.Count > salida.Viaje.CantMaxPaquetes)
                return new() { Estado = EstadoReserva.Incompatible, Mensaje = "Ya no hay cupos suficientes en la salida." };
            if (ocupados.Sum() + envios.Sum(e => e.PesoKg) > salida.Viaje.PesoMax)
                return new() { Estado = EstadoReserva.Incompatible, Mensaje = "Ya no hay peso disponible suficiente en la salida." };

            foreach (var envio in envios)
            {
                envio.ConductorId = conductorId;
                envio.SalidaViajeId = salidaId;
                envio.Estado = EstadoEnvio.ConductorAsignado;
            }
            // RowVersion de cada envío entra en su UPDATE: todo se confirma junto o nada.
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new() { Estado = EstadoReserva.Reservado, Evaluacion = eval };
        }
        catch (DbUpdateConcurrencyException) { return new() { Estado = EstadoReserva.ConflictoConcurrencia }; }
        catch (DbUpdateException e) when (e.InnerException is DbException) { return new() { Estado = EstadoReserva.ConflictoConcurrencia }; }
        catch (DbException) { return new() { Estado = EstadoReserva.ConflictoConcurrencia }; }
        finally { _db.ChangeTracker.Clear(); }
    }
}
