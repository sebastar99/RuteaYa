using RuteaYa.Entidades;

namespace RuteaYa.Logica.DTOs;

public class EnvioCompatibilidadDto
{
    public int EnvioId { get; set; }

    // Obligatorios: no se inicializan en (0,0), que es una coordenada válida.
    public PuntoRutaDto Retiro { get; set; } = null!;
    public PuntoRutaDto Entrega { get; set; } = null!;

    public decimal PesoKg { get; set; }
    public EstadoEnvio Estado { get; set; }

    /// <summary>Null: no se informó una restricción de inicio de disponibilidad para retirar.</summary>
    public DateTimeOffset? DisponibleDesde { get; set; }

    /// <summary>
    /// Null: no se informó una restricción de vencimiento y no se inventa una.
    /// Si hay inicio, no puede ser anterior a él. No describe el estado actual del envío.
    /// </summary>
    public DateTimeOffset? FechaLimiteEntrega { get; set; }
}
