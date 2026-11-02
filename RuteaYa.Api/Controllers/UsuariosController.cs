
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RuteaYa.Api.DTOs.Usuarios;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RuteaYa.Api.Controllers
{
    [ApiController]
    [Route("api/usuarios")]
    public class UsuariosController : ControllerBase
    {
        private readonly IUsuariosLogica _usuariosLogica;
        private readonly IConfiguration _configuration;

        public UsuariosController(
            IUsuariosLogica usuariosLogica,
            IConfiguration configuration)
        {
            _usuariosLogica = usuariosLogica;
            _configuration = configuration;
        }

        [HttpPost("registro")]
        public async Task<IActionResult> Registrar(
            [FromBody] RegistroUsuarioDto dto)
        {
            if (dto == null)
                return BadRequest(new
                {
                    mensaje = "Los datos del registro son obligatorios."
                });

            if (string.IsNullOrWhiteSpace(dto.Nombre) ||
                string.IsNullOrWhiteSpace(dto.Apellido) ||
                string.IsNullOrWhiteSpace(dto.Email) ||
                string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new
                {
                    mensaje = "Todos los campos son obligatorios."
                });
            }

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
            if (dto == null ||
                string.IsNullOrWhiteSpace(dto.Email) ||
                string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new
                {
                    mensaje = "El correo y la contraseña son obligatorios."
                });
            }

            var usuario =
                await _usuariosLogica.ValidarCredencialesAsync(
                    dto.Email.Trim(),
                    dto.Password);

            if (usuario == null)
            {
                return Unauthorized(new
                {
                    mensaje = "Correo o contraseña incorrectos."
                });
            }

            var issuer = _configuration["Jwt:Issuer"];
            var audience = _configuration["Jwt:Audience"];
            var key = _configuration["Jwt:Key"];

            if (string.IsNullOrWhiteSpace(issuer) ||
                string.IsNullOrWhiteSpace(audience) ||
                string.IsNullOrWhiteSpace(key))
            {
                return StatusCode(500, new
                {
                    mensaje = "La configuración JWT está incompleta."
                });
            }

            if (Encoding.UTF8.GetByteCount(key) < 32)
            {
                return StatusCode(500, new
                {
                    mensaje = "La clave JWT debe tener al menos 32 bytes."
                });
            }

            var claims = new List<Claim>
            {
                new(
                    ClaimTypes.NameIdentifier,
                    usuario.Id.ToString()),

                new(
                    JwtRegisteredClaimNames.Sub,
                    usuario.Id.ToString()),

                new(
                    ClaimTypes.Name,
                    usuario.Nombre),

                new(
                    ClaimTypes.Email,
                    usuario.Email),

                new(
                    JwtRegisteredClaimNames.Email,
                    usuario.Email),

                new(
                    "Apellido",
                    usuario.Apellido),

                new(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString())
            };

            var signingKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(key));

            var credentials = new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256);

            var expiration = DateTime.UtcNow.AddMinutes(
                int.TryParse(
                    _configuration["Jwt:ExpirationMinutes"],
                    out var minutes) && minutes > 0
                    ? minutes
                    : 30);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiration,
                signingCredentials: credentials);

            var tokenString = new JwtSecurityTokenHandler()
                .WriteToken(token);

            var respuestaUsuario = new UsuarioRespuestaDto
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Apellido = usuario.Apellido,
                Email = usuario.Email,
                Token = tokenString
            };

            return Ok(new
            {
                token = tokenString,
                tipo = "Bearer",
                expiraEn = expiration,
                usuario = respuestaUsuario
            });
        }

        [Authorize]
        [HttpGet("perfil")]
        public IActionResult Perfil()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue("sub")
                ?? User.FindFirstValue("nameid");

            var email = User.FindFirstValue(
                ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(id))
                return Unauthorized();

            return Ok(new
            {
                mensaje = "Token válido",
                id = id,
                email = email
            });
        }
    }
}
