namespace RuteaYa.Web.Models;

public class MapaRecorridoViewModel
{
    public long DistanciaMetros { get; set; }
    public long DuracionSegundos { get; set; }
    public List<List<decimal[]>> Secciones { get; set; } = new();
    public List<decimal[]> Puntos { get; set; } = new();
    public List<decimal[]> Posiciones { get; set; } = new();
    public List<string> Etiquetas { get; set; } = new();
    public List<string> Identificadores { get; set; } = new();
    public string? Teselas { get; set; }
    public string? Atribuciones { get; set; }

    public static MapaRecorridoViewModel Crear(PrevisualizacionRecorridoViewModel modelo, string? teselas, string? atribuciones) => new()
    {
        DistanciaMetros = modelo.Ruta!.Totales.DistanciaMetros, DuracionSegundos = modelo.Ruta.Totales.DuracionSegundos,
        Secciones = modelo.Ruta.Secciones.Select(s => s.Select(p => new[] { p.Latitud, p.Longitud }).ToList()).ToList(),
        Puntos = modelo.PuntosConfirmados.Select(p => new[] { p.Latitud, p.Longitud }).ToList(),
        Posiciones = modelo.CandidatosConfirmados.Select(c => new[] { c.Posicion.Latitud, c.Posicion.Longitud }).ToList(),
        Etiquetas = modelo.Ubicaciones.Select((u, i) => u.Etiqueta + ": " + modelo.CandidatosConfirmados[i].DireccionNormalizada).ToList(),
        Identificadores = modelo.CandidatosConfirmados.Select(c => c.IdHere).ToList(), Teselas = teselas, Atribuciones = atribuciones
    };
}
