
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RuteaYa.Entidades;

namespace RuteaYa.Api.Services;

public class TokenServicio : ITokenServicio
{
    private readonly IConfiguration _configuration;

    public TokenServicio(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CrearToken(Usuario usuario)
    {
        var key = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Falta Jwt:Key.");

        var issuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Falta Jwt:Issuer.");

        var audience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Falta Jwt:Audience.");

        var expirationMinutes = int.TryParse(
            _configuration["Jwt:ExpirationMinutes"],
            out var minutes) ? minutes : 120;

        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException(
                "La clave JWT debe tener al menos 32 bytes.");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
            new(ClaimTypes.Email, usuario.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(key));

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var expiration = DateTime.UtcNow.AddMinutes(expirationMinutes);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiration,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
