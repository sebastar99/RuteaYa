using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IBusquedaDireccionesServicio
{
    Task<IReadOnlyList<SugerenciaDireccionDto>> SugerirAsync(string texto, CancellationToken cancellationToken = default);
    Task<ResultadoGeocodificacionDto> ResolverAsync(string idHere, CancellationToken cancellationToken = default);
}
