namespace RuteaYa.Logica.DTOs;

public class ResultadoRutaGeometriaDto
{
    public ResultadoRutaDto Totales { get; set; } = new();

    // Cada sección conserva su propia geometría: no inventar enlaces entre secciones.
    public List<List<PuntoRutaDto>> Secciones { get; set; } = new();
}
