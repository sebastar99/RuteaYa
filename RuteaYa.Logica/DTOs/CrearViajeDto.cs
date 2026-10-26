using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Logica.DTOs;

public class CrearViajeDto
{
    [Required]
    [MaxLength(150)]
    public string Origen { get; set; } = "";

    [Required]
    [MaxLength(150)]
    public string Destino { get; set; } = "";

    [Required]
    [MinLength(1)]
    public List<int> DiasSemana { get; set; } = new();

    public TimeSpan HoraSalida { get; set; }
    public TimeSpan HoraLlegada { get; set; }

    [Range(1, 100)]
    public int CantMaxPaquetes { get; set; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal PesoMax { get; set; }

    [Range(typeof(decimal), "0", "100000")]
    public decimal DesvioMaxKm { get; set; }

    public List<string> Paradas { get; set; } = new();
}