namespace RuteaYa.Logica;

public sealed class ComprobanteUbicacionException(string codigo, string mensaje) : InvalidOperationException(mensaje)
{
    public string Codigo { get; } = codigo;
}
