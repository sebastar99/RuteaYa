namespace RuteaYa.Entidades;

public enum TipoRecursoUbicacion { Viaje = 1, Envio = 2 }
public enum CampoUbicacion { OrigenViaje = 1, DestinoViaje = 2, Parada = 3, RetiroEnvio = 4, EntregaEnvio = 5 }

/// <summary>
/// Snapshot servidor de una selección HERE. Primero es un comprobante pendiente;
/// al consumirlo queda vinculado al recurso. Su ubicación no vence después del guardado.
/// </summary>
public class UbicacionConfirmada
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TokenHash { get; set; } = "";
    public string SeleccionHash { get; set; } = "";
    public int UsuarioId { get; set; }
    public Guid FormularioId { get; set; }
    public TipoRecursoUbicacion TipoRecurso { get; set; }
    public int? RecursoId { get; set; }
    public CampoUbicacion Campo { get; set; }
    public string ClaveCampo { get; set; } = "";
    public string? RevisionRecurso { get; set; }
    public string IdHere { get; set; } = "";
    public string DireccionNormalizada { get; set; } = "";
    public decimal PosicionLatitud { get; set; }
    public decimal PosicionLongitud { get; set; }
    public decimal NavegacionLatitud { get; set; }
    public decimal NavegacionLongitud { get; set; }
    // -1 = position; >=0 = índice original de access. No se elige implícitamente.
    public int AccesoElegido { get; set; }
    public string TipoResultado { get; set; } = "";
    public string? TipoNumeroPuerta { get; set; }
    public bool PosicionEstimada { get; set; }
    public bool NumeroPuertaCorregido { get; set; }
    public string Proveedor { get; set; } = "HERE-v7";
    public DateTimeOffset ConfirmadaEnUtc { get; set; }
    public DateTimeOffset VenceEnUtc { get; set; }
    public DateTimeOffset? ConsumidaEnUtc { get; set; }
    // Token de concurrencia generado por aplicación, portable entre SQL Server y pruebas relacionales.
    public Guid Version { get; set; } = Guid.NewGuid();
}
