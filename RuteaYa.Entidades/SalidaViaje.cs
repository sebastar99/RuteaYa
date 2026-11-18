namespace RuteaYa.Entidades;

/// <summary>Una ocurrencia del viaje recurrente. Los cupos se derivan de sus envíos, no se duplican aquí.</summary>
public class SalidaViaje
{
    public int Id { get; set; }
    public int ViajeId { get; set; }
    public Viaje Viaje { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public DateTimeOffset FechaHora { get; set; }
    public DateTimeOffset? LlegadaMaxima { get; set; }
    public bool LlegadaSinRestriccion { get; set; }
    public bool Disponible { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
}
