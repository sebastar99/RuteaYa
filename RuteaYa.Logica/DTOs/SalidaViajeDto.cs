namespace RuteaYa.Logica.DTOs;

public class SalidaViajeDto
{
    public int Id { get; set; }
    public int ViajeId { get; set; }
    public DateTimeOffset FechaHora { get; set; }
    public RestriccionTemporalDto LlegadaMaxima { get; set; } = RestriccionTemporalDto.Desconocida();
    public bool Disponible { get; set; }
    public int PaquetesComprometidos { get; set; }
    public decimal PesoComprometidoKg { get; set; }
    public int? CuposDisponibles { get; set; }
    public decimal? PesoDisponibleKg { get; set; }
    public string? AdvertenciaCapacidad { get; set; }
}
