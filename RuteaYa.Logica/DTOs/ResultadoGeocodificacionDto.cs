namespace RuteaYa.Logica.DTOs;

public class ResultadoGeocodificacionDto
{
    public string IdHere { get; set; } = string.Empty;
    public string DireccionNormalizada { get; set; } = string.Empty;

    /// <summary>Ubicación de referencia para mostrar en un mapa; no se reemplaza por access.</summary>
    public PuntoRutaDto Posicion { get; set; } = new();

    /// <summary>
    /// Accesos de navegación informados por HERE, en su orden original.
    /// Puede estar vacía. El consumidor debe elegir expresamente cuál usar para rutas.
    /// </summary>
    public List<PuntoRutaDto> Accesos { get; set; } = new();

    public string TipoResultado { get; set; } = string.Empty;
    public string? TipoNumeroPuerta { get; set; }
    public bool NumeroPuertaCorregido { get; set; }
    public bool PosicionEstimada { get; set; }

    /// <summary>Coincidencia de la consulta entre 0 y 1; no representa precisión en metros.</summary>
    public decimal? PuntajeConsulta { get; set; }

    /// <summary>
    /// fieldScore de HERE: valores escalares normalizados a listas de un elemento
    /// y listas (por ejemplo streets) conservadas completas. Sin inventar campos ausentes.
    /// </summary>
    public Dictionary<string, List<decimal>> PuntajesCampos { get; set; } = new();

    /// <summary>
    /// Política inicial: ningún candidato se acepta automáticamente, ni siquiera
    /// houseNumber/PA con puntaje 1. La confirmación futura pertenece al consumidor.
    /// </summary>
    public bool RequiereConfirmacion => true;
}
