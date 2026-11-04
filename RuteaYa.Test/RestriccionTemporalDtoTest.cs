using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class RestriccionTemporalDtoTest
{
    [Fact]
    public void ConstruccionSinDatos_EsDesconocidaSinFecha()
    {
        var restriccion = new RestriccionTemporalDto();

        Assert.Equal(EstadoRestriccionTemporal.Desconocida, restriccion.Estado);
        Assert.Equal(EstadoRestriccionTemporal.Desconocida, default(EstadoRestriccionTemporal));
        Assert.Null(restriccion.Valor);
        Assert.Null(RestriccionTemporalDto.Desconocida().Valor);
    }

    [Fact]
    public void SinRestriccion_NoInventaUnaFecha()
    {
        var restriccion = RestriccionTemporalDto.SinRestriccion();

        Assert.Equal(EstadoRestriccionTemporal.SinRestriccion, restriccion.Estado);
        Assert.Null(restriccion.Valor);
    }

    [Fact]
    public void Informada_ConservaFechaYOffset()
    {
        var fecha = new DateTimeOffset(2026, 11, 2, 10, 30, 0, TimeSpan.FromHours(2));

        var restriccion = RestriccionTemporalDto.Informada(fecha);

        Assert.Equal(EstadoRestriccionTemporal.Informada, restriccion.Estado);
        Assert.Equal(fecha, restriccion.Valor);
        Assert.Equal(fecha.Offset, restriccion.Valor!.Value.Offset);
    }

    [Fact]
    public void Informada_FechaPorDefecto_SeRechazaEnLugarDeInventarValorObligatorio()
    {
        Assert.Throws<ArgumentException>(() => RestriccionTemporalDto.Informada(default));
    }
}
