using System.Data;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Test.Helpers;
using Xunit;
using Moq;

namespace RuteaYa.Test;

public class EnviosLogicaTest
{
    private static DataTable TablaEnvios(params (int Id, int UsuarioId, int? ConductorId, byte Estado, string Descripcion)[] filas)
    {
        return EnviosContextoPrueba.Envios(filas.Select(f => (f.Id, f.UsuarioId, f.ConductorId,
            (EstadoEnvio)f.Estado, f.Descripcion)).ToArray());
    }

    private static DataTable TablaViajes(int viajeId, int usuarioId, bool activo)
    {
        return EnviosContextoPrueba.Existe(activo);
    }

    [Fact]
    public async Task ListarAsync_UsuarioValido_ConsultaSinTrackingYDevuelveEnvios()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((1, 7, null, (byte)EstadoEnvio.BuscandoConductor, "P1"), (2, 7, null, (byte)EstadoEnvio.Entregado, "P2")));
        await using var db = consultas.CrearContexto();

        var resultado = await new EnviosLogica(db, null!).ListarAsync(7);

        Assert.NotNull(resultado);
        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, e => Assert.Equal(7, e.UsuarioId));
        Assert.Single(consultas.Consultas);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ListarAsync_UsuarioInvalido_LanzaExcepcionSinConsultar(int usuarioId)
    {
        var consultas = new ConsultasEfSimuladas();
        await using var db = consultas.CrearContexto();

        await Assert.ThrowsAsync<ArgumentException>(() => new EnviosLogica(db, null!).ListarAsync(usuarioId));

        Assert.Empty(consultas.Consultas);
    }

    [Fact]
    public async Task ObtenerAsync_EnvioExisteYEsDelUsuario_DevuelveDto()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((5, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Monitor")));
        await using var db = consultas.CrearContexto();

        var resultado = await new EnviosLogica(db, null!).ObtenerAsync(7, 5);

        Assert.NotNull(resultado);
        Assert.Equal("Monitor", resultado.Descripcion);
        Assert.Single(consultas.Consultas);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CrearAsync_SinComprobantes_NoPermiteCrearNiConsultar()
    {
        var consultas = new ConsultasEfSimuladas();
        await using var db = consultas.CrearContexto();

        var dto = new CrearEnvioDto
        {
            OrigenDir = "Edison 3436",
            DestinoDir = "Edison 3436",
            Descripcion = "Test",
            PesoKg = 1,
            Monto = 100,
            FechaLimite = DateTime.Today
        };

        await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => new EnviosLogica(db, null!).CrearAsync(7, dto));

        Assert.Empty(consultas.Consultas);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrearAsync_SinComprobantes_NoGeocodificaAunqueProveedorDevuelvaDatosOErrores(bool errorEnDestino)
    {
        using var contexto = new EnviosContextoPrueba();
        var geo = new Mock<IGeocodificacionServicio>(MockBehavior.Strict);
        geo.Setup(g => g.GeocodificarAsync("Origen", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ResultadoGeocodificacionDto { Posicion = new() { Latitud = -34, Longitud = -58 } } });
        if (errorEnDestino)
            geo.Setup(g => g.GeocodificarAsync("Destino", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Error de HERE simulado"));
        else
            geo.Setup(g => g.GeocodificarAsync("Destino", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ResultadoGeocodificacionDto>());
        var servicio = new EnviosLogica(contexto.Db, geo.Object);
        var dto = new CrearEnvioDto { OrigenDir = "Origen", DestinoDir = "Destino", Descripcion = "Test",
            PesoKg = 1, Monto = 100, FechaLimite = DateTime.Today.AddDays(1) };
        await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.CrearAsync(7, dto));
        geo.VerifyNoOtherCalls();
        Assert.Equal(0, contexto.Guardados);
        Assert.Empty(contexto.Db.ChangeTracker.Entries());
        Assert.Empty(contexto.Consultas.Consultas);
        Assert.Equal(ConnectionState.Closed, contexto.Db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task CancelarAsync_EnvioEnBuscandoConductor_ModificaEstadoYGuarda()
    {
        using var contexto = new EnviosContextoPrueba();
        var consultas = contexto.Consultas;
        consultas.Responder(TablaEnvios((10, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Test")));
        var db = contexto.Db;

        var resultado = await new EnviosLogica(db, null!).CancelarAsync(7, 10);

        Assert.True(resultado);
        Assert.Single(consultas.Consultas);
        Assert.Equal(1, contexto.Guardados);
        Assert.Equal(EstadoEnvio.Cancelado, db.Envios.Local.Single().Estado);
    }

    [Fact]
    public async Task CancelarAsync_EnvioYaEntregado_LanzaExcepcion()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((10, 7, null, (byte)EstadoEnvio.Entregado, "Test")));
        await using var db = consultas.CrearContexto();

        await Assert.ThrowsAsync<ArgumentException>(() => new EnviosLogica(db, null!).CancelarAsync(7, 10));

        Assert.Single(consultas.Consultas);
    }

    [Fact]
    public async Task AceptarAsync_LegadoSinSalida_NoPuedeAsignar()
    {
        using var contexto = new EnviosContextoPrueba();
        var consultas = contexto.Consultas;
        consultas.Responder(TablaViajes(10, 8, true));
        consultas.Responder(TablaEnvios((25, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Test")));
        var db = contexto.Db;

        await Assert.ThrowsAsync<ArgumentException>(() => new EnviosLogica(db, null!).AceptarAsync(conductorId: 8, viajeId: 10, envioId: 25));
        Assert.Empty(consultas.Consultas);
        Assert.Equal(0, contexto.Guardados);
        Assert.Empty(db.Envios.Local);
    }

    [Fact]
    public async Task ActualizarEstadoAsync_NoPermiteAsignarSinCompatibilidad()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((25, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Test")));
        await using var db = consultas.CrearContexto();

        await Assert.ThrowsAsync<ArgumentException>(() => new EnviosLogica(db, null!).ActualizarEstadoAsync(7, 25, EstadoEnvio.ConductorAsignado));
        Assert.Single(consultas.Consultas);
        Assert.Equal(EstadoEnvio.BuscandoConductor, db.Envios.Local.Single().Estado);
        Assert.Null(db.Envios.Local.Single().ConductorId);
    }

    [Fact]
    public async Task ActualizarEstadoConductorAsync_TransicionValida_AvanzaEstado()
    {
        using var contexto = new EnviosContextoPrueba();
        var consultas = contexto.Consultas;
        consultas.Responder(TablaEnvios((25, 7, 8, (byte)EstadoEnvio.ConductorAsignado, "Test")));
        var db = contexto.Db;

        var resultado = await new EnviosLogica(db, null!).ActualizarEstadoConductorAsync(conductorId: 8, envioId: 25, nuevoEstado: EstadoEnvio.Retirado);

        Assert.True(resultado);
        Assert.Single(consultas.Consultas);
        Assert.Equal(1, contexto.Guardados);
        Assert.Equal(EstadoEnvio.Retirado, db.Envios.Local.Single().Estado);
    }

    [Fact]
    public async Task ListarTransportesAsync_DevuelveSoloTransportesDelConductor()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((1, 2, 8, (byte)EstadoEnvio.ConductorAsignado, "T1")));
        await using var db = consultas.CrearContexto();

        var resultado = await new EnviosLogica(db, null!).ListarTransportesAsync(8);

        Assert.NotNull(resultado);
        Assert.Single(resultado);
        Assert.Equal("T1", resultado[0].Descripcion);
        Assert.Single(consultas.Consultas);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
