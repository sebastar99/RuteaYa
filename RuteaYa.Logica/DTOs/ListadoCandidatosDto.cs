namespace RuteaYa.Logica.DTOs;

public class ListadoCandidatosDto
{
    public IReadOnlyList<ViajeOportunidadesDto> Viajes { get; set; } = Array.Empty<ViajeOportunidadesDto>();
    public ViajeOportunidadesDto? ViajeSeleccionado { get; set; }
    public IReadOnlyList<EnvioDto> Candidatos { get; set; } = Array.Empty<EnvioDto>();
    public IReadOnlyDictionary<int, EvaluacionOportunidadDto> Evaluaciones { get; set; } = new Dictionary<int, EvaluacionOportunidadDto>();
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 10;
    public int TotalCandidatos { get; set; }
    public int TotalPaginas => TotalCandidatos == 0 ? 0 : (TotalCandidatos - 1) / TamanoPagina + 1;

    /// <summary>
    /// Hay al menos una evaluación indeterminada o todavía no hay resultados.
    /// </summary>
    public bool EvaluacionPendiente => Evaluaciones.Count == 0 || Evaluaciones.Count != Candidatos.Count || Evaluaciones.Values.Any(e => e.Estado == EstadoCompatibilidad.Indeterminado);
}
