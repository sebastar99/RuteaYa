using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>
/// Evaluación local preparada para un recorrido, retiro, entrega y límite concretos.
/// Reutiliza solo su resultado base; no almacena alternativas ni comparte datos
/// con otras preparaciones. Debe crearse una nueva para cada evaluación de compatibilidad.
/// </summary>
public interface IEvaluacionDesvios
{
    /// <summary>
    /// Calcula una alternativa. Los segmentos se refieren a la ruta base desde cero;
    /// retiro debe ser anterior o igual a entrega. Cada llamada calcula una ruta.
    /// El token se recibe por llamada y no se conserva desde la preparación.
    /// </summary>
    Task<ResultadoDesvioDto> CalcularAsync(
        int segmentoRetiro,
        int segmentoEntrega,
        CancellationToken cancellationToken = default);
}
