namespace RuteaYa.Web.Services;

public sealed class ErroresDireccionesFormularioException(IReadOnlyDictionary<string, string> errores)
    : ArgumentException("Revisá las direcciones indicadas en el formulario.")
{
    public IReadOnlyDictionary<string, string> Errores { get; } = errores;
}
