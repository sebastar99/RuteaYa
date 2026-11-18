using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public class OptimizadorViewModel
{
    public IReadOnlyList<ViajeOportunidadesDto> Viajes { get; set; } = Array.Empty<ViajeOportunidadesDto>();
    public ViajeOportunidadesDto? ViajeSeleccionado { get; set; }
    public int[] DiasHabilitados { get; set; } = Array.Empty<int>();
    public TimeSpan HoraSalida { get; set; }
    public string? ZonaAutomatica { get; set; }
    public SeleccionSalidaDto Seleccion { get; set; } = new();
    public string? Error { get; set; }
}

public class OptimizadorResultadoViewModel
{
    public ResultadoOptimizacionDto Resultado { get; set; } = new();
    public string? Error { get; set; }
}

public class ConfirmarCombinacionViewModel
{
    public SeleccionEvaluadaDto Evaluacion { get; set; } = new();
    public int[] EnvioIds { get; set; } = Array.Empty<int>();
    public string? Error { get; set; }
}
