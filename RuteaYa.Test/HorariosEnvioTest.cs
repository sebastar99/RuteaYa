using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class HorariosEnvioTest
{
    private static TimeZoneInfo Argentina() => ZonaHorariaViaje.PorDirecciones("Retiro, Morón, Argentina", "Entrega, Luján, Argentina")!;

    [Fact]
    public void ConvierteConLaZonaResueltaPorElServidor_YNoCompletaHorariosOmitidos()
    {
        var dto = new CrearEnvioDto { FechaLimite = new DateTime(2030, 1, 1) };
        var desconocidos = HorariosEnvio.Convertir(dto, null); Assert.Null(desconocidos.Entrega); Assert.Null(desconocidos.Retiro);
        dto.EntregaLimiteLocal = new DateTime(2030, 1, 1, 18, 30, 0);
        dto.RetiroSinRestriccion = true;
        var horarios = HorariosEnvio.Convertir(dto, Argentina());
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 21, 30, 0, TimeSpan.Zero), horarios.Entrega);
        Assert.Equal(TimeSpan.FromHours(-3), horarios.Entrega!.Value.Offset); Assert.Null(horarios.Retiro);
        dto.RetiroDisponibleDesdeLocal = dto.EntregaLimiteLocal;
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto, Argentina()));
        dto.RetiroSinRestriccion = false; dto.RetiroDisponibleDesdeLocal = dto.EntregaLimiteLocal.Value.AddMinutes(1);
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto, Argentina()));
        dto.RetiroDisponibleDesdeLocal = null; dto.EntregaLimiteLocal = dto.EntregaLimiteLocal.Value.AddDays(1);
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto, Argentina()));
    }

    [Fact]
    public void ConHorariosYSinZonaDeterminable_RechazaSinInventarUnaZona()
    {
        var dto = new CrearEnvioDto { FechaLimite = new DateTime(2030, 1, 1), EntregaLimiteLocal = new DateTime(2030, 1, 1, 18, 30, 0) };
        var error = Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto, null));
        Assert.Contains("zona horaria", error.Message);
        dto.EntregaLimiteLocal = null; dto.RetiroDisponibleDesdeLocal = new DateTime(2030, 1, 1, 8, 0, 0);
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto, null));
    }

    [Theory]
    [InlineData("Retiro, Morón, Argentina", "Entrega, Luján, Argentina", true)]
    [InlineData("Retiro, Morón, Argentina", "Entrega, Montevideo, Uruguay", false)]
    [InlineData("Retiro sin país", "Entrega, Luján, Argentina", false)]
    [InlineData(null, "Entrega, Luján, Argentina", false)]
    public void ZonaDelEnvio_SoloSeResuelveSiTodasLasDireccionesSonArgentinas(string? retiro, string entrega, bool esperada)
    {
        var zona = ZonaHorariaViaje.PorDirecciones(retiro, entrega);
        Assert.Equal(esperada, zona != null);
        if (esperada) Assert.Equal(TimeSpan.FromHours(-3), zona!.GetUtcOffset(new DateTime(2030, 1, 1)));
    }
}
