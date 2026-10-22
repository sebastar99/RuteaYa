
using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Web.Models
{
    public class LoginUsuarioViewModel
    {
        [Required(ErrorMessage = "Ingresá tu correo.")]
        [EmailAddress(ErrorMessage = "El correo no es válido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresá tu contraseña.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
