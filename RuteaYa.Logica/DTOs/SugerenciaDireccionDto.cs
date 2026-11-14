namespace RuteaYa.Logica.DTOs;

// Autocomplete no proporciona coordenadas: se resuelven exclusivamente mediante lookup.
public class SugerenciaDireccionDto
{
    public string IdHere { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Localidad { get; set; } = "";
    public string Direccion { get; set; } = "";
}
