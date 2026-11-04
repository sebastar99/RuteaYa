using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IRutasServicio
{
    /// <summary>
    /// Calcula un recorrido respetando el orden: origen, paradas y destino.
    /// La distancia se expresa en metros y la duración en segundos.
    /// </summary>
    Task<ResultadoRutaDto> CalcularAsync(
        IReadOnlyList<PuntoRutaDto> puntos,
        CancellationToken cancellationToken = default);
}
