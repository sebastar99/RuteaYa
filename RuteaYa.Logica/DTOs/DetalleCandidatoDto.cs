namespace RuteaYa.Logica.DTOs;

public class DetalleCandidatoDto
{
    public ViajeOportunidadesDto Viaje { get; set; } = new();
    public EnvioDto Envio { get; set; } = new();

    /// <summary>La lectura de datos y disponibilidad por estado no evalúa compatibilidad.</summary>
    public bool EvaluacionPendiente => true;
}
