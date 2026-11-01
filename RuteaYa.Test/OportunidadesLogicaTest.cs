using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Test.Helpers;

namespace RuteaYa.Test;

public class OportunidadesLogicaTest
{
    private static DataTable Viajes(bool incluirViaje = true)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Id", typeof(int));
        tabla.Columns.Add("Origen", typeof(string));
        tabla.Columns.Add("Destino", typeof(string));
        tabla.Columns.Add("Activo", typeof(bool));
        if (incluirViaje) tabla.Rows.Add(10, "Morón", "Luján", true);
        return tabla;
    }

    private static DataTable Cantidad(int total)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Total", typeof(int));
        tabla.Rows.Add(total);
        return tabla;
    }

    private static DataTable Envios(params (int Id, int UsuarioId)[] filas)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("Id", typeof(int));
        tabla.Columns.Add("UsuarioId", typeof(int));
        tabla.Columns.Add("OrigenDir", typeof(string));
        tabla.Columns.Add("DestinoDir", typeof(string));
        tabla.Columns.Add("FechaLimite", typeof(DateTime));
        tabla.Columns.Add("PesoKg", typeof(decimal));
        tabla.Columns.Add("Tamano", typeof(byte));
        tabla.Columns.Add("Descripcion", typeof(string));
        tabla.Columns.Add("Monto", typeof(decimal));
        tabla.Columns.Add("Estado", typeof(byte));
        foreach (var fila in filas)
            tabla.Rows.Add(fila.Id, fila.UsuarioId, "Retiro publicado", "Entrega publicada",
                new DateTime(2026, 11, 15), 2.5m, (byte)TamanoEnvio.Mediano,
                "Paquete publicado", 4000m, (byte)EstadoEnvio.BuscandoConductor);
        return tabla;
    }

    [Fact]
    public async Task ConsultarAsync_ViajePropio_ListadoGlobalPorEstadoSinFiltrarPorDueno()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes());
        consultas.Responder(Cantidad(2));
        consultas.Responder(Envios((25, 99), (24, 7)));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ConsultarAsync(7, 10);

        Assert.NotNull(resultado);
        Assert.Equal(10, resultado.ViajeSeleccionado!.Id);
        Assert.Equal(new[] { 99, 7 }, resultado.Candidatos.Select(e => e.UsuarioId));
        Assert.All(resultado.Candidatos, e => Assert.Equal(EstadoEnvio.BuscandoConductor, e.Estado));
        Assert.Equal(2, resultado.TotalCandidatos);
        Assert.True(resultado.EvaluacionPendiente);
        Assert.Equal(3, consultas.Consultas.Count);
        var viajesSql = consultas.Consultas[0];
        Assert.Matches(@"\[UsuarioId\]\s*=\s*@", viajesSql.Sql);
        Assert.Contains(viajesSql.Parametros.Values, valor => Equals(valor, 7));
        foreach (var consulta in consultas.Consultas.Skip(1))
        {
            Assert.Matches(@"\[Estado\]\s*=\s*(?:CAST\(\s*)?0\b", consulta.Sql);
            Assert.DoesNotMatch(@"\[UsuarioId\]\s*=", consulta.Sql);
        }
        Assert.Equal(2.5m, resultado.Candidatos[0].PesoKg);
        Assert.Equal(4000m, resultado.Candidatos[0].Monto);
        Assert.Equal("Retiro publicado", resultado.Candidatos[0].OrigenDir);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(99, true)]
    [InlineData(999, false)]
    public async Task ConsultarAsync_ViajeAjenoOInexistente_MismoResultadoSinConsultarEnvios(int viajeId, bool tieneViajes)
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes(tieneViajes));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ConsultarAsync(7, viajeId);

        Assert.Null(resultado);
        Assert.Single(consultas.Consultas);
        Assert.DoesNotContain("[Envios]", consultas.Consultas[0].Sql);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConsultarAsync_SinSeleccion_SoloConsultaViajesPropios(bool tieneViajes)
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes(tieneViajes));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ConsultarAsync(7);

        Assert.NotNull(resultado);
        Assert.Null(resultado.ViajeSeleccionado);
        Assert.Equal(tieneViajes ? 1 : 0, resultado.Viajes.Count);
        Assert.Empty(resultado.Candidatos);
        Assert.Single(consultas.Consultas);
    }

    [Fact]
    public async Task ConsultarAsync_SinEnvios_DevuelvePaginaVaciaSinConsultaAdicional()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes());
        consultas.Responder(Cantidad(0));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ConsultarAsync(7, 10, 99);

        Assert.NotNull(resultado);
        Assert.Empty(resultado.Candidatos);
        Assert.Equal(0, resultado.TotalPaginas);
        Assert.Equal(1, resultado.Pagina);
        Assert.Equal(2, consultas.Consultas.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(2, 2, 5, 2, 2)]
    [InlineData(3, 2, 5, 3, 4)]
    [InlineData(int.MaxValue, 2, 5, 3, 4)]
    [InlineData(1, 50, 5, 1, 0)]
    public async Task ConsultarAsync_PaginacionOrdenada_AcotaPaginaYUsaSkipTake(
        int solicitada, int tamano, int total, int paginaEsperada, int saltoEsperado)
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes());
        consultas.Responder(Cantidad(total));
        consultas.Responder(Envios((1, 99)));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ConsultarAsync(7, 10, solicitada, tamano);

        Assert.Equal(paginaEsperada, resultado!.Pagina);
        Assert.Equal(tamano, resultado.TamanoPagina);
        Assert.Equal((total - 1) / tamano + 1, resultado.TotalPaginas);
        var paginaSql = consultas.Consultas[2];
        Assert.Matches(@"ORDER BY .*\[Id\] DESC", paginaSql.Sql);
        var paginacion = Regex.Match(paginaSql.Sql, @"OFFSET\s+(@\w+)\s+ROWS\s+FETCH NEXT\s+(@\w+)\s+ROWS");
        Assert.True(paginacion.Success);
        Assert.Equal(saltoEsperado, paginaSql.Parametros[paginacion.Groups[1].Value]);
        Assert.Equal(tamano, paginaSql.Parametros[paginacion.Groups[2].Value]);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(0, 10, 1, 10)]
    [InlineData(7, 0, 1, 10)]
    [InlineData(7, 10, 0, 10)]
    [InlineData(7, 10, 1, 0)]
    [InlineData(7, 10, 1, 51)]
    public async Task ConsultarAsync_ParametrosInvalidos_FallaAntesDeConsultar(
        int conductor, int viaje, int pagina, int tamano)
    {
        var consultas = new ConsultasEfSimuladas();
        await using var db = consultas.CrearContexto();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new OportunidadesLogica(db).ConsultarAsync(conductor, viaje, pagina, tamano));

        Assert.Empty(consultas.Consultas);
    }

    [Fact]
    public async Task ObtenerDetalleAsync_ViajePropioYEnvioDisponible_LeeDatosSinTrackingNiEscrituras()
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes());
        consultas.Responder(Envios((25, 99)));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ObtenerDetalleAsync(7, 10, 25);

        Assert.NotNull(resultado);
        Assert.Equal(10, resultado.Viaje.Id);
        Assert.Equal(25, resultado.Envio.Id);
        Assert.Equal("Retiro publicado", resultado.Envio.OrigenDir);
        Assert.Equal("Entrega publicada", resultado.Envio.DestinoDir);
        Assert.Equal("Paquete publicado", resultado.Envio.Descripcion);
        Assert.Equal(2.5m, resultado.Envio.PesoKg);
        Assert.Equal(TamanoEnvio.Mediano, resultado.Envio.Tamano);
        Assert.Equal(4000m, resultado.Envio.Monto);
        Assert.True(resultado.EvaluacionPendiente);
        Assert.Equal(2, consultas.Consultas.Count);
        Assert.Matches(@"\[Id\]\s*=\s*@", consultas.Consultas[0].Sql);
        Assert.Matches(@"\[UsuarioId\]\s*=\s*@", consultas.Consultas[0].Sql);
        Assert.Contains(consultas.Consultas[0].Parametros.Values, p => Equals(p, 7));
        Assert.Contains(consultas.Consultas[0].Parametros.Values, p => Equals(p, 10));
        Assert.Matches(@"\[Id\]\s*=\s*@", consultas.Consultas[1].Sql);
        Assert.Matches(@"\[Estado\]\s*=\s*(?:CAST\(\s*)?0\b", consultas.Consultas[1].Sql);
        Assert.Contains(consultas.Consultas[1].Parametros.Values, p => Equals(p, 25));
        Assert.DoesNotContain("[Usuarios]", consultas.Consultas[1].Sql);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(999)]
    public async Task ObtenerDetalleAsync_ViajeAjenoOInexistente_NoConsultaElEnvio(int viajeId)
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes(false));
        await using var db = consultas.CrearContexto();

        var resultado = await new OportunidadesLogica(db).ObtenerDetalleAsync(7, viajeId, 25);

        Assert.Null(resultado);
        Assert.Single(consultas.Consultas);
        Assert.DoesNotContain("[Envios]", consultas.Consultas[0].Sql);
    }

    [Theory]
    [InlineData(999)] // ID inexistente.
    [InlineData(25)] // El envío del listado ya no cumple el estado de disponibilidad.
    public async Task ObtenerDetalleAsync_EnvioInexistenteONoDisponible_MismoResultado(int envioId)
    {
        var consultas = new ConsultasEfSimuladas();
        consultas.Responder(Viajes());
        // El SELECT de ID + estado no tiene coincidencias en ambos casos.
        consultas.Responder(Envios());
        await using var db = consultas.CrearContexto();

        Assert.Null(await new OportunidadesLogica(db).ObtenerDetalleAsync(7, 10, envioId));

        Assert.Equal(2, consultas.Consultas.Count);
        Assert.Matches(@"\[Estado\]\s*=\s*(?:CAST\(\s*)?0\b", consultas.Consultas[1].Sql);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(0, 10, 25)]
    [InlineData(7, 0, 25)]
    [InlineData(7, 10, 0)]
    public async Task ObtenerDetalleAsync_IdsInvalidos_NoConsulta(int conductor, int viaje, int envio)
    {
        var consultas = new ConsultasEfSimuladas();
        await using var db = consultas.CrearContexto();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new OportunidadesLogica(db).ObtenerDetalleAsync(conductor, viaje, envio));

        Assert.Empty(consultas.Consultas);
    }

    [Fact]
    public async Task ObtenerDetalleAsync_Cancelacion_NoConsulta()
    {
        var consultas = new ConsultasEfSimuladas();
        await using var db = consultas.CrearContexto();
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OportunidadesLogica(db).ObtenerDetalleAsync(7, 10, 25, cancelacion.Token));

        Assert.Empty(consultas.Consultas);
    }

    [Fact]
    public async Task ConsultarAsync_Cancelacion_NoConectaNiConsulta()
    {
        var consultas = new ConsultasEfSimuladas();
        await using var db = consultas.CrearContexto();
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OportunidadesLogica(db).ConsultarAsync(7, 10, cancellationToken: cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Empty(consultas.Consultas);
    }
}
