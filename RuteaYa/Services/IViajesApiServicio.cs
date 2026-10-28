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

    }
}