using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public static class HorariosEnvio
{
    public static (DateTimeOffset? Entrega, DateTimeOffset? Retiro) Convertir(CrearEnvioDto dto)
    {
        if (dto.RetiroSinRestriccion && dto.RetiroDisponibleDesdeLocal.HasValue)
            throw new ArgumentException("Informá disponibilidad de retiro o sin restricción, no ambas.");
        if ((dto.EntregaLimiteLocal.HasValue || dto.RetiroDisponibleDesdeLocal.HasValue) && !dto.OffsetUtcMinutos.HasValue)
            throw new ArgumentException("Indicá el offset UTC de los horarios del envío.");
        if (dto.OffsetUtcMinutos is < -840 or > 840)
            throw new ArgumentException("El offset UTC debe estar entre -840 y 840 minutos.");
        DateTimeOffset? ConvertirFecha(DateTime? fecha)
        {
            if (!fecha.HasValue) return null;
            if (fecha.Value == default) throw new ArgumentException("El horario informado no es válido.");
            return new DateTimeOffset(DateTime.SpecifyKind(fecha.Value, DateTimeKind.Unspecified), TimeSpan.FromMinutes(dto.OffsetUtcMinutos!.Value));
        }
        var entrega = ConvertirFecha(dto.EntregaLimiteLocal);
        var retiro = ConvertirFecha(dto.RetiroDisponibleDesdeLocal);
        if (entrega.HasValue && entrega.Value.Date != dto.FechaLimite.Date)
            throw new ArgumentException("El horario límite debe corresponder a la fecha límite publicada.");
        if (retiro.HasValue && entrega.HasValue && retiro > entrega)
            throw new ArgumentException("La disponibilidad de retiro no puede ser posterior al límite de entrega.");
        return (entrega, retiro);
    }
}
