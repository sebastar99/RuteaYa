using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IOportunidadesLogica
{
    /// <summary>
    /// Lista viajes propios y, si se seleccionó uno, envíos BuscandoConductor.
    /// Devuelve null tanto para un viaje inexistente como para uno ajeno.
    /// No evalúa compatibilidad ni reserva capacidad. Sin viaje no consulta envíos.
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
}
