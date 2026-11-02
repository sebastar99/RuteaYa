namespace RuteaYa.Logica.DTOs;

/// <summary>
/// Datos de una salida concreta. La recurrencia, las coordenadas confirmadas y
/// la capacidad restante se resuelven fuera del motor. No es una entidad persistida.
/// </summary>
public class ViajeCompatibilidadDto
{
    public int ViajeId { get; set; }
    public IReadOnlyList<PuntoRutaDto> RecorridoBase { get; set; } = Array.Empty<PuntoRutaDto>();
    public bool Activo { get; set; }
    public bool Disponible { get; set; }
    public decimal DesvioMaxKm { get; set; }

    /// <summary>
    /// Peso restante para la salida completa, no el máximo original. Null significa
    /// desconocido; cero significa que no queda peso disponible.
    /// </summary>
    public decimal? PesoDisponibleKg { get; set; }

    /// <summary>Cupos restantes para la salida completa. Null: desconocidos; cero: sin cupos.</summary>
    public int? CuposDisponibles { get; set; }

    /// <summary>Null significa que la fecha y hora de salida aún no fueron informadas.</summary>
    public DateTimeOffset? SalidaProgramada { get; set; }

    /// <summary>
    /// Límite de llegada. Por defecto es desconocido; SinRestriccion debe elegirse
    /// explícitamente. Si está informado y hay salida, no puede ser anterior a ella.
    /// </summary>
    public RestriccionTemporalDto LlegadaMaxima { get; set; } = RestriccionTemporalDto.Desconocida();
}
