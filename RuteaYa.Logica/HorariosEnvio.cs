using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public static class HorariosEnvio
{
    /// <summary>
    /// Convierte los horarios locales del envío con la zona resuelta por el servidor.
    /// Si hay horarios y no se pudo determinar la zona, se rechaza: no se inventa una zona horaria.
    /// </summary>
    public static (DateTimeOffset? Entrega, DateTimeOffset? Retiro) Convertir(CrearEnvioDto dto, TimeZoneInfo? zona)
    {
        if (dto.RetiroSinRestriccion && dto.RetiroDisponibleDesdeLocal.HasValue)
            throw new ArgumentException("Informá disponibilidad de retiro o sin restricción, no ambas.");
        if ((dto.EntregaLimiteLocal.HasValue || dto.RetiroDisponibleDesdeLocal.HasValue) && zona == null)
            throw new ArgumentException("No pudimos determinar la zona horaria de los horarios. Solo se pueden indicar horarios para direcciones de Argentina elegidas de la lista; dejalos en blanco o cambiá las direcciones.");
        DateTimeOffset? ConvertirFecha(DateTime? fecha)
        {
            if (!fecha.HasValue) return null;
            if (fecha.Value == default) throw new ArgumentException("El horario informado no es válido.");
            return ZonaHorariaViaje.Convertir(fecha.Value, zona!);
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
