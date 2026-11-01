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
}
