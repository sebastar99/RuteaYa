using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class RutasConsultaServicioTest
{
    private static PuntoRutaDto[] Puntos() => new[] { new PuntoRutaDto { Latitud = -34.6m, Longitud = -58.4m }, new PuntoRutaDto { Latitud = -35, Longitud = -59 } };
    [Fact]
    public async Task ReutilizaPorCoordenadasOrdenadasYNoComparteResultadosMutablesNiEntreSolicitudes()
    {
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.Setup(p => p.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ResultadoRutaDto { DistanciaMetros = 100, DuracionSegundos = 20 });
        var consulta = new RutasConsultaServicio(proveedor.Object);
        var primero = await consulta.CalcularAsync(Puntos()); primero.DistanciaMetros = 999;
        Assert.Equal(100, (await consulta.CalcularAsync(Puntos())).DistanciaMetros);
        await consulta.CalcularAsync(Puntos().Reverse().ToArray());
        proveedor.Verify(p => p.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        await new RutasConsultaServicio(proveedor.Object).CalcularAsync(Puntos());
        proveedor.Verify(p => p.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
    [Fact]
    public async Task ErrorOCancelacion_NoSeConservanComoUnaRutaValida()
    {
        var proveedor = new Mock<IRutasServicio>();
        proveedor.SetupSequence(p => p.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Error simulado")).ReturnsAsync(new ResultadoRutaDto { DistanciaMetros = 100, DuracionSegundos = 20 });
        var consulta = new RutasConsultaServicio(proveedor.Object);
        await Assert.ThrowsAsync<HttpRequestException>(() => consulta.CalcularAsync(Puntos()));
        Assert.Equal(100, (await consulta.CalcularAsync(Puntos())).DistanciaMetros);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consulta.CalcularAsync(Puntos(), cts.Token));
    }
}
