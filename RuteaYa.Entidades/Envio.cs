using System;
using RuteaYa.Entidades; // O el namespace donde esté definido EstadoEnvio

namespace RuteaYa.Entidades
{
    public class Envio
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string OrigenDir { get; set; } = string.Empty;
        public decimal OrigenLat { get; set; }
        public decimal OrigenLng { get; set; }
        public string DestinoDir { get; set; } = string.Empty;
        public decimal DestinoLat { get; set; }
        public decimal DestinoLng { get; set; }
        public DateTime FechaLimite { get; set; }
        public decimal PesoKg { get; set; }
        public TamanoEnvio Tamano { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public EstadoEnvio Estado { get; set; } = EstadoEnvio.BuscandoConductor;
        public DateTime CreadoEn { get; set; } = DateTime.Now;
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();


        public virtual Usuario? Usuario { get; set; }
    }

    public enum EstadoEnvio : byte
    {
        BuscandoConductor = 0,
        ConductorAsignado = 1,
        Retirado = 2,
        EnCamino = 3,
        Entregado = 4,
        Cancelado = 5
    }

    public enum TamanoEnvio : byte
    {
        Pequeno = 0,
        Mediano = 1,
        Grande = 2
    }
}