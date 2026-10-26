namespace RuteaYa.Logica.DTOs;

public class ViajeDto
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }

    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";

    public List<int> DiasSemana { get; set; } = new();

    public TimeSpan HoraSalida { get; set; }
    public TimeSpan HoraLlegada { get; set; }

    public int CantMaxPaquetes { get; set; }
    public decimal PesoMax { get; set; }
    public decimal DesvioMaxKm { get; set; }

    public bool Activo { get; set; }

    public List<ParadaDto> Paradas { get; set; } = new();
}

public class ParadaDto
{
    public int Id { get; set; }

    public string Ubicacion { get; set; } = "";

    public int Orden { get; set; }
}