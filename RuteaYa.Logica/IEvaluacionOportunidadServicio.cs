using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IEvaluacionOportunidadServicio
{
    /// <summary>
    /// Prepara una única evaluación después de autorizar viaje y disponibilidad.
    /// La fuente persistida permite rechazos probados y evaluación geográfica parcial;
    /// los datos restantes nunca se completan por inferencia. Fuentes anteriores conservan la política estricta.
    /// </summary>
    Task<EvaluacionOportunidadDto> EvaluarAsync(
        ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, EvaluacionOportunidadDto>> EvaluarLoteAsync(ViajeOportunidadesDto viaje,
        IReadOnlyList<EnvioDto> envios, CancellationToken ct = default);
}
