namespace RuteaYa.Logica.DTOs;

public class SeleccionSalidaDto
{
    public int ViajeId { get; set; }
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Elegí la fecha de salida.")]
    public DateOnly? Fecha { get; set; }
    public string? ZonaHorariaId { get; set; }
    public DateTime? LlegadaMaximaLocal { get; set; }
    public bool LlegadaSinRestriccion { get; set; }
}
