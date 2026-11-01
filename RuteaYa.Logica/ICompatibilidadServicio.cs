using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface ICompatibilidadServicio
{
    /// <summary>
    /// Evalúa una fotografía de un viaje concreto y un envío, sin persistir cambios.
    /// Datos obligatorios malformados producen errores de argumento; información
    /// opcional desconocida se distingue de un rechazo de negocio.
    /// Esta etapa solo aplica reglas previas: nunca devuelve Compatible definitivo.
    /// </summary>
    Task<ResultadoCompatibilidadDto> EvaluarAsync(
        ViajeCompatibilidadDto viaje,
        EnvioCompatibilidadDto envio,
        CancellationToken cancellationToken = default);
}
