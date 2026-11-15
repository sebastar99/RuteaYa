using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IOportunidadesLogica
{
    /// <summary>
    /// Lista viajes propios y, si se seleccionó uno, envíos BuscandoConductor.
    /// Devuelve null tanto para un viaje inexistente como para uno ajeno.
    /// Evalúa compatibilidad sin reservar capacidad. Sin viaje no consulta envíos.
    /// </summary>
    Task<ListadoCandidatosDto?> ConsultarAsync(
        int conductorId,
        int? viajeId = null,
        int pagina = 1,
        int tamanoPagina = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene un candidato disponible para un viaje propio. Devuelve null sin
    /// distinguir viaje ajeno/inexistente o envío inexistente/no disponible.
    /// No consulta el envío si no se pudo verificar la propiedad del viaje.
    /// </summary>
    Task<DetalleCandidatoDto?> ObtenerDetalleAsync(
        int conductorId, int viajeId, int envioId, CancellationToken cancellationToken = default);

    Task<int?> SeleccionarSalidaAsync(int conductorId, SeleccionSalidaDto seleccion, CancellationToken ct = default);
    Task<ListadoCandidatosDto?> ConsultarSalidaAsync(int conductorId, int viajeId, int salidaId,
        int pagina = 1, int tamanoPagina = 10, CancellationToken ct = default);
    Task<DetalleCandidatoDto?> ObtenerDetalleSalidaAsync(int conductorId, int viajeId, int salidaId, int envioId, CancellationToken ct = default);
}
