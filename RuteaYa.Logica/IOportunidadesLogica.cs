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
}
