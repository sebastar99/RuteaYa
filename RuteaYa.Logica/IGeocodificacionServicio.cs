using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IGeocodificacionServicio
{
    /// <summary>
    /// Devuelve candidatos en orden de relevancia, sin elegir ni confirmar uno.
    /// Una lista vacía indica que la consulta válida no produjo resultados.
    /// </summary>
    Task<IReadOnlyList<ResultadoGeocodificacionDto>> GeocodificarAsync(
        string direccion,
        CancellationToken cancellationToken = default);
}
