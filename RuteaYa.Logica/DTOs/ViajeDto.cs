namespace RuteaYa.Logica.DTOs;

public class ViajeDto
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }

    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public bool OrigenConfirmado { get; set; }
    public bool DestinoConfirmado { get; set; }

    public List<int> DiasSemana { get; set; } = new();

    public TimeSpan HoraSalida { get; set; }
    public TimeSpan HoraLlegada { get; set; }

    public int CantMaxPaquetes { get; set; }
    public decimal PesoMax { get; set; }
    public decimal DesvioMaxKm { get; set; }

    public bool Activo { get; set; }

    public List<ParadaDto> Paradas { get; set; } = new();
    public DateTime CreadoEn { get; set; }
}

public class ParadaDto
{
    public bool Confirmada { get; set; }
    public int Id { get; set; }

    public string Ubicacion { get; set; } = "";

    public int Orden { get; set; }
}
