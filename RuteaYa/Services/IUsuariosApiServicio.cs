
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services
{
    public interface IUsuariosApiServicio
    {
        Task<(bool Exito, string Mensaje)> RegistrarAsync(
            RegistroUsuarioViewModel modelo);

        Task<(bool Exito, string Mensaje, UsuarioApiRespuesta? Usuario, string? Token)>
            LoginAsync(LoginUsuarioViewModel modelo);


    }

    public class UsuarioApiRespuesta
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
