using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IEvaluacionOportunidadServicio
{
    /// <summary>
    /// Prepara una única evaluación después de autorizar viaje y disponibilidad.
    /// Si faltan requisitos, no llama al motor ni a servicios de rutas/geocodificación.
    /// </summary>
    Task<EvaluacionOportunidadDto> EvaluarAsync(
        ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default);
}
