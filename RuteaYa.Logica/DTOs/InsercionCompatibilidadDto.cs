namespace RuteaYa.Logica.DTOs;

public class InsercionCompatibilidadDto
{
    /// <summary>Segmento de la ruta base entre los puntos i e i + 1, desde cero.</summary>
    public int SegmentoRetiro { get; set; }

    /// <summary>
    /// Segmento base mayor o igual al de retiro. Si coinciden, retiro precede a entrega.
    /// Los índices no se desplazan por los puntos insertados.
    /// </summary>
    public int SegmentoEntrega { get; set; }

    public ResultadoDesvioDto Desvio { get; set; } = null!;
}
