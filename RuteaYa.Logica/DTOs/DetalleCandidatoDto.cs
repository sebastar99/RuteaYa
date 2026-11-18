namespace RuteaYa.Logica.DTOs;

public class DetalleCandidatoDto
{
    public ViajeOportunidadesDto Viaje { get; set; } = new();
    public EnvioDto Envio { get; set; } = new();

    public EvaluacionOportunidadDto Evaluacion { get; set; } = new();
    public bool EvaluacionPendiente => !Evaluacion.MotorEjecutado || Evaluacion.Estado == EstadoCompatibilidad.Indeterminado;
}
