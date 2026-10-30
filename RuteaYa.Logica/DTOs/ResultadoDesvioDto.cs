namespace RuteaYa.Logica.DTOs;

public class ResultadoDesvioDto
{
    public long DistanciaOriginalMetros { get; set; }
    public long DuracionOriginalSegundos { get; set; }
    public long DistanciaConEnvioMetros { get; set; }
    public long DuracionConEnvioSegundos { get; set; }

    /// <summary>
    /// Distancia con envío menos distancia original. Un valor negativo representa
    /// una reducción estimada por el proveedor y cumple un límite no negativo.
    /// </summary>
    public long DistanciaAdicionalMetros { get; set; }

    /// <summary>
    /// Duración con envío menos duración original. Un valor negativo representa
    /// una reducción estimada por el proveedor; no se reemplaza por cero.
    /// </summary>
    public long TiempoAdicionalSegundos { get; set; }

    public bool CumpleLimiteDesvio { get; set; }
}
