using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public record SugerenciaSeleccionDireccion(string Token, SugerenciaDireccionDto Sugerencia);

public class EstadoSeleccionDireccion
{
    public string Token { get; set; } = "";
    public ResultadoGeocodificacionDto Ubicacion { get; set; } = new();
    public bool Confirmada { get; set; }
    // -1: position, >=0: índice de access. No se interpreta ausencia como selección.
    public int? Acceso { get; set; }
    public PuntoRutaDto? Navegacion { get; set; }
    public DateTimeOffset? ConfirmadaEnUtc { get; set; }
    public DateTimeOffset? VenceEnUtc { get; set; }
}

public class CampoDireccionViewModel
{
    public string? NombreTexto { get; set; }
    public string? TokenInicial { get; set; }
    public bool ConservarActual { get; set; }
    public bool ConfirmadaPersistida { get; set; }
    public string IdHtml { get; set; } = "";
    public string Etiqueta { get; set; } = "";
    public string TextoInicial { get; set; } = "";
    public string NombreToken { get; set; } = "";
    public string UrlSugerencias { get; set; } = "";
    public string UrlResolver { get; set; } = "";
    public string UrlConfirmar { get; set; } = "";
}
