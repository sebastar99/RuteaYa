using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Models;

public class PrevisualizacionRecorridoViewModel
{
    public int ViajeId { get; set; }
    public string? Sesion { get; set; }
    public string? Error { get; set; }
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public List<UbicacionPrevisualizacion> Ubicaciones { get; set; } = new();
    public ResultadoRutaGeometriaDto? Ruta { get; set; }
    public List<PuntoRutaDto> PuntosConfirmados { get; set; } = new();
    public List<ResultadoGeocodificacionDto> CandidatosConfirmados { get; set; } = new();
    public List<int> AccesosConfirmados { get; set; } = new();
}

public class UbicacionPrevisualizacion
{
    public string Etiqueta { get; set; } = "";
    public string Direccion { get; set; } = "";
    public List<ResultadoGeocodificacionDto> Candidatos { get; set; } = new();
}

public class ConfirmarRecorridoFormulario
{
    public string Sesion { get; set; } = "";
    // Un índice por ubicación. -1 significa position; >= 0, índice explícito de access.
    public List<SeleccionUbicacion> Selecciones { get; set; } = new();
}

public class SeleccionUbicacion
{
    public int? Candidato { get; set; }
    public int? Acceso { get; set; }
}
