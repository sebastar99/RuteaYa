using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public class DetalleOportunidadViewModel
{
    public DetalleCandidatoDto? Detalle { get; set; }
    public int ViajeId { get; set; }
    public int Pagina { get; set; } = 1;
    public int TamanoPagina { get; set; } = 10;
    public string? Error { get; set; }
}
