
namespace RuteaYa.Web.Models
{
    public class DashboardViewModel
    {
        public int ProximosViajes { get; set; }

        public int EnviosPendientes { get; set; }

        public int OportunidadesDisponibles { get; set; }

        public decimal PotencialMensual { get; set; }

        public List<ViajeDashboardViewModel> ViajesRecientes { get; set; }
            = new();

        public List<EnvioDashboardViewModel> EnviosPendientesLista { get; set; }
            = new();
    }

    public class ViajeDashboardViewModel
    {
        public int Id { get; set; }
        public string Origen { get; set; } = string.Empty;

        public string Destino { get; set; } = string.Empty;

        public string Dias { get; set; } = string.Empty;

        public string HorarioSalida { get; set; } = string.Empty;

        public string HorarioLlegada { get; set; } = string.Empty;

        public string Estado { get; set; } = string.Empty;
    }

    public class EnvioDashboardViewModel
    {
        public int Id { get; set; }
       public string Origen { get; set; } = string.Empty;

        public string Destino { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public decimal Importe { get; set; }

        public string Estado { get; set; } = string.Empty;
    }
}
