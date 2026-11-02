using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services
{
    public interface IViajesApiServicio
    {
        Task<(bool Exito, string Mensaje, List<ViajeViewModel> Viajes)> ListarViajesAsync(
            int usuarioId
        );

        Task<(bool Exito, string Mensaje)> CrearViajeAsync(
            int usuarioId, 
            ViajeViewModel modelo
        );

        Task<(bool Exito, string Mensaje, ViajeViewModel? Viaje)> ObtenerViajePorIdAsync(
            int usuarioId, 
            int viajeId
        );

        Task<(bool Exito, string Mensaje)> EditarViajeAsync(
            int usuarioId,
            int viajeId,
            ViajeViewModel modelo
        );

        Task<(bool Exito, string Mensaje)> DesactivarViajeAsync(
            int usuarioId,
            int viajeId
        );

        Task<(bool Exito, string Mensaje)> AgregarParadaAsync(
            int usuarioId,
            int viajeId,
            string ubicacion,
            int orden 
        );

        Task<(bool Exito, string Mensaje)> EliminarParadaAsync(
            int usuarioId,
            int viajeId,
            int paradaId
        );

        Task<(bool Exito, string Mensaje)> ReordenarParadasAsync(
            int usuarioId,
            int viajeId,
            List<int> paradasId
        );

    }
}