using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Logica.DTOs;

public class CrearViajeDto
{
    [Required(ErrorMessage = "El origen es obligatorio.")]
    [StringLength(150, MinimumLength = 2)]
    public string Origen { get; set; } = "";

    [Required(ErrorMessage = "El destino es obligatorio.")]
    [StringLength(150, MinimumLength = 2)]
    public string Destino { get; set; } = "";

    [Required]
    [MinLength(1, ErrorMessage = "Seleccioná al menos un día.")]
    [MaxLength(7)]
    public List<int> DiasSemana { get; set; } = new();

    public TimeSpan HoraSalida { get; set; }

    public TimeSpan HoraLlegada { get; set; }

    [Range(1, 100)]
    public int CantMaxPaquetes { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal PesoMax { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal DesvioMaxKm { get; set; }

    [MaxLength(50)]
    public List<string> Paradas { get; set; } = new();
}
