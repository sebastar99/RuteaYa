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
    /// Mejor inserción geográficamente válida. Null si no se evaluó el desvío
    /// o si todas las alternativas exceden el límite; los motivos distinguen esos casos.
    /// Su presencia no certifica compatibilidad temporal ni reserva capacidad.
    /// </summary>
    public InsercionCompatibilidadDto? InsercionSeleccionada { get; set; }
}
