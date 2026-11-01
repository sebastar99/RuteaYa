namespace RuteaYa.Logica.DTOs;

public class ListadoCandidatosDto
{
    public IReadOnlyList<ViajeOportunidadesDto> Viajes { get; set; } = Array.Empty<ViajeOportunidadesDto>();
    public ViajeOportunidadesDto? ViajeSeleccionado { get; set; }
    public IReadOnlyList<EnvioDto> Candidatos { get; set; } = Array.Empty<EnvioDto>();
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 10;
    public int TotalCandidatos { get; set; }
    public int TotalPaginas => TotalCandidatos == 0 ? 0 : (TotalCandidatos - 1) / TamanoPagina + 1;

    /// <summary>
    /// Los datos son candidatos por estado de publicación, no resultados del motor.
    /// No se han verificado coordenadas, salida, capacidad ni restricciones temporales.
    /// </summary>
    public bool EvaluacionPendiente => true;
}
