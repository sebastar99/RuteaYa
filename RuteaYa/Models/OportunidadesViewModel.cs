using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public class OportunidadesViewModel
{
    public ListadoCandidatosDto Listado { get; set; } = new();
    public string? Error { get; set; }
}
