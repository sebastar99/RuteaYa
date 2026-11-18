namespace RuteaYa.Logica.DTOs;

public class EvaluacionOportunidadDto
{
    public IReadOnlyList<DatoPendienteOportunidad> DatosPendientes { get; set; } = Array.Empty<DatoPendienteOportunidad>();
    public ResultadoCompatibilidadDto? ResultadoMotor { get; set; }

    public bool MotorEjecutado => ResultadoMotor != null;
    // Un rechazo probado prevalece; datos pendientes nunca convierten un resultado en Compatible.
    public EstadoCompatibilidad Estado => ResultadoMotor == null || ResultadoMotor.Estado == EstadoCompatibilidad.Compatible && DatosPendientes.Count > 0
        ? EstadoCompatibilidad.Indeterminado : ResultadoMotor.Estado;
}
