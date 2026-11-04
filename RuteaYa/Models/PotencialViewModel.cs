namespace RuteaYa.Web.Models
{
    public class PotencialViewModel
    {
        public decimal PotencialTotal { get; set; }

        public int EnviosCandidatos { get; set; }

        public int ViajesAnalizados { get; set; }

        public int PaquetesPosibles { get; set; }

        public List<decimal> MontosSemanales { get; set; } = new();

        public EnvioDashboardViewModel? MejorEnvio { get; set; }
    }
}
