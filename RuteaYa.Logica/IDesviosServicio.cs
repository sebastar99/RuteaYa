using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IDesviosServicio
{
    /// <summary>
    /// Compara el recorrido base con uno que incorpora retiro y entrega.
    /// El límite en kilómetros se aplica a la distancia adicional, no a la total.
    /// </summary>
    /// <param name="rutaBase">Origen, paradas y destino en su orden original.</param>
    /// <param name="retiro">Coordenadas del retiro.</param>
    /// <param name="entrega">Coordenadas de la entrega.</param>
    /// <param name="desvioMaxKm">Límite no negativo de distancia adicional en kilómetros.</param>
    /// <param name="segmentoRetiro">
    /// Índice del segmento base entre rutaBase[i] y rutaBase[i + 1], desde cero.
    /// Debe estar entre 0 y rutaBase.Count - 2.
    /// </param>
    /// <param name="segmentoEntrega">
    /// Índice de segmento base válido, mayor o igual que segmentoRetiro.
    /// Si coinciden, se inserta retiro y luego entrega dentro de ese segmento.
    /// Los índices siempre se refieren a la ruta base, no a la ruta ya modificada.
    /// </param>
    /// <param name="cancellationToken">Token de cancelación de ambas consultas.</param>
    Task<ResultadoDesvioDto> CalcularAsync(
        IReadOnlyList<PuntoRutaDto> rutaBase,
        PuntoRutaDto retiro,
        PuntoRutaDto entrega,
        decimal desvioMaxKm,
        int segmentoRetiro,
        int segmentoEntrega,
        CancellationToken cancellationToken = default);
}
