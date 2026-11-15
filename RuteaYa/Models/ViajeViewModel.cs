using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Web.Models
{
    public class ViajeViewModel
    {
        public string? FormularioDirecciones { get; set; }
        public string? OrigenComprobante { get; set; }
        public string? DestinoComprobante { get; set; }
        public bool OrigenConfirmado { get; set; }
        public bool DestinoConfirmado { get; set; }
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public RuteaYa.Logica.DTOs.UbicacionesViajeDto? Ubicaciones { get; set; }
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public CampoDireccionViewModel? OrigenCampo { get; set; }
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public CampoDireccionViewModel? DestinoCampo { get; set; }
        public int Id { get; set; }

        [Required(ErrorMessage = "Ingresar el origen.")]
        [StringLength(150)]
        public string Origen { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresar el destino.")]
        [StringLength(150)]
        public string Destino { get; set; } = string.Empty;

        [Required(ErrorMessage = "Selecciona al menos un dia.")]
        [MinLength(1, ErrorMessage = "Selecciona al menos un dia.")]
        public List<int> DiasSemana { get; set; } = new();

        [Required(ErrorMessage = "Ingresar la hora de salida.")]
        public TimeSpan HoraSalida { get; set; }

        [Required(ErrorMessage = "Ingresar la hora de llegada.")]
        public TimeSpan HoraLlegada { get; set; }

        [Range(1, 100, ErrorMessage = "La cantidad debe estar entre 1 y 100.")]
        public int CantMaxPaquetes { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Ingresar un peso valido.")]
        public decimal PesoMax { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Ingresar un desvio valido.")]
        public decimal DesvioMaxKm { get; set; }

        // Compatibilidad de modelos anteriores; los formularios y el gateway ya no leen ni dividen este campo.
        public string ParadasTexto { get; set; } = string.Empty;

        // Paradas recibidas desde la API.
        public List<ParadaViewModel> Paradas { get; set; } = new();

        public bool Activo { get; set; }

        public DateTime CreadoEn { get; set; }
    }

    public class ParadaViewModel
    {
        public string ClaveCampo { get; set; } = Guid.NewGuid().ToString("N");
        public string? Comprobante { get; set; }
        public bool Confirmada { get; set; }
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public CampoDireccionViewModel? CampoDireccion { get; set; }
        public int Id { get; set; }

        public string Ubicacion { get; set; } = string.Empty;

        public int Orden { get; set; }
    }
}
