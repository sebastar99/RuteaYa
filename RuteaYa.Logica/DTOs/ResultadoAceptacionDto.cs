namespace RuteaYa.Logica.DTOs;

public enum EstadoAceptacion
{
    Aceptado, YaAceptado, NoEncontrado, EnvioNoDisponible, SalidaNoValida,
    CapacidadInsuficiente, Incompatible, DatosPendientes, ConflictoConcurrencia
}

public sealed class ResultadoAceptacionDto
{
    public EstadoAceptacion Estado { get; init; }
    public EvaluacionOportunidadDto? Evaluacion { get; init; }
    public bool Exito => Estado is EstadoAceptacion.Aceptado or EstadoAceptacion.YaAceptado;
}
