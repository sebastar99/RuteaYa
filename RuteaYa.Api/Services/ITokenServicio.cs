using RuteaYa.Entidades;

namespace RuteaYa.Api.Services;

public interface ITokenServicio
{
    string CrearToken(Usuario usuario);
}