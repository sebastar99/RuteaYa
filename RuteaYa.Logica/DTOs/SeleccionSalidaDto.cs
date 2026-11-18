namespace RuteaYa.Logica.DTOs;

public class SeleccionSalidaDto
{
    public int ViajeId { get; set; }
    public DateOnly? Fecha { get; set; }
    public int? OffsetUtcMinutos { get; set; }
    public string? ZonaHorariaId { get; set; }
    public DateTime? LlegadaMaximaLocal { get; set; }
    public bool LlegadaSinRestriccion { get; set; }
}
