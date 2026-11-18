using RuteaYa.Entidades;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

/// <summary>Solo un adaptador servidor de la selección puede actuar como fuente; no implementar con datos del navegador.</summary>
public interface IFuenteSeleccionUbicacionVerificada
{
    SeleccionUbicacionVerificadaDto Obtener(int usuarioId, ContextoUbicacionDto contexto, string tokenSeleccion);
}

public interface IUbicacionesConfirmadasServicio
{
    Task<ContextoUbicacionDto> CrearContextoAsync(int usuarioId, TipoRecursoUbicacion tipo, int? recursoId,
        CampoUbicacion campo, string claveCampo, Guid? formularioId = null, CancellationToken ct = default);
    Task<ComprobanteUbicacionDto> EmitirAsync(int usuarioId, ContextoUbicacionDto contexto,
        string tokenSeleccion, CancellationToken ct = default);
    Task GuardarAsync(int usuarioId, IReadOnlyList<AsignacionUbicacionDto> asignaciones, CancellationToken ct = default);
    Task<string> ObtenerDireccionAsync(int usuarioId, ContextoUbicacionDto contexto, string token, CancellationToken ct = default);
    Task GuardarViajeAsync(int usuarioId, Viaje viaje, ContextoUbicacionDto contexto,
        IReadOnlyList<AsignacionUbicacionDto> asignaciones, Func<CancellationToken, Task>? prepararOrden = null, CancellationToken ct = default);
}
