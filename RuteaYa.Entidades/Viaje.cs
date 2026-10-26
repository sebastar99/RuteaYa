using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Entidades;

public class Viaje {
    public int Id { get; set; }
    public int UsuarioId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Destino { get; set; }

    [Required]
    [MaxLength(150)]
    public string Origen { get; set; }

    // Horario del viaje
    public TimeSpan HoraSalida { get; set; }
    public TimeSpan HoraLlegada { get; set; }

    // capacidades
    [Range(1, 1000)]
    public int CantMaxPaquetes { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal PesoMax { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal DesvioMaxKm { get; set; }

    public bool Activo { get; set; }

    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;

    public ICollection<DiaViaje> Dias {get; set; } = new List<DiaViaje>();
    public ICollection<Parada> Paradas { get; set; } = new List<Parada>();

}