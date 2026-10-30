using RuteaYa.Entidades;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RuteaYa.Logica.DTOs
{
    public class CrearEnvioDto
    {
        [Required(ErrorMessage = "El  origen es obligatorio.")]
        public string OrigenDir { get; set; } = string.Empty;

        [Required(ErrorMessage = "El destino es obligatorio.")]
        public string DestinoDir { get; set; } = string.Empty;

        [Required(ErrorMessage = "La fecha límite es obligatoria.")]
        public DateTime FechaLimite { get; set; }

        [Range(0.1, 1000.0, ErrorMessage = "El peso debe ser mayor a 0 kg.")]
        public decimal PesoKg { get; set; }

        [Required(ErrorMessage = "Elegí un tamaño para el paquete.")]
        public TamanoEnvio Tamano { get; set; }

        [Required(ErrorMessage = "Ingresá una descripción de lo que enviás.")]
        public string Descripcion { get; set; } = string.Empty;

        [Range(1, 1000000, ErrorMessage = "El monto ofrecido debe ser mayor a 0.")]
        public decimal Monto { get; set; }
    }
}
