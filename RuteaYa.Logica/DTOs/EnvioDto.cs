using System;
using System.Collections.Generic;
using System.Text;
using RuteaYa.Entidades;

namespace RuteaYa.Logica.DTOs
{
    public class EnvioDto
    {
        public int Id { get; set; }
        public int UsuarioId { get; set; }
        public string OrigenDir { get; set; } = string.Empty;
        public string DestinoDir { get; set; } = string.Empty;
        public DateTime FechaLimite { get; set; }
        public decimal PesoKg { get; set; }
        public TamanoEnvio Tamano { get; set; }
        public string TamanoTexto => Tamano.ToString();
        public string Descripcion { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public EstadoEnvio Estado { get; set; }
        public string EstadoTexto => Estado.ToString();
    }
}
