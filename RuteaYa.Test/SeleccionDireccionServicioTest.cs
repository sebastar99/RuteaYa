using Microsoft.Extensions.Caching.Memory;
using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class SeleccionDireccionServicioTest
{
    [Fact]
    public async Task SeleccionYConfirmacion_SeVerificanEnServidorYConservanPositionAccessEId()
    {
        using var f = new Fixture();
        var sugerencia = Assert.Single(await f.Servicio.SugerirAsync(7, "origen", "calle"));
        var seleccion = await f.Servicio.ResolverAsync(7, "origen", sugerencia.Token);
        Assert.False(seleccion.Confirmada);
        Assert.Null(seleccion.Acceso);
        Assert.Null(seleccion.Navegacion);
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ObtenerConfirmada(7, "origen", seleccion.Token));
        var confirmada = f.Servicio.Confirmar(7, "origen", seleccion.Token, 1);
        Assert.True(confirmada.Confirmada);
        Assert.Equal("here:af:prueba", confirmada.Ubicacion.IdHere);
        Assert.Equal("Calle normalizada", confirmada.Ubicacion.DireccionNormalizada);
        Assert.Equal(-34, confirmada.Ubicacion.Posicion.Latitud);
        Assert.Equal(-36, confirmada.Navegacion!.Latitud);
        confirmada.Ubicacion.Posicion.Latitud = 90; // El consumidor no puede modificar el estado almacenado.
        Assert.Equal(-34, f.Servicio.ObtenerConfirmada(7, "origen", confirmada.Token).Ubicacion.Posicion.Latitud);
        f.Busqueda.Verify(b => b.ResolverAsync("here:af:prueba", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-2)]
    [InlineData(2)]
    public async Task AccesoNoElegidoOManipulado_NoConfirma(int? acceso)
    {
        using var f = new Fixture();
        var s = Assert.Single(await f.Servicio.SugerirAsync(7, "origen", "calle"));
        var r = await f.Servicio.ResolverAsync(7, "origen", s.Token);
        Assert.Throws<ArgumentException>(() => f.Servicio.Confirmar(7, "origen", r.Token, acceso));
    }

    [Fact]
    public async Task Position_SoloSeUsaConEleccionExplicita()
    {
        using var f = new Fixture();
        var s = Assert.Single(await f.Servicio.SugerirAsync(7, "origen", "calle"));
        var r = await f.Servicio.ResolverAsync(7, "origen", s.Token);
        Assert.Equal(-34, f.Servicio.Confirmar(7, "origen", r.Token, -1).Navegacion!.Latitud);
    }

    [Fact]
    public async Task TokensNoSePuedenCruzarEntreUsuarioCampoOEtapa()
    {
        using var f = new Fixture();
        var s = Assert.Single(await f.Servicio.SugerirAsync(7, "origen", "calle"));
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ResolverAsync(8, "origen", s.Token));
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ResolverAsync(7, "destino", s.Token));
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => f.Servicio.Confirmar(7, "origen", s.Token, -1));
        var r = await f.Servicio.ResolverAsync(7, "origen", s.Token);
        var c = f.Servicio.Confirmar(7, "origen", r.Token, -1);
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ObtenerConfirmada(8, "origen", c.Token));
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ObtenerConfirmada(7, "retiro", c.Token));
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ResolverAsync(7, "origen", "here:af:id-enviado-libremente"));
    }

    [Fact]
    public async Task Vencimiento_NoSeRenuevaPorResolverOConfirmar()
    {
        using var f = new Fixture();
        var s = Assert.Single(await f.Servicio.SugerirAsync(7, "origen", "calle"));
        f.Reloj.Ahora = f.Reloj.Ahora.AddMinutes(9);
        var r = await f.Servicio.ResolverAsync(7, "origen", s.Token);
        var c = f.Servicio.Confirmar(7, "origen", r.Token, -1);
        f.Reloj.Ahora = f.Reloj.Ahora.AddMinutes(2);
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ObtenerConfirmada(7, "origen", c.Token));
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => f.Servicio.Confirmar(7, "origen", r.Token, -1));
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ResolverAsync(7, "origen", s.Token));
    }

    [Fact]
    public async Task LookupInconsistente_NoEmiteSeleccion()
    {
        using var f = new Fixture();
        f.Busqueda.Setup(b => b.ResolverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoGeocodificacionDto { IdHere = "here:otro" });
        var s = Assert.Single(await f.Servicio.SugerirAsync(7, "origen", "calle"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Servicio.ResolverAsync(7, "origen", s.Token));
    }

    [Fact]
    public async Task CancelacionPrevia_NoConsultaProveedor()
    {
        using var f = new Fixture();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Servicio.SugerirAsync(7, "origen", "calle", cts.Token));
        f.Busqueda.VerifyNoOtherCalls();
    }

    private sealed class RelojPrueba : TimeProvider { public DateTimeOffset Ahora = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Ahora; }
    private sealed class Fixture : IDisposable
    {
        public Mock<IBusquedaDireccionesServicio> Busqueda = new(MockBehavior.Strict);
        public RelojPrueba Reloj = new();
        private MemoryCache _cache = new(new MemoryCacheOptions());
        public SeleccionDireccionServicio Servicio;
        public Fixture()
        {
            Busqueda.Setup(b => b.SugerirAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new SugerenciaDireccionDto { IdHere = "here:af:prueba", Nombre = "Calle" } });
            Busqueda.Setup(b => b.ResolverAsync("here:af:prueba", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultadoGeocodificacionDto { IdHere = "here:af:prueba", DireccionNormalizada = "Calle normalizada",
                    Posicion = new() { Latitud = -34, Longitud = -58 }, Accesos = new() {
                        new() { Latitud = -35, Longitud = -59 }, new() { Latitud = -36, Longitud = -60 } } });
            Servicio = new(Busqueda.Object, _cache, Reloj);
        }
        public void Dispose() => _cache.Dispose();
    }
}
