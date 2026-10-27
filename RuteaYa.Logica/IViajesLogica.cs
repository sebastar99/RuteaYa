using RuteaYa.Logica.DTOs;

namespace RuteaYa.Logica;

public interface IViajesLogica
{
    Task<List<ViajeDto>> ListarViajesAsync(
        int usuarioId, 
        string estado = "todos"
    );

    Task<ViajeDto?> ObtenerPorIdAsync(
        int usuarioId, 
        int id
    );

    Task<(bool Exito, string mensaje, ViajeDto? viaje)> CrearViajeAsync(
        int usuarioId, 
        CrearViajeDto dto
    );

    Task<(bool exito, string mensaje, ViajeDto? viaje)> EditarViajeAsync(
        int usuarioId, 
        int id, 
        EditarViajeDto dto
    );

    Task<(bool exito, string mensaje)> DesactivarViajeAsync(
        int usuarioId, 
        int id
    );

    Task<(bool exito, string mensaje, ParadaDto? parada)> AgregarParadaAsync(
        int usuarioId,
        int viajeId,
        string ubicacion,
        int Orden
    );

    Task<(bool exito, string mensaje)> EliminarParadaAsync(
        int usuarioId, 
        int viajeId, 
        int paradaId
    );
}