using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>Extensión optativa para mapas; no modifica el contrato de cálculo de desvíos.</summary>
public interface IRutasGeometriaServicio
{
    Task<ResultadoRutaGeometriaDto> CalcularGeometriaAsync(
        IReadOnlyList<PuntoRutaDto> puntos, CancellationToken cancellationToken = default);
}
