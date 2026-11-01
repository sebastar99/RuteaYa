namespace RuteaYa.Logica.DTOs;

public class ResultadoCompatibilidadDto
{
    public int ViajeId { get; set; }
    public int EnvioId { get; set; }
    public EstadoCompatibilidad Estado { get; set; } = EstadoCompatibilidad.Indeterminado;

    /// <summary>
    /// Incluye rechazos comprobados, datos desconocidos y evaluaciones pendientes.
    /// Un rechazo comprobado tiene prioridad sobre los motivos de indeterminación.
    /// </summary>
    public IReadOnlyList<MotivoCompatibilidad> Motivos { get; set; } = Array.Empty<MotivoCompatibilidad>();

    /// <summary>
    /// Mejor inserción compatible; en su ausencia, mejor provisional indeterminada.
    /// Null si hubo rechazo previo o ninguna alternativa quedó compatible o indeterminada.
    /// Su presencia no reserva capacidad. EstadoTemporal distingue definitiva de provisional.
    /// </summary>
    public InsercionCompatibilidadDto? InsercionSeleccionada { get; set; }
}
