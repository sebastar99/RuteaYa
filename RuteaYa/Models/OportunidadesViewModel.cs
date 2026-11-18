using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public class OportunidadesViewModel
{
    public ListadoCandidatosDto Listado { get; set; } = new();
    public string? Error { get; set; }
    public int[] DiasHabilitados { get; set; } = Array.Empty<int>();
    public TimeSpan HoraSalida { get; set; }
    public string? ZonaAutomatica { get; set; }
}
