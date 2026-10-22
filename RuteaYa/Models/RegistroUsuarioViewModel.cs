
using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Web.Models
{
    public class RegistroUsuarioViewModel
    {
        [Required(ErrorMessage = "Ingresá tu nombre.")]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresá tu apellido.")]
        [StringLength(100)]
        public string Apellido { get; set; } = string.Empty;

        [Required(ErrorMessage = "Elegí un nombre de usuario.")]
        [StringLength(30, MinimumLength = 3,
            ErrorMessage = "Debe tener entre 3 y 30 caracteres.")]
        [RegularExpression(@"^[a-zA-Z0-9_.-]+$",
            ErrorMessage = "Usá solo letras, números, puntos, guiones y guiones bajos.")]
        public string NombreUsuario { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresá tu correo.")]
        [EmailAddress(ErrorMessage = "El correo no es válido.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Ingresá una contraseña.")]
        [StringLength(128, MinimumLength = 8,
            ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirmá tu contraseña.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password),
            ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmarPassword { get; set; } = string.Empty;
    }
}
