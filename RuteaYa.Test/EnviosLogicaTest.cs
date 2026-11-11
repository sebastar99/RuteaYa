using System.Data;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Test.Helpers;
using Xunit;

namespace RuteaYa.Test;

public class EnviosLogicaTest
{
    private static DataTable TablaEnvios(params (int Id, int UsuarioId, int? ConductorId, byte Estado, string Descripcion)[] filas)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Id", typeof(int));
        tabla.Columns.Add("UsuarioId", typeof(int));
        tabla.Columns.Add("ConductorId", typeof(int));
        tabla.Columns.Add("OrigenDir", typeof(string));
        tabla.Columns.Add("OrigenLat", typeof(decimal));
        tabla.Columns.Add("OrigenLng", typeof(decimal));
        tabla.Columns.Add("DestinoDir", typeof(string));
        tabla.Columns.Add("DestinoLat", typeof(decimal));
        tabla.Columns.Add("DestinoLng", typeof(decimal));
        tabla.Columns.Add("FechaLimite", typeof(DateTime));
        tabla.Columns.Add("PesoKg", typeof(decimal));
        tabla.Columns.Add("Tamano", typeof(byte));
        tabla.Columns.Add("Descripcion", typeof(string));
        tabla.Columns.Add("Monto", typeof(decimal));
        tabla.Columns.Add("Estado", typeof(byte));
        tabla.Columns.Add("CreadoEn", typeof(DateTime));

        foreach (var f in filas)
        {
            tabla.Rows.Add(
                f.Id, f.UsuarioId, f.ConductorId.HasValue ? (object)f.ConductorId.Value : DBNull.Value,
                "Edison 3436", -34.6037m, -58.3816m, "Illia 2849", -34.6000m, -58.3800m,
                new DateTime(2026, 11, 20), 2.5m, (byte)TamanoEnvio.Mediano, f.Descripcion, 4000m, f.Estado, DateTime.Now
            );
        }
        return tabla;
    }

    private static DataTable TablaViajes(int viajeId, int usuarioId, bool activo)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Id", typeof(int));
        tabla.Columns.Add("UsuarioId", typeof(int));
        tabla.Columns.Add("Origen", typeof(string));
        tabla.Columns.Add("Destino", typeof(string));
        tabla.Columns.Add("Activo", typeof(bool));
        tabla.Rows.Add(viajeId, usuarioId, "Morón", "Luján", activo);
        return tabla;
    }

    private static DataTable TablaResultadoEscritura(int filasAfectadas = 1)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("FilasAfectadas", typeof(int));
        tabla.Rows.Add(filasAfectadas);
        return tabla;
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
    public async Task CrearAsync_OrigenYDestinoIguales_LanzaExcepcionSinConsultar()
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

        await Assert.ThrowsAsync<ArgumentException>(() => new EnviosLogica(db, null!).CrearAsync(7, dto));

        Assert.Empty(consultas.Consultas);
    }

    [Fact]
    public async Task CancelarAsync_EnvioEnBuscandoConductor_ModificaEstadoYGuarda()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((10, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Test")));
        consultas.Responder(TablaResultadoEscritura(1));
        await using var db = consultas.CrearContexto();

        var resultado = await new EnviosLogica(db, null!).CancelarAsync(7, 10);

        Assert.True(resultado);
        Assert.Equal(2, consultas.Consultas.Count);
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
    public async Task AceptarAsync_ConductorValidoYViajeActivo_AsignaConductorYEstado()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaViajes(10, 8, true));
        consultas.Responder(TablaEnvios((25, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Test")));
        consultas.Responder(TablaResultadoEscritura(1));
        await using var db = consultas.CrearContexto();

        var resultado = await new EnviosLogica(db, null!).AceptarAsync(conductorId: 8, viajeId: 10, envioId: 25);

        Assert.True(resultado);
        Assert.Equal(3, consultas.Consultas.Count);
    }

    [Fact]
    public async Task AceptarAsync_IntentarTransportarEnvioPropio_LanzaExcepcion()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaViajes(10, 7, true));
        consultas.Responder(TablaEnvios((25, 7, null, (byte)EstadoEnvio.BuscandoConductor, "Test")));
        await using var db = consultas.CrearContexto();

        await Assert.ThrowsAsync<ArgumentException>(() => new EnviosLogica(db, null!).AceptarAsync(conductorId: 7, viajeId: 10, envioId: 25));

        Assert.Equal(2, consultas.Consultas.Count);
    }

    [Fact]
    public async Task ActualizarEstadoConductorAsync_TransicionValida_AvanzaEstado()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(TablaEnvios((25, 7, 8, (byte)EstadoEnvio.ConductorAsignado, "Test")));
        consultas.Responder(TablaResultadoEscritura(1));
        await using var db = consultas.CrearContexto();

        var resultado = await new EnviosLogica(db, null!).ActualizarEstadoConductorAsync(conductorId: 8, envioId: 25, nuevoEstado: EstadoEnvio.Retirado);

        Assert.True(resultado);
        Assert.Equal(2, consultas.Consultas.Count);
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