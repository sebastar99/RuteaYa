using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class HorariosEnvioTest
{
    [Fact]
    public void ConvierteSoloConOffsetExplicito_YNoCompletaHorariosOmitidos()
    {
        var dto = new CrearEnvioDto { FechaLimite = new DateTime(2030, 1, 1) };
        var desconocidos = HorariosEnvio.Convertir(dto); Assert.Null(desconocidos.Entrega); Assert.Null(desconocidos.Retiro);
        dto.EntregaLimiteLocal = new DateTime(2030, 1, 1, 18, 30, 0);
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto));
        dto.OffsetUtcMinutos = -180; dto.RetiroSinRestriccion = true;
        var horarios = HorariosEnvio.Convertir(dto);
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 21, 30, 0, TimeSpan.Zero), horarios.Entrega);
        Assert.Equal(TimeSpan.FromHours(-3), horarios.Entrega!.Value.Offset); Assert.Null(horarios.Retiro);
        dto.RetiroDisponibleDesdeLocal = dto.EntregaLimiteLocal;
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto));
        dto.RetiroSinRestriccion = false; dto.RetiroDisponibleDesdeLocal = dto.EntregaLimiteLocal.Value.AddMinutes(1);
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto));
        dto.RetiroDisponibleDesdeLocal = null; dto.EntregaLimiteLocal = dto.EntregaLimiteLocal.Value.AddDays(1);
        Assert.Throws<ArgumentException>(() => HorariosEnvio.Convertir(dto));
    }
}
