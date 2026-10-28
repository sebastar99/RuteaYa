
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
                var usuario = await _usuariosLogica.ValidarCredencialesAsync(
                    dto.Email,
                    dto.Password);

                if (usuario == null)
                {
                    return Unauthorized(new
                    {
                        mensaje = "Correo o contraseña incorrectos."
                    });
                }

                var issuer = _configuration["Jwt:Issuer"]!;
                var audience = _configuration["Jwt:Audience"]!;
                var key = _configuration["Jwt:Key"]!;

                var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, usuario.Email),
                new(ClaimTypes.Name, usuario.Nombre),
                new("Apellido", usuario.Apellido),
                new(JwtRegisteredClaimNames.Jti,
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
                        out var minutes)
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

                return Ok(new
                {
                    token = tokenString,
                    tipo = "Bearer",
                    expiraEn = expiration,
                    usuario = new UsuarioRespuestaDto
                    {
                        Id = usuario.Id,
                        Nombre = usuario.Nombre,
                        Apellido = usuario.Apellido,
                        Email = usuario.Email
                    }
                });
        }

        [Authorize(AuthenticationSchemes =
            Microsoft.AspNetCore.Authentication.JwtBearer
                .JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("perfil")]
        public IActionResult Perfil()
        {
            return Ok(new
            {
                mensaje = "Token válido",
                id = User.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier
                )?.Value
                ?? User.FindFirst(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub
                )?.Value,

                email = User.FindFirst(
                    System.Security.Claims.ClaimTypes.Email
                )?.Value
                ?? User.FindFirst(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email
                )?.Value
            });
        }
    }
}
