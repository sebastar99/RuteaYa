using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public sealed class SalidasViajeServicio(RuteaYaDbContext db)
{
    public async Task<int?> SeleccionarAsync(int conductorId, SeleccionSalidaDto dto, CancellationToken ct = default)
    {
        var viaje = await db.Viajes.AsNoTracking().Include(v => v.Dias).Include(v => v.OrigenUbicacion)
            .SingleOrDefaultAsync(v => v.Id == dto.ViajeId && v.UsuarioId == conductorId, ct);
        if (viaje == null) return null;
        if (!viaje.Activo) throw new ArgumentException("El viaje debe estar activo.");
        if (!dto.Fecha.HasValue || dto.Fecha == DateOnly.MinValue)
            throw new ArgumentException("Elegí una fecha concreta.");
        if (!viaje.Dias.Any(d => d.DiaSemana == DiaRecurrente(dto.Fecha.Value)))
            throw new ArgumentException("La fecha no corresponde a un día habilitado del viaje.");
        var existente = await db.SalidasViaje.AsNoTracking().SingleOrDefaultAsync(s => s.ViajeId == viaje.Id && s.Fecha == dto.Fecha, ct);
        if (existente != null)
        {
            if (existente.FechaHora <= DateTimeOffset.UtcNow || !existente.Disponible || existente.FechaHora.TimeOfDay != viaje.HoraSalida)
                throw new ArgumentException("La salida guardada ya no está disponible o cambió el horario del viaje.");
            // Seleccionar la fecha existente no modifica horarios ni compromisos.
            return existente.Id;
        }
        var zona = ZonaHorariaViaje.Resolver(viaje, dto.ZonaHorariaId);
        if (viaje.HoraSalida < TimeSpan.Zero || viaje.HoraSalida >= TimeSpan.FromDays(1))
            throw new ArgumentException("El horario de salida del viaje no es válido.");
        var salida = ZonaHorariaViaje.Convertir(dto.Fecha.Value.ToDateTime(TimeOnly.FromTimeSpan(viaje.HoraSalida)), zona);
        if (salida <= DateTimeOffset.UtcNow) throw new ArgumentException("La salida debe ser futura.");
        if (dto.LlegadaSinRestriccion && dto.LlegadaMaximaLocal.HasValue)
            throw new ArgumentException("Informá una llegada máxima o marcá sin límite, no ambas.");
        DateTimeOffset? llegada = dto.LlegadaMaximaLocal.HasValue
            ? ZonaHorariaViaje.Convertir(dto.LlegadaMaximaLocal.Value, zona) : null;
        if (llegada < salida) throw new ArgumentException("La llegada máxima no puede ser anterior a la salida.");
        var nueva = new SalidaViaje { ViajeId = viaje.Id, Fecha = dto.Fecha.Value, FechaHora = salida,
            LlegadaMaxima = llegada, LlegadaSinRestriccion = dto.LlegadaSinRestriccion };
        db.SalidasViaje.Add(nueva);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(nueva).State = EntityState.Detached; throw; }
        return nueva.Id;
    }

    public async Task<SalidaViajeDto?> ObtenerAsync(int conductorId, int viajeId, int salidaId, CancellationToken ct = default)
    {
        var s = await db.SalidasViaje.AsNoTracking().Include(s => s.Viaje).ThenInclude(v => v.Dias)
            .SingleOrDefaultAsync(s => s.Id == salidaId && s.ViajeId == viajeId && s.Viaje.UsuarioId == conductorId, ct);
        if (s == null) return null;
        var v = s.Viaje;
        var valida = v.Activo && s.Disponible && s.FechaHora > DateTimeOffset.UtcNow &&
            s.Fecha == DateOnly.FromDateTime(s.FechaHora.DateTime) && s.FechaHora.TimeOfDay == v.HoraSalida &&
            v.Dias.Any(d => d.DiaSemana == DiaRecurrente(s.Fecha));
        // Se cuentan también los entregados: un envío ocupa un cupo durante toda su salida.
        var comprometidos = await db.Envios.AsNoTracking().Where(e => e.SalidaViajeId == s.Id && e.Estado != EstadoEnvio.Cancelado)
            .Select(e => new { e.PesoKg, e.Estado, e.ConductorId }).ToListAsync(ct);
        var legadosSinSalida = await db.Envios.AsNoTracking().AnyAsync(e => e.ConductorId == conductorId && e.SalidaViajeId == null &&
            (e.Estado == EstadoEnvio.ConductorAsignado || e.Estado == EstadoEnvio.Retirado || e.Estado == EstadoEnvio.EnCamino), ct);
        var consistente = !legadosSinSalida && v.PesoMax >= 0 && v.CantMaxPaquetes >= 0 &&
            comprometidos.All(e => e.PesoKg > 0 && e.ConductorId == conductorId && e.Estado is EstadoEnvio.ConductorAsignado or EstadoEnvio.Retirado or EstadoEnvio.EnCamino or EstadoEnvio.Entregado);
        var peso = comprometidos.Sum(e => e.PesoKg);
        return new SalidaViajeDto { Id = s.Id, ViajeId = v.Id, FechaHora = s.FechaHora, Disponible = valida,
            LlegadaMaxima = s.LlegadaMaxima.HasValue ? RestriccionTemporalDto.Informada(s.LlegadaMaxima.Value) :
                s.LlegadaSinRestriccion ? RestriccionTemporalDto.SinRestriccion() : RestriccionTemporalDto.Desconocida(),
            PaquetesComprometidos = comprometidos.Count, PesoComprometidoKg = peso,
            CuposDisponibles = consistente ? Math.Max(0, v.CantMaxPaquetes - comprometidos.Count) : null,
            PesoDisponibleKg = consistente ? Math.Max(0, v.PesoMax - peso) : null,
            AdvertenciaCapacidad = consistente ? null : "Hay asignaciones sin salida o datos inconsistentes; no se puede asegurar la capacidad restante." };
    }

    // Viajes usa lunes=1 ... domingo=7, distinto de DayOfWeek.Sunday=0.
    private static int DiaRecurrente(DateOnly fecha) => fecha.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)fecha.DayOfWeek;
}
