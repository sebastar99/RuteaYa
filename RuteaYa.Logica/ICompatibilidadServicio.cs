using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface ICompatibilidadServicio
{
    /// <summary>
    /// Evalúa una fotografía de un viaje concreto y un envío, sin persistir cambios.
    /// Datos obligatorios malformados producen errores de argumento; información
    /// opcional desconocida se distingue de un rechazo de negocio.
    /// Evalúa reglas previas y desvíos de un solo envío. El horario sigue pendiente:
    /// nunca devuelve Compatible definitivo. Errores del proveedor y cancelación se propagan.
    /// </summary>
    Task<ResultadoCompatibilidadDto> EvaluarAsync(
        ViajeCompatibilidadDto viaje,
        EnvioCompatibilidadDto envio,
        CancellationToken cancellationToken = default);
}
