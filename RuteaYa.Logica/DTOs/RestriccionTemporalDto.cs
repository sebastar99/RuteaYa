namespace RuteaYa.Logica.DTOs;

/// <summary>
/// Estado y valor atómicos e inmutables: solo Informada tiene fecha.
/// Una instancia nueva es Desconocida, nunca ausencia implícita de restricción.
/// </summary>
public sealed class RestriccionTemporalDto
{
    public EstadoRestriccionTemporal Estado { get; }
    public DateTimeOffset? Valor { get; }

    public RestriccionTemporalDto()
    {
    }

    private RestriccionTemporalDto(EstadoRestriccionTemporal estado, DateTimeOffset? valor)
    {
        Estado = estado;
        Valor = valor;
    }

    public static RestriccionTemporalDto Desconocida() => new();

    public static RestriccionTemporalDto SinRestriccion() =>
        new(EstadoRestriccionTemporal.SinRestriccion, null);

    public static RestriccionTemporalDto Informada(DateTimeOffset valor)
    {
        if (valor == default)
            throw new ArgumentException(
                "Una restricción informada no puede usar la fecha por defecto; usá Desconocida si falta el dato.",
                nameof(valor));
        return new(EstadoRestriccionTemporal.Informada, valor);
    }
}
