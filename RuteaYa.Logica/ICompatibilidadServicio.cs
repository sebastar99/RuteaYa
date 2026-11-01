using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface ICompatibilidadServicio
{
    /// <summary>
    /// Evalúa una fotografía de un viaje concreto y un envío, sin persistir cambios.
    /// Datos obligatorios malformados producen errores de argumento; información
    /// opcional desconocida se distingue de un rechazo de negocio.
    /// Evalúa reglas previas, desvíos y horarios conservadores de un solo envío.
    /// Prioriza inserciones compatibles sobre provisionales; no deduce horarios por parada.
    /// Errores del proveedor y cancelación se propagan sin resultados parciales definitivos.
    /// </summary>
    Task<ResultadoCompatibilidadDto> EvaluarAsync(
        ViajeCompatibilidadDto viaje,
        EnvioCompatibilidadDto envio,
        CancellationToken cancellationToken = default);
}
