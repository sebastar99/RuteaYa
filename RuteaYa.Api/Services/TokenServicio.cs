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
        var clave = _configuration["Jwt:Clave"] 
            ?? throw new InvalidOperationException("No se configuro la clave JWT.");

        var emisor = _configuration["Jwt:Emisor"] 
            ?? throw new InvalidOperationException("No se configuro el emisor JWT.");

        var audiencia = _configuration["Jwt:Audiencia"]
            ?? throw new InvalidOperationException("No se configuro la audiencia JWT.");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave));
        var credenciales = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim (ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim (ClaimTypes.Name, usuario.Nombre),
            new Claim (ClaimTypes.Email, usuario.Email)
        };

        var token = new JwtSecurityToken(
            issuer: emisor,
            audience: audiencia,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credenciales
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}