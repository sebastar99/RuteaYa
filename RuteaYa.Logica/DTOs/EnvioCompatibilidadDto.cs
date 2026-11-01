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

    /// <summary>Inicio de retiro; desconocido por defecto, sin inventar disponibilidad inmediata.</summary>
    public RestriccionTemporalDto DisponibleDesde { get; set; } = RestriccionTemporalDto.Desconocida();

    /// <summary>
    /// Vencimiento desconocido por defecto. SinRestriccion solo si sabemos que no aplica.
    /// Si este límite y el inicio están informados, no puede ser anterior al inicio.
    /// </summary>
    public RestriccionTemporalDto FechaLimiteEntrega { get; set; } = RestriccionTemporalDto.Desconocida();
}
