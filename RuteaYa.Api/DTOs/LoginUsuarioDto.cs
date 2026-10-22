
using System.ComponentModel.DataAnnotations;

namespace RuteaYa.Api.DTOs.Usuarios
{
    public class LoginUsuarioDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}
