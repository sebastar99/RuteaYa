
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Moq;
using RuteaYa.Api.Controllers;
using RuteaYa.Api.DTOs.Usuarios;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using Xunit;

namespace RuteaYa.Test.Api
{
    public class UsuariosControllerJwtTest
    {
        private const string Issuer = "RuteaYa.Tests";
        private const string Audience = "RuteaYa.Tests.Client";
        private const string Key =
            "RuteaYa.Tests.Signing.Key.Must.Be.At.Least.32.Bytes!";

        private static IConfiguration CrearConfiguracion(
            int expirationMinutes = 120)
        {
            var valores = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:Key"] = Key,
                ["Jwt:ExpirationMinutes"] =
                    expirationMinutes.ToString()
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(valores)
                .Build();
        }

        [Fact]
        public async Task Login_CredencialesIncorrectas_RetornaUnauthorized()
        {
            var logica = new Mock<IUsuariosLogica>();

            logica
                .Setup(x => x.ValidarCredencialesAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync((Usuario?)null);

            var controller = new UsuariosController(
                logica.Object,
                CrearConfiguracion());

            var dto = new LoginUsuarioDto
            {
                Email = "invalido@example.com",
                Password = "incorrecta"
            };

            var resultado = await controller.Login(dto);

            Assert.IsType<UnauthorizedObjectResult>(resultado);
        }

        [Fact]
        public async Task Login_CredencialesCorrectas_DevuelveJwtValido()
        {
            var usuario = new Usuario
            {
                Id = 1,
                Nombre = "Usuario",
                Apellido = "Prueba",
                Email = "usuario@example.com"
            };

            var logica = new Mock<IUsuariosLogica>();

            logica
                .Setup(x => x.ValidarCredencialesAsync(
                    usuario.Email,
                    "PasswordPrueba123!"))
                .ReturnsAsync(usuario);

            var controller = new UsuariosController(
                logica.Object,
                CrearConfiguracion());

            var dto = new LoginUsuarioDto
            {
                Email = usuario.Email,
                Password = "PasswordPrueba123!"
            };

            var antesDelLogin = DateTime.UtcNow;

            var resultado = await controller.Login(dto);

            var respuesta = Assert.IsType<OkObjectResult>(resultado);
            Assert.NotNull(respuesta.Value);

            var tipoRespuesta = respuesta.Value!.GetType();

            var token = tipoRespuesta
                .GetProperty("token")!
                .GetValue(respuesta.Value) as string;

            var tipo = tipoRespuesta
                .GetProperty("tipo")!
                .GetValue(respuesta.Value) as string;

            var expiraEn = (DateTime)tipoRespuesta
                .GetProperty("expiraEn")!
                .GetValue(respuesta.Value)!;

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.Equal("Bearer", tipo);

            Assert.InRange(
                expiraEn,
                antesDelLogin.AddMinutes(119),
                DateTime.UtcNow.AddMinutes(121));

            var handler = new JwtSecurityTokenHandler();

            var parametros = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = handler.ValidateToken(
                token!,
                parametros,
                out var tokenValidado);

            Assert.Equal(
                usuario.Id.ToString(),
                principal.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst(
                    JwtRegisteredClaimNames.Sub)?.Value);
        }


        [Fact]
        public void Jwt_Expirado_LanzaExcepcionDeExpiracion()
        {
            var token = CrearJwtDePrueba(
                issuer: Issuer,
                audience: Audience,
                key: Key,
                expiration: DateTime.UtcNow.AddMinutes(-5));

            var excepcion = Assert.Throws<SecurityTokenExpiredException>(
                () => ValidarJwt(token, Issuer, Audience, Key));

            Assert.NotNull(excepcion);
        }

        [Fact]
        public void Jwt_FirmadoConOtraClave_EsRechazado()
        {
            var token = CrearJwtDePrueba(
                issuer: Issuer,
                audience: Audience,
                key: Key,
                expiration: DateTime.UtcNow.AddMinutes(30));

            var otraClave =
                "Otra.Clave.De.Prueba.Diferente.De.32.Bytes.Minimo!";

            var excepcion = Record.Exception(
                () => ValidarJwt(token, Issuer, Audience, otraClave));

            Assert.NotNull(excepcion);
            Assert.IsAssignableFrom<SecurityTokenInvalidSignatureException>(
                excepcion);
        }

        [Fact]
        public void Jwt_ConIssuerIncorrecto_EsRechazado()
        {
            var token = CrearJwtDePrueba(
                issuer: "OtroIssuer",
                audience: Audience,
                key: Key,
                expiration: DateTime.UtcNow.AddMinutes(30));

            Assert.Throws<SecurityTokenInvalidIssuerException>(
                () => ValidarJwt(token, Issuer, Audience, Key));
        }

        [Fact]
        public void Jwt_ConAudienceIncorrecta_EsRechazado()
        {
            var token = CrearJwtDePrueba(
                issuer: Issuer,
                audience: "OtraAplicacion",
                key: Key,
                expiration: DateTime.UtcNow.AddMinutes(30));

            Assert.Throws<SecurityTokenInvalidAudienceException>(
                () => ValidarJwt(token, Issuer, Audience, Key));
        }

        [Fact]
        public void Jwt_Manipulado_EsRechazado()
        {
            var token = CrearJwtDePrueba(
                issuer: Issuer,
                audience: Audience,
                key: Key,
                expiration: DateTime.UtcNow.AddMinutes(30));

            var partes = token.Split('.');
            Assert.Equal(3, partes.Length);

            var firma = partes[2];
            partes[2] = (firma[0] == 'A' ? "B" : "A")
                + firma.Substring(1);

            var tokenManipulado = string.Join(".", partes);

            var excepcion = Record.Exception(
                () => ValidarJwt(tokenManipulado, Issuer, Audience, Key));

            Assert.NotNull(excepcion);
            Assert.IsAssignableFrom<SecurityTokenException>(excepcion);
        }


        private static string CrearJwtDePrueba(
            string issuer,
            string audience,
            string key,
            DateTime expiration)
        {
            var signingKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(key));

            var credentials = new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: new[]
                {
            new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Sub, "1"),
            new System.Security.Claims.Claim(
                JwtRegisteredClaimNames.Email, "usuario@example.com")
                },
                expires: expiration,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static System.Security.Claims.ClaimsPrincipal ValidarJwt(
            string token,
            string issuer,
            string audience,
            string key)
        {
            var handler = new JwtSecurityTokenHandler();

            var parametros = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,

                ValidateAudience = true,
                ValidAudience = audience,

                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key)),

                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            return handler.ValidateToken(
                token,
                parametros,
                out _);
        }

    }

}
