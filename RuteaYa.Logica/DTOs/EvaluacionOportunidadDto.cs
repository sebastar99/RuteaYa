namespace RuteaYa.Logica.DTOs;

public class EvaluacionOportunidadDto
{
    public IReadOnlyList<DatoPendienteOportunidad> DatosPendientes { get; set; } = Array.Empty<DatoPendienteOportunidad>();
    public ResultadoCompatibilidadDto? ResultadoMotor { get; set; }

    // Un payload incompleto no se convierte en Compatible aunque contenga un resultado.
    public bool MotorEjecutado => DatosPendientes.Count == 0 && ResultadoMotor != null;
    public EstadoCompatibilidad Estado => MotorEjecutado ? ResultadoMotor!.Estado : EstadoCompatibilidad.Indeterminado;
}
