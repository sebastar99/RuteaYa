namespace RuteaYa.Entidades;

public class DiaViaje {
    public int Id { get; set; }
    public int ViajeId { get; set; }

    public int DiaSemana {get; set; }
    public Viaje? Viaje { get; set; }

}