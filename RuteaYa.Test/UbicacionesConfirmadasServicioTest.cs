using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class UbicacionesConfirmadasServicioTest
{
    [Fact]
    public void MigracionSqlServer_EsAditivaYCoincideConModeloSinConectar()
    {
        using var db = new RuteaYaDbContext(new DbContextOptionsBuilder<RuteaYaDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=NoConectar;Integrated Security=true;TrustServerCertificate=true").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261108145615_AgregarConductorAEnvio", "20261115070141_UbicacionesConfirmadasConComprobantes");
        Assert.Contains("CREATE TABLE [UbicacionesConfirmadas]", sql);
        Assert.Contains("ADD [OrigenUbicacionId] uniqueidentifier NULL", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
        Assert.DoesNotContain("UPDATE [Envios]", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task FuenteMvc_ExigeSeleccionRealDeEseUsuarioYApiConsumeSinProveedorHere()
    {
        using var f = new Fixture(); await using var db = f.Contexto();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var busqueda = new Mock<IBusquedaDireccionesServicio>(MockBehavior.Strict);
        busqueda.Setup(b => b.SugerirAsync("calle", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new SugerenciaDireccionDto { IdHere = "here:af:prueba" } });
        busqueda.Setup(b => b.ResolverAsync("here:af:prueba", It.IsAny<CancellationToken>())).ReturnsAsync(new ResultadoGeocodificacionDto {
            IdHere = "here:af:prueba", DireccionNormalizada = "Dirección verificada", TipoResultado = "houseNumber",
            Posicion = new() { Latitud = -34, Longitud = -58 }, Accesos = new() { new() { Latitud = -35, Longitud = -59 } } });
        var seleccion = new SeleccionDireccionServicio(busqueda.Object, cache, f.Reloj);
        var mvc = new UbicacionesConfirmadasServicio(db, new FuenteSeleccionUbicacionVerificada(seleccion), f.Reloj);
        var c = await mvc.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var sugerencia = (await seleccion.SugerirAsync(7, c.AmbitoSeleccion, "calle")).Single();
        var resuelta = await seleccion.ResolverAsync(7, c.AmbitoSeleccion, sugerencia.Token);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => mvc.EmitirAsync(7, c, resuelta.Token));
        var confirmada = seleccion.Confirmar(7, c.AmbitoSeleccion, resuelta.Token, 0);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => mvc.EmitirAsync(8, c, confirmada.Token));
        var recibo = await mvc.EmitirAsync(7, c, confirmada.Token);
        await using var dbApi = f.Contexto();
        var api = new UbicacionesConfirmadasServicio(dbApi, reloj: f.Reloj); // deliberadamente sin fuente ni clave HERE.
        var viaje = ViajeNuevo(); dbApi.Viajes.Add(viaje);
        await api.GuardarAsync(7, new[] { new AsignacionUbicacionDto(c, recibo.Token, Viaje: viaje) });
        Assert.Equal(-35m, viaje.OrigenUbicacion!.NavegacionLatitud);
        busqueda.Verify(b => b.ResolverAsync("here:af:prueba", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsumoFueraDeGuardadoTransaccional_SeRechazaInclusoConEfDirecto()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var c = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        await servicio.EmitirAsync(7, c, f.Seleccion(c));
        var ubicacion = await db.UbicacionesConfirmadas.SingleAsync();
        ubicacion.ConsumidaEnUtc = f.Reloj.Ahora; ubicacion.Version = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await using var tx = await db.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); // hay transacción, pero falta guardado del vínculo.
        await tx.RollbackAsync();
        await using var lectura = f.Contexto(); Assert.Null((await lectura.UbicacionesConfirmadas.SingleAsync()).ConsumidaEnUtc);
    }

    [Fact]
    public async Task CambiosAjenosEnContexto_NoSeGuardanComoEfectoSecundario()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var c = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var recibo = await servicio.EmitirAsync(7, c, f.Seleccion(c));
        var viaje = ViajeNuevo(); db.Viajes.Add(viaje);
        db.Envios.Add(new Envio { UsuarioId = 8, OrigenDir = "Ajeno", DestinoDir = "Ajeno", Descripcion = "No guardar", PesoKg = 1 });
        var error = await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.GuardarAsync(7,
            new[] { new AsignacionUbicacionDto(c, recibo.Token, Viaje: viaje) }));
        Assert.Equal("cambios_ajenos_en_unidad_de_trabajo", error.Codigo);
        await using var lectura = f.Contexto();
        Assert.Empty(await lectura.Envios.ToListAsync()); Assert.Empty(await lectura.Viajes.ToListAsync());
        Assert.Null((await lectura.UbicacionesConfirmadas.SingleAsync()).ConsumidaEnUtc);
    }

    [Fact]
    public async Task EmisionYGuardado_UnSoloSnapshotConservaPositionYNavegacionYNoGuardaTokens()
    {
        using var f = new Fixture();
        await using var db = f.Contexto();
        var servicio = f.Servicio(db);
        var contexto = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var seleccion = f.Seleccion(contexto, acceso: 0);
        var recibo = await servicio.EmitirAsync(7, contexto, seleccion);
        var snapshot = await db.UbicacionesConfirmadas.SingleAsync();
        Assert.NotEqual(recibo.Token, snapshot.TokenHash);
        Assert.NotEqual(seleccion, snapshot.SeleccionHash);
        Assert.Equal(-34m, snapshot.PosicionLatitud);
        Assert.Equal(-35m, snapshot.NavegacionLatitud);
        var viaje = ViajeNuevo(); db.Viajes.Add(viaje);
        await servicio.GuardarAsync(7, new[] { new AsignacionUbicacionDto(contexto, recibo.Token, Viaje: viaje) });
        Assert.Equal("Dirección verificada", viaje.Origen);
        Assert.Equal(snapshot.Id, viaje.OrigenUbicacionId);
        Assert.Null(viaje.DestinoUbicacionId); // el servicio base permite confirmar parcialmente un recurso legado.
        Assert.NotNull(snapshot.ConsumidaEnUtc);
        Assert.Single(await db.UbicacionesConfirmadas.ToListAsync());
        await using var lectura = f.Contexto();
        var guardado = await lectura.Viajes.Include(v => v.OrigenUbicacion).SingleAsync();
        Assert.Equal(-35m, guardado.OrigenUbicacion!.NavegacionLatitud);
        f.Reloj.Ahora = f.Reloj.Ahora.AddDays(1);
        Assert.Equal(snapshot.Id, guardado.OrigenUbicacionId); // el snapshot guardado no pierde su validez por vencer el comprobante.
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("formulario")]
    [InlineData("campo")]
    [InlineData("recurso")]
    [InlineData("vencimiento")]
    [InlineData("token")]
    public async Task ComprobanteNoCoincidente_NoConsumeNiGuarda(string ataque)
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var contexto = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var recibo = await servicio.EmitirAsync(7, contexto, f.Seleccion(contexto));
        var destino = ViajeNuevo(ataque == "usuario" ? 8 : 7); db.Viajes.Add(destino);
        var usado = contexto;
        if (ataque == "formulario") usado = contexto with { FormularioId = Guid.NewGuid() };
        if (ataque == "campo") usado = contexto with { Campo = CampoUbicacion.DestinoViaje, ClaveCampo = "DestinoViaje" };
        if (ataque == "recurso") usado = contexto with { RecursoId = 999 };
        if (ataque == "vencimiento") f.Reloj.Ahora = f.Reloj.Ahora.AddMinutes(11);
        await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.GuardarAsync(ataque == "usuario" ? 8 : 7,
            new[] { new AsignacionUbicacionDto(usado, ataque == "token" ? new string('F', 64) : recibo.Token, Viaje: destino) }));
        await using var comprobar = f.Contexto();
        Assert.Empty(await comprobar.Viajes.ToListAsync());
        Assert.Null((await comprobar.UbicacionesConfirmadas.SingleAsync()).ConsumidaEnUtc);
        Assert.Null(destino.OrigenUbicacion);
    }

    [Fact]
    public async Task ConsumoDobleYConcurrente_SoloUnGuardadoPuedeGanar()
    {
        using var f = new Fixture();
        ContextoUbicacionDto contexto; ComprobanteUbicacionDto recibo;
        await using (var emision = f.Contexto()) {
            var servicio = f.Servicio(emision);
            contexto = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
            recibo = await servicio.EmitirAsync(7, contexto, f.Seleccion(contexto));
        }
        await using var uno = f.Contexto(); await using var dos = f.Contexto();
        // Dos peticiones que leyeron la misma versión disponible antes de guardar.
        await uno.UbicacionesConfirmadas.SingleAsync(); await dos.UbicacionesConfirmadas.SingleAsync();
        var v1 = ViajeNuevo(); var v2 = ViajeNuevo(); uno.Viajes.Add(v1); dos.Viajes.Add(v2);
        await f.Servicio(uno).GuardarAsync(7, new[] { new AsignacionUbicacionDto(contexto, recibo.Token, Viaje: v1) });
        var conflicto = await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => f.Servicio(dos).GuardarAsync(7,
            new[] { new AsignacionUbicacionDto(contexto, recibo.Token, Viaje: v2) }));
        Assert.Equal("consumo_en_conflicto", conflicto.Codigo);
        await using var tercero = f.Contexto(); var v3 = ViajeNuevo(); tercero.Viajes.Add(v3);
        var repetido = await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => f.Servicio(tercero).GuardarAsync(7,
            new[] { new AsignacionUbicacionDto(contexto, recibo.Token, Viaje: v3) }));
        Assert.Equal("comprobante_consumido", repetido.Codigo);
        await using var lectura = f.Contexto(); Assert.Single(await lectura.Viajes.ToListAsync());
    }

    [Fact]
    public async Task FallaDeGuardado_RevierteConsumoYVinculoYPermiteReintentar()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var contexto = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var recibo = await servicio.EmitirAsync(7, contexto, f.Seleccion(contexto));
        var viaje = ViajeNuevo();
        // Violación real de índice único de días, dentro de la misma transacción del consumo.
        viaje.Dias.Add(new DiaViaje { DiaSemana = 1 }); viaje.Dias.Add(new DiaViaje { DiaSemana = 1 });
        db.Viajes.Add(viaje);
        await Assert.ThrowsAsync<DbUpdateException>(() => servicio.GuardarAsync(7,
            new[] { new AsignacionUbicacionDto(contexto, recibo.Token, Viaje: viaje) }));
        await using (var lectura = f.Contexto()) {
            Assert.Empty(await lectura.Viajes.ToListAsync());
            Assert.Null((await lectura.UbicacionesConfirmadas.SingleAsync()).ConsumidaEnUtc);
        }
        Assert.Null(viaje.OrigenUbicacion); Assert.Equal("Publicado", viaje.Origen);
        // Nueva petición y contexto EF tras el error de guardado.
        await using var reintento = f.Contexto(); var nuevo = ViajeNuevo(); reintento.Viajes.Add(nuevo);
        await f.Servicio(reintento).GuardarAsync(7, new[] { new AsignacionUbicacionDto(contexto, recibo.Token, Viaje: nuevo) });
    }

    [Fact]
    public async Task EmisionRepetidaOSeleccionManipulada_NoCreaOtroComprobante()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var c = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var token = f.Seleccion(c);
        await servicio.EmitirAsync(7, c, token);
        await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.EmitirAsync(7, c, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EmitirAsync(7, c with { FormularioId = Guid.NewGuid() }, token));
        Assert.Single(await db.UbicacionesConfirmadas.ToListAsync());
    }

    [Fact]
    public async Task RecursoAjenoOEditado_NoSePuedeUsar()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var viaje = ViajeNuevo(); db.Viajes.Add(viaje); await db.SaveChangesAsync();
        var servicio = f.Servicio(db);
        await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.CrearContextoAsync(8, TipoRecursoUbicacion.Viaje, viaje.Id, CampoUbicacion.OrigenViaje, "OrigenViaje"));
        var c = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, viaje.Id, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var recibo = await servicio.EmitirAsync(7, c, f.Seleccion(c));
        await using (var otro = f.Contexto()) { var v = await otro.Viajes.SingleAsync(); v.Destino = "Editado"; await otro.SaveChangesAsync(); }
        await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.GuardarAsync(7,
            new[] { new AsignacionUbicacionDto(c, recibo.Token, Viaje: viaje) }));
        await using var lectura = f.Contexto(); Assert.Null((await lectura.UbicacionesConfirmadas.SingleAsync()).ConsumidaEnUtc);
    }

    [Fact]
    public async Task DatosLegados_NoSeMarcanConfirmadosNiSeAlteran()
    {
        using var f = new Fixture(); await using var db = f.Contexto();
        var viaje = ViajeNuevo(); viaje.Paradas.Add(new Parada { Ubicacion = "Parada antigua", Orden = 1 });
        var envio = new Envio { UsuarioId = 7, OrigenDir = "Retiro antiguo", DestinoDir = "Entrega antigua", OrigenLat = 0,
            DestinoLat = -34.612345m, Descripcion = "Legado", PesoKg = 1, Monto = 100 };
        db.AddRange(viaje, envio); await db.SaveChangesAsync();
        await using var lectura = f.Contexto();
        var v = await lectura.Viajes.Include(v => v.Paradas).SingleAsync(); var e = await lectura.Envios.SingleAsync();
        Assert.Null(v.OrigenUbicacionId); Assert.Null(v.DestinoUbicacionId); Assert.Null(v.Paradas.Single().UbicacionConfirmadaId);
        Assert.Null(e.RetiroUbicacionId); Assert.Null(e.EntregaUbicacionId); Assert.Equal(-34.612345m, e.DestinoLat);
        Assert.Equal("Publicado", v.Origen); Assert.Empty(await lectura.UbicacionesConfirmadas.ToListAsync());
    }

    [Fact]
    public async Task SnapshotNoPuedeModificarCoordenadasLuegoDeEmitido()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var c = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        await servicio.EmitirAsync(7, c, f.Seleccion(c));
        (await db.UbicacionesConfirmadas.SingleAsync()).NavegacionLatitud = 80;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await using var lectura = f.Contexto(); Assert.NotEqual(80, (await lectura.UbicacionesConfirmadas.SingleAsync()).NavegacionLatitud);
    }

    [Fact]
    public async Task LoteViajeYParadaYEnvio_ConservaRolesYPosicionLegada()
    {
        using var f = new Fixture(); await using var db = f.Contexto(); var servicio = f.Servicio(db);
        var origen = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje");
        var parada = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Viaje, null, CampoUbicacion.Parada, Guid.NewGuid().ToString("N"), origen.FormularioId);
        var r1 = await servicio.EmitirAsync(7, origen, f.Seleccion(origen)); var r2 = await servicio.EmitirAsync(7, parada, f.Seleccion(parada));
        var v = ViajeNuevo(); var p = new Parada { Viaje = v, Ubicacion = "Vieja", Orden = 1 }; v.Paradas.Add(p); db.Viajes.Add(v);
        await servicio.GuardarAsync(7, new[] { new AsignacionUbicacionDto(origen, r1.Token, Viaje: v), new AsignacionUbicacionDto(parada, r2.Token, Parada: p) });
        Assert.NotNull(p.UbicacionConfirmadaId);
        var retiro = await servicio.CrearContextoAsync(7, TipoRecursoUbicacion.Envio, null, CampoUbicacion.RetiroEnvio, "RetiroEnvio");
        var r3 = await servicio.EmitirAsync(7, retiro, f.Seleccion(retiro, acceso: 0));
        var envio = new Envio { UsuarioId = 7, OrigenDir = "Original", DestinoDir = "Destino", Descripcion = "Envío", PesoKg = 1, Monto = 2 };
        db.Envios.Add(envio);
        await servicio.GuardarAsync(7, new[] { new AsignacionUbicacionDto(retiro, r3.Token, Envio: envio) });
        Assert.Equal(-34m, envio.OrigenLat); Assert.Equal(-35m, envio.RetiroUbicacion!.NavegacionLatitud);
    }

    private static Viaje ViajeNuevo(int usuario = 7) => new() { UsuarioId = usuario, Origen = "Publicado", Destino = "Destino", Activo = true, PesoMax = 10, CantMaxPaquetes = 2 };
    private sealed class RelojPrueba : TimeProvider { public DateTimeOffset Ahora = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Ahora; }
    private sealed class Fuente : IFuenteSeleccionUbicacionVerificada
    {
        public Dictionary<(int, string, string), SeleccionUbicacionVerificadaDto> Datos = new();
        public SeleccionUbicacionVerificadaDto Obtener(int usuarioId, ContextoUbicacionDto c, string token) =>
            Datos.TryGetValue((usuarioId, c.AmbitoSeleccion, token), out var dato) ? dato : throw new InvalidOperationException("Selección no verificada");
    }
    private sealed class ContextoPrueba(DbContextOptions<RuteaYaDbContext> opciones) : RuteaYaDbContext(opciones)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            base.OnModelCreating(modelBuilder);
            // SQL Server genera RowVersion; SQLite de pruebas no ofrece ese tipo. No se cambia el modelo productivo.
            modelBuilder.Entity<Envio>().Property(e => e.RowVersion).ValueGeneratedNever();
            // SQLite guarda decimal como TEXT por defecto; usar REAL para ejecutar checks de rango numéricos.
            foreach (var nombre in new[] { "PosicionLatitud", "PosicionLongitud", "NavegacionLatitud", "NavegacionLongitud" })
                modelBuilder.Entity<UbicacionConfirmada>().Property<decimal>(nombre).HasConversion<double>();
        }
    }
    private sealed class Fixture : IDisposable
    {
        private SqliteConnection _conexion = new("Data Source=:memory:");
        public RelojPrueba Reloj = new(); private Fuente _fuente = new();
        public Fixture() {
            _conexion.Open(); using var db = Contexto(); db.Database.EnsureCreated(); // esquema efímero; no ejecuta migraciones.
            db.Usuarios.AddRange(new Usuario { Id = 7, Nombre = "Usuario", Apellido = "Prueba", Email = "siete@test.invalid", PasswordHash = "ficticio" },
                new Usuario { Id = 8, Nombre = "Otro", Apellido = "Prueba", Email = "ocho@test.invalid", PasswordHash = "ficticio" });
            db.SaveChanges();
        }
        public RuteaYaDbContext Contexto() => new ContextoPrueba(new DbContextOptionsBuilder<RuteaYaDbContext>().UseSqlite(_conexion).Options);
        public UbicacionesConfirmadasServicio Servicio(RuteaYaDbContext db) => new(db, _fuente, Reloj);
        public string Seleccion(ContextoUbicacionDto c, int acceso = -1) {
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            _fuente.Datos[(7, c.AmbitoSeleccion, token)] = new(new ResultadoGeocodificacionDto {
                IdHere = "here:af:prueba", DireccionNormalizada = "Dirección verificada", TipoResultado = "houseNumber",
                Posicion = new() { Latitud = -34, Longitud = -58 }, Accesos = new() { new() { Latitud = -35, Longitud = -59 } }
            }, acceso, Reloj.Ahora, Reloj.Ahora.AddMinutes(10));
            return token;
        }
        public void Dispose() => _conexion.Dispose();
    }
}
