using RuteaYa.Entidades;

namespace RuteaYa.Logica.DTOs;

/// <summary>Un punto del recorrido ordenado: origen, paradas originales, retiros, entregas y destino.</summary>
public sealed class ParadaRecorridoDto
{
    public string Tipo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public int? EnvioId { get; set; }
}

public sealed class PaqueteRecorridoDto
{
    public int EnvioId { get; set; }
    public string Descripcion { get; set; } = "";
    public string OrigenDir { get; set; } = "";
    public string DestinoDir { get; set; } = "";
    public PuntoRutaDto Retiro { get; set; } = null!;
    public PuntoRutaDto Entrega { get; set; } = null!;
    public RestriccionTemporalDto DisponibleDesde { get; set; } = RestriccionTemporalDto.Desconocida();
    public RestriccionTemporalDto FechaLimiteEntrega { get; set; } = RestriccionTemporalDto.Desconocida();
}

/// <summary>Datos fijos de la salida: recorrido original ya confirmado y sus restricciones. La ruta base se calcula una sola vez.</summary>
public sealed class ContextoRecorridoDto
{
    public IReadOnlyList<PuntoRutaDto> Base { get; set; } = Array.Empty<PuntoRutaDto>();
    public IReadOnlyList<string> EtiquetasBase { get; set; } = Array.Empty<string>();
    public decimal DesvioMaxKm { get; set; }
    public DateTimeOffset? Salida { get; set; }
    public RestriccionTemporalDto LlegadaMaxima { get; set; } = RestriccionTemporalDto.Desconocida();
    public long DistanciaBaseMetros { get; set; }
    public long DuracionBaseSegundos { get; set; }
}

public sealed class ResultadoRecorridoDto
{
    public EstadoCompatibilidad Estado { get; set; } = EstadoCompatibilidad.Indeterminado;
    public IReadOnlyList<MotivoCompatibilidad> Motivos { get; set; } = Array.Empty<MotivoCompatibilidad>();
    public IReadOnlyList<ParadaRecorridoDto> Orden { get; set; } = Array.Empty<ParadaRecorridoDto>();
    public long DistanciaOriginalMetros { get; set; }
    public long DuracionOriginalSegundos { get; set; }
    public long DistanciaCombinadaMetros { get; set; }
    public long DuracionCombinadaSegundos { get; set; }
    /// <summary>Distancia combinada menos distancia original. Puede ser negativa si el proveedor estima un recorrido más corto.</summary>
    public long DesvioMetros => DistanciaCombinadaMetros - DistanciaOriginalMetros;
    public long TiempoAdicionalSegundos => DuracionCombinadaSegundos - DuracionOriginalSegundos;
    public DateTimeOffset? FinEstimado { get; set; }
    /// <summary>Se compararon todos los órdenes posibles que no estaban descartados por una cota demostrada.</summary>
    public bool Completo { get; set; } = true;
    /// <summary>Ningún orden puede cumplir el desvío máximo ni siquiera en línea recta.</summary>
    public bool ImposibleProbado { get; set; }
}

public sealed class PaqueteOptimizadoDto
{
    public int EnvioId { get; set; }
    public string Descripcion { get; set; } = "";
    public string OrigenDir { get; set; } = "";
    public string DestinoDir { get; set; } = "";
    public decimal PesoKg { get; set; }
    public decimal Monto { get; set; }
    public DateTime FechaLimite { get; set; }
}

public sealed class CombinacionDto
{
    public IReadOnlyList<PaqueteOptimizadoDto> Paquetes { get; set; } = Array.Empty<PaqueteOptimizadoDto>();
    public decimal GananciaTotal { get; set; }
    public decimal PesoTotalKg { get; set; }
    public int CantidadPaquetes => Paquetes.Count;
    public long DistanciaOriginalMetros { get; set; }
    public long DistanciaCombinadaMetros { get; set; }
    public long DesvioMetros { get; set; }
    public long DuracionOriginalSegundos { get; set; }
    public long DuracionCombinadaSegundos { get; set; }
    public long TiempoAdicionalSegundos { get; set; }
    public DateTimeOffset? FinEstimado { get; set; }
    public IReadOnlyList<ParadaRecorridoDto> Orden { get; set; } = Array.Empty<ParadaRecorridoDto>();
    /// <summary>Huella del estado evaluado (salida y envíos). Solo detecta cambios; el servidor recalcula todo al reservar.</summary>
    public string Huella { get; set; } = "";
}

