namespace RuteaYa.Logica.DTOs;

/// <summary>
/// Información suplementaria de una fuente confiable del servidor. Un punto o
/// recorrido en estos campos ya debe estar confirmado; null significa que no lo está.
/// No sustituye Viaje/Envio ni representa persistencia o una confirmación del navegador.
/// </summary>
public class DatosConfirmadosOportunidadDto
{
    public int ViajeId { get; set; }
    public int EnvioId { get; set; }
    public IReadOnlyList<PuntoRutaDto>? RecorridoConfirmado { get; set; }
    public PuntoRutaDto? RetiroConfirmado { get; set; }
    public PuntoRutaDto? EntregaConfirmada { get; set; }
    public DateTimeOffset? SalidaProgramada { get; set; }
    public bool? Disponible { get; set; }
    public decimal? PesoDisponibleKg { get; set; }
    public int? CuposDisponibles { get; set; }
    public RestriccionTemporalDto LlegadaMaxima { get; set; } = RestriccionTemporalDto.Desconocida();
    public RestriccionTemporalDto DisponibleDesde { get; set; } = RestriccionTemporalDto.Desconocida();
    public RestriccionTemporalDto FechaLimiteEntrega { get; set; } = RestriccionTemporalDto.Desconocida();
}
