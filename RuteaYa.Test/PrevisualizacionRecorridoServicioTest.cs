using Microsoft.Extensions.Caching.Memory;
using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class PrevisualizacionRecorridoServicioTest
{
    [Fact]
    public async Task ViajeAjeno_NoGeocodificaNiCalcula()
    {
        using var f = new Fixture();
        Assert.Null(await f.Servicio.BuscarAsync(8, 10));
        Assert.Null(await f.Servicio.ConfirmarAsync(8, 10, new()));
        f.Geo.VerifyNoOtherCalls();
        f.Rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Buscar_RespetaOrdenDeParadas_NoConfirmaNiLlamaRutas()
    {
        using var f = new Fixture();
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        Assert.Equal(new[] { "Origen", "Primera", "Segunda", "Destino" }, modelo!.Ubicaciones.Select(u => u.Direccion));
        Assert.All(modelo.Ubicaciones, u => Assert.True(Assert.Single(u.Candidatos).RequiereConfirmacion));
        Assert.Empty(modelo.PuntosConfirmados);
        Assert.Null(modelo.Ruta);
        f.Rutas.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(null, -1)]
    [InlineData(1, -1)]
    [InlineData(0, -2)]
    [InlineData(0, 1)]
    public async Task Confirmar_SeleccionAusenteOManipulada_NoCalcula(int? candidato, int? acceso)
    {
        using var f = new Fixture();
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        var form = Formulario(modelo!.Sesion!);
        form.Selecciones[0] = new() { Candidato = candidato, Acceso = acceso };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Servicio.ConfirmarAsync(7, 10, form));
        f.Rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Confirmar_SeleccionCompleta_UsaAccessSoloDondeSeEligioYConservaPosition()
    {
        using var f = new Fixture();
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        var form = Formulario(modelo!.Sesion!);
        form.Selecciones[1].Acceso = 0;
        IReadOnlyList<PuntoRutaDto>? recibidos = null;
        f.Rutas.Setup(r => r.CalcularGeometriaAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<PuntoRutaDto>, CancellationToken>((p, _) => recibidos = p)
            .ReturnsAsync(new ResultadoRutaGeometriaDto { Totales = new() { DistanciaMetros = 12345, DuracionSegundos = 678 } });
        var resultado = await f.Servicio.ConfirmarAsync(7, 10, form);
        Assert.Equal(new[] { 1m, 12m, 3m, 4m }, recibidos!.Select(p => p.Latitud));
        Assert.Equal(2m, resultado!.CandidatosConfirmados[1].Posicion.Latitud);
        Assert.Equal(12m, resultado.PuntosConfirmados[1].Latitud);
        var recargado = await f.Servicio.ObtenerAsync(7, 10, modelo.Sesion);
        Assert.Equal(12345, recargado!.Ruta!.Totales.DistanciaMetros);
        // Cambiar el modelo del consumidor no cambia las confirmaciones en caché.
        resultado.PuntosConfirmados.Clear();
        Assert.Equal(4, (await f.Servicio.ObtenerAsync(7, 10, modelo.Sesion))!.PuntosConfirmados.Count);
    }

    [Fact]
    public async Task TokenDeOtroUsuarioOViaje_NoPuedeReutilizarse()
    {
        using var f = new Fixture();
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        f.Viajes.Setup(v => v.ObtenerPorIdAsync(8, 10)).ReturnsAsync(new ViajeDto { Id = 10, UsuarioId = 8 });
        f.Viajes.Setup(v => v.ObtenerPorIdAsync(7, 11)).ReturnsAsync(new ViajeDto { Id = 11, UsuarioId = 7 });
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ConfirmarAsync(8, 10, Formulario(modelo!.Sesion!)));
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ConfirmarAsync(7, 11, Formulario(modelo!.Sesion!)));
        f.Rutas.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SesionVencidaOViajeEditado_InvalidaConfirmacion(bool editar)
    {
        using var f = new Fixture();
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        if (editar) f.Viaje.Paradas[0].Ubicacion = "Dirección cambiada";
        else f.Reloj.Ahora = f.Reloj.Ahora.AddMinutes(11);
        await Assert.ThrowsAsync<SeleccionTemporalNoDisponibleException>(() => f.Servicio.ConfirmarAsync(7, 10, Formulario(modelo!.Sesion!)));
        f.Rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SinCandidatos_NoSePuedeConfirmar()
    {
        using var f = new Fixture();
        f.Geo.Setup(g => g.GeocodificarAsync("Origen", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ResultadoGeocodificacionDto>());
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Servicio.ConfirmarAsync(7, 10, Formulario(modelo!.Sesion!)));
        f.Rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HereFalla_NoGuardaRutaNiConfirmacionesParciales()
    {
        using var f = new Fixture();
        var modelo = await f.Servicio.BuscarAsync(7, 10);
        f.Rutas.Setup(r => r.CalcularGeometriaAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Error simulado"));
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Servicio.ConfirmarAsync(7, 10, Formulario(modelo!.Sesion!)));
        var recargado = await f.Servicio.ObtenerAsync(7, 10, modelo!.Sesion);
        Assert.Null(recargado!.Ruta);
        Assert.Empty(recargado.PuntosConfirmados);
    }

    private static ConfirmarRecorridoFormulario Formulario(string sesion) => new()
    {
        Sesion = sesion,
        Selecciones = Enumerable.Range(0, 4).Select(_ => new SeleccionUbicacion { Candidato = 0, Acceso = -1 }).ToList()
    };

    private sealed class RelojPrueba : TimeProvider
    {
        public DateTimeOffset Ahora = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Mock<IViajesLogica> Viajes = new();
        public readonly Mock<IGeocodificacionServicio> Geo = new(MockBehavior.Strict);
        public readonly Mock<IRutasGeometriaServicio> Rutas = new(MockBehavior.Strict);
        public readonly MemoryCache Cache = new(new MemoryCacheOptions());
        public readonly RelojPrueba Reloj = new();
        public readonly ViajeDto Viaje = new() { Id = 10, UsuarioId = 7, Origen = "Origen", Destino = "Destino",
            Paradas = new() { new() { Id = 2, Orden = 2, Ubicacion = "Segunda" }, new() { Id = 1, Orden = 1, Ubicacion = "Primera" } } };
        public readonly PrevisualizacionRecorridoServicio Servicio;
        public Fixture()
        {
            Viajes.Setup(v => v.ObtenerPorIdAsync(7, 10)).ReturnsAsync(Viaje);
            var direcciones = new[] { "Origen", "Primera", "Segunda", "Destino" };
            for (var i = 0; i < direcciones.Length; i++)
            {
                var n = i + 1;
                Geo.Setup(g => g.GeocodificarAsync(direcciones[i], It.IsAny<CancellationToken>())).ReturnsAsync(new[]
                {
                    new ResultadoGeocodificacionDto { DireccionNormalizada = direcciones[i],
                        Posicion = new() { Latitud = n, Longitud = n }, Accesos = new() { new() { Latitud = n + 10, Longitud = n + 10 } } }
                });
            }
            Servicio = new(Viajes.Object, Geo.Object, Rutas.Object, Cache, Reloj);
        }
        public void Dispose() => Cache.Dispose();
    }
}
