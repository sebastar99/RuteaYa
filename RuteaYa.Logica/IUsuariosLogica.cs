
using RuteaYa.Entidades;

namespace RuteaYa.Logica
{
    public interface IUsuariosLogica
    {
        Task<(bool Exito, string Mensaje, Usuario? Usuario)>
            RegistrarAsync(Usuario usuario, string password);

        Task<Usuario?> ValidarCredencialesAsync(
            string email,
            string password);
    }
}
