using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class UxDireccionesYHorariosTest
{
    [Theory]
    [InlineData("houseNumber", false, true)]
    [InlineData("place", false, true)]
    [InlineData("street", false, false)]
    [InlineData("locality", false, false)]
    [InlineData("houseNumber", true, true)]
    public async Task SeleccionAutomatica_ExigePrecision_YUsaAccesoMasCercanoSinRecibirCoordenadas(string tipo, bool estimada, bool acepta)
    {
        var u = new ResultadoGeocodificacionDto { IdHere = "here:af:1", DireccionNormalizada = "Calle 10, Argentina", TipoResultado = tipo, PosicionEstimada = estimada,
            Posicion = new() { Latitud = -34, Longitud = -58 }, Accesos = new() { new() { Latitud = 91, Longitud = 0 }, new() { Latitud = -34.01m, Longitud = -58 }, new() { Latitud = -34.001m, Longitud = -58 } } };
        var b = new Mock<IBusquedaDireccionesServicio>();
        b.Setup(b => b.SugerirAsync("calle", default)).ReturnsAsync(new[] { new SugerenciaDireccionDto { IdHere = u.IdHere } });
        b.Setup(b => b.ResolverAsync(u.IdHere, default)).ReturnsAsync(u);
        using var cache = new MemoryCache(new MemoryCacheOptions()); var s = new SeleccionDireccionServicio(b.Object, cache);
        var sugerida = (await s.SugerirAsync(7, "campo", "calle")).Single(); var resuelta = await s.ResolverAsync(7, "campo", sugerida.Token);
        if (!acepta) { Assert.Throws<ArgumentException>(() => s.ConfirmarAutomaticamente(7, "campo", resuelta.Token)); return; }
        var confirmada = s.ConfirmarAutomaticamente(7, "campo", resuelta.Token);
        Assert.True(confirmada.Confirmada); Assert.Equal(2, confirmada.Acceso); Assert.Equal(-34.001m, confirmada.Navegacion!.Latitud);
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => s.ConfirmarAutomaticamente(8, "campo", resuelta.Token));
        u.Accesos.Clear();
        var otra = await s.ResolverAsync(7, "campo", sugerida.Token);
        var sinAcceso = s.ConfirmarAutomaticamente(7, "campo", otra.Token);
        Assert.Equal(-1, sinAcceso.Acceso); Assert.Equal(-34, sinAcceso.Navegacion!.Latitud);
    }

    [Fact]
    public void ArgentinaDesdeSnapshot_NoConfiaEnZonaNiOffsetDelNavegador()
    {
        var v = new Viaje { Id = 10, UsuarioId = 7, Origen = "Calle 10, Morón, Argentina", OrigenUbicacion = new() {
            UsuarioId = 7, TipoRecurso = TipoRecursoUbicacion.Viaje, Campo = CampoUbicacion.OrigenViaje,
            ConfirmadaEnUtc = DateTimeOffset.UtcNow.AddMinutes(-1), ConsumidaEnUtc = DateTimeOffset.UtcNow,
            VenceEnUtc = DateTimeOffset.UtcNow.AddMinutes(5), IdHere = "here:af:1", DireccionNormalizada = "Calle 10, Morón, Argentina" } };
        var zona = ZonaHorariaViaje.Resolver(v, "America/New_York");
        Assert.Equal(ZonaHorariaViaje.Argentina, zona.Id);
        Assert.Equal(new DateTimeOffset(2030, 1, 7, 8, 0, 0, TimeSpan.FromHours(-3)), ZonaHorariaViaje.Convertir(new DateTime(2030, 1, 7, 8, 0, 0), zona));
        v.OrigenUbicacion.UsuarioId = 8;
        Assert.Throws<ArgumentException>(() => ZonaHorariaViaje.Resolver(v, null));
        Assert.NotNull(ZonaHorariaViaje.Resolver(v, "Europe/Madrid"));
    }

    [Fact]
    public void ZonaConCambioDeHora_ConvierteCadaFecha_YRechazaAmbiguedades()
    {
        var z = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Equal(TimeSpan.FromHours(-5), ZonaHorariaViaje.Convertir(new DateTime(2030, 1, 7, 8, 0, 0), z).Offset);
        Assert.Equal(TimeSpan.FromHours(-4), ZonaHorariaViaje.Convertir(new DateTime(2030, 7, 1, 8, 0, 0), z).Offset);
        Assert.Throws<ArgumentException>(() => ZonaHorariaViaje.Convertir(new DateTime(2030, 3, 10, 2, 30, 0), z));
        Assert.Throws<ArgumentException>(() => ZonaHorariaViaje.Convertir(new DateTime(2030, 11, 3, 1, 30, 0), z));
    }

    [Fact]
    public async Task SalidaGuardada_ConservaOffset_YNoReinterpretaConZonaNueva()
    {
        using var f = new CompatibilidadPersistidaTest.Fixture(false); await using var db = f.Db();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2)); while (fecha.DayOfWeek != DayOfWeek.Monday) fecha = fecha.AddDays(1);
        var s = new SalidasViajeServicio(db);
        var id = (await s.SeleccionarAsync(7, new() { ViajeId = f.ViajeId, Fecha = fecha, ZonaHorariaId = "Europe/Madrid", LlegadaSinRestriccion = true }))!.Value;
        var original = (await db.SalidasViaje.AsNoTracking().SingleAsync()).FechaHora;
        Assert.Equal(id, await s.SeleccionarAsync(7, new() { ViajeId = f.ViajeId, Fecha = fecha, ZonaHorariaId = ZonaHorariaViaje.Argentina, OffsetUtcMinutos = -999 }));
        var actual = (await db.SalidasViaje.AsNoTracking().SingleAsync()).FechaHora;
        Assert.Equal(original, actual); Assert.Equal(original.Offset, actual.Offset);
    }
}
