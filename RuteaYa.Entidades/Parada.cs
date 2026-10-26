using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Entidades;

public class Parada {
    public int Id { get; set; }
    public int ViajeId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Ubicacion { get; set; } = string.Empty;

    public int Orden { get; set; }

    public Viaje? Viaje { get; set; }
}