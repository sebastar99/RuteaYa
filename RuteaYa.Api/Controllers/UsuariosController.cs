
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Api.DTOs.Usuarios;
using RuteaYa.Entidades;
using RuteaYa.Logica;

namespace RuteaYa.Api.Controllers
{
    [ApiController]
    [Route("api/usuarios")]
    public class UsuariosController : ControllerBase
    {
        private readonly IUsuariosLogica _usuariosLogica;

        public UsuariosController(IUsuariosLogica usuariosLogica)
        {
            _usuariosLogica = usuariosLogica;
        }

        [HttpPost("registro")]
        public async Task<IActionResult> Registrar(
            [FromBody] RegistroUsuarioDto dto)
        {
            var usuario = new Usuario
            {
                Nombre = dto.Nombre.Trim(),
                Apellido = dto.Apellido.Trim(),
                Email = dto.Email.Trim()
            };

            var resultado = await _usuariosLogica.RegistrarAsync(
                usuario,
                dto.Password);

            if (!resultado.Exito)
            {
                return BadRequest(new
                {
                    mensaje = resultado.Mensaje
                });
            }

            var respuesta = new UsuarioRespuestaDto
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Apellido = usuario.Apellido,
                Email = usuario.Email
            };

            return StatusCode(201, respuesta);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(
            [FromBody] LoginUsuarioDto dto)
        {
            var usuario =
                await _usuariosLogica.ValidarCredencialesAsync(
                    dto.Email,
                    dto.Password);

            if (usuario == null)
            {
                return Unauthorized(new
                {
                    mensaje = "Correo o contraseña incorrectos."
                });
            }

            var respuesta = new UsuarioRespuestaDto
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Apellido = usuario.Apellido,
                Email = usuario.Email
            };

            return Ok(respuesta);
        }
    }
}