public sealed class DescarteDto
{
    public int EnvioId { get; set; }
    public string Descripcion { get; set; } = "";
    public string Motivo { get; set; } = "";
}

public sealed class ResultadoOptimizacionDto
{
    public int ViajeId { get; set; }
    public int SalidaId { get; set; }
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public DateTimeOffset? Salida { get; set; }
    public int CupoTotal { get; set; }
    public int CuposComprometidos { get; set; }
    public int CuposDisponibles { get; set; }
    public decimal PesoMaximoKg { get; set; }
    public decimal PesoComprometidoKg { get; set; }
    public decimal PesoDisponibleKg { get; set; }
    public decimal DesvioMaxKm { get; set; }
    public int CandidatosDisponibles { get; set; }
    public int CandidatosEvaluados { get; set; }
    public CombinacionDto? Recomendada { get; set; }
    public IReadOnlyList<CombinacionDto> Alternativas { get; set; } = Array.Empty<CombinacionDto>();
    public IReadOnlyList<DescarteDto> Descartados { get; set; } = Array.Empty<DescarteDto>();
    /// <summary>Motivo por el que no se pudo optimizar esta salida (por ejemplo, ya está en curso).</summary>
    public string? Bloqueo { get; set; }
    /// <summary>Verdadero solo si se exploraron todas las alternativas relevantes dentro de los límites de búsqueda.</summary>
    public bool BusquedaCompleta { get; set; } = true;
    public string? Aviso { get; set; }
    public int CombinacionesEvaluadas { get; set; }
    public int SolicitudesDeRuta { get; set; }
}

public sealed class SeleccionEvaluadaDto
{
    public bool Valida { get; set; }
    /// <summary>Los envíos ya están reservados por este conductor en esta salida (reintento idempotente).</summary>
    public bool YaReservada { get; set; }
    public string? Motivo { get; set; }
    public IReadOnlyList<int> EnviosNoDisponibles { get; set; } = Array.Empty<int>();
    public IReadOnlyList<MotivoCompatibilidad> Motivos { get; set; } = Array.Empty<MotivoCompatibilidad>();
    public CombinacionDto? Combinacion { get; set; }
    public string Huella { get; set; } = "";
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public DateTimeOffset? Salida { get; set; }
    public int ViajeId { get; set; }
    public int SalidaId { get; set; }
    public int CuposDisponibles { get; set; }
    public decimal PesoDisponibleKg { get; set; }
}

/// <summary>Recorrido original confirmado del viaje: origen, paradas en orden y destino, con sus direcciones.</summary>
public sealed class RecorridoConfirmadoDto
{
    public IReadOnlyList<PuntoRutaDto> Puntos { get; set; } = Array.Empty<PuntoRutaDto>();
    public IReadOnlyList<string> Etiquetas { get; set; } = Array.Empty<string>();
}

/// <summary>Un envío ya asignado a la salida. Sus restricciones se conservan; su recompensa no vuelve a contarse.</summary>
public sealed class PaqueteComprometidoDto
{
    public int EnvioId { get; set; }
    public string Descripcion { get; set; } = "";
    public string OrigenDir { get; set; } = "";
    public string DestinoDir { get; set; } = "";
    public decimal PesoKg { get; set; }
    public EstadoEnvio Estado { get; set; }
    public PuntoRutaDto Retiro { get; set; } = null!;
    public PuntoRutaDto Entrega { get; set; } = null!;
    public RestriccionTemporalDto DisponibleDesde { get; set; } = RestriccionTemporalDto.Desconocida();
    public RestriccionTemporalDto FechaLimiteEntrega { get; set; } = RestriccionTemporalDto.Desconocida();
    public byte[] Version { get; set; } = Array.Empty<byte>();
}

public enum EstadoReserva
{
    Reservado, YaReservado, NoEncontrado, EnvioNoDisponible, SalidaNoValida, Incompatible, Cambio, ConflictoConcurrencia
}

public sealed class ResultadoReservaDto
{
    public EstadoReserva Estado { get; init; }
    public string? Mensaje { get; init; }
    public IReadOnlyList<int> EnviosEnConflicto { get; init; } = Array.Empty<int>();
    public SeleccionEvaluadaDto? Evaluacion { get; init; }
    public bool Exito => Estado is EstadoReserva.Reservado or EstadoReserva.YaReservado;
}
