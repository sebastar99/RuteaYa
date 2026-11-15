using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class CompatibilidadPersistidaTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListadoYDetalle_UsanNavegacionYParadasOrdenadas_SinInventarSalidaOCapacidad(bool paradas)
    {
        using var f = new Fixture(paradas); await using var db = f.Db(); var rutas = new RutasPrueba(f.CantidadBase);
        var logica = Logica(db, rutas);
        f.Contador.Consultas = 0;
        var listado = await logica.ConsultarAsync(7, f.ViajeId);
        Assert.NotNull(listado); Assert.Single(listado.Candidatos);
        var evaluacion = listado.Evaluaciones[f.EnvioId];
        Assert.True(evaluacion.MotorEjecutado); Assert.Equal(EstadoCompatibilidad.Indeterminado, evaluacion.Estado);
        Assert.Contains(DatoPendienteOportunidad.SalidaConcretaDesconocida, evaluacion.DatosPendientes);
        Assert.Contains(DatoPendienteOportunidad.PesoDisponibleDesconocido, evaluacion.DatosPendientes);
        Assert.Contains(DatoPendienteOportunidad.CuposDisponiblesDesconocidos, evaluacion.DatosPendientes);
        Assert.Contains(DatoPendienteOportunidad.VencimientoEntregaNoInterpretado, evaluacion.DatosPendientes);
        Assert.Equal(paradas ? new[] { -34.6m, -34.7m, -35m } : new[] { -34.6m, -35m }, rutas.Pedidos[0].Select(p => p.Latitud));
        Assert.DoesNotContain(rutas.Pedidos.SelectMany(p => p), p => p.Latitud == 89m); // latitudes legadas de Envio no se usan.
        Assert.Equal(1000, evaluacion.ResultadoMotor!.InsercionSeleccionada!.Desvio.DistanciaAdicionalMetros);
        Assert.Null(evaluacion.ResultadoMotor.InsercionSeleccionada.FinalizacionEstimada);
        Assert.Equal(7, f.Contador.Consultas); // selector + count + página + cuatro consultas de fuente.
        Assert.Empty(db.ChangeTracker.Entries());
        // Nueva consulta (otra solicitud): vuelve a validar, no reutiliza resultados de negocio obsoletos.
        var detalle = await Logica(db, new RutasPrueba(f.CantidadBase)).ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.Equal(evaluacion.Estado, detalle!.Evaluacion.Estado);
    }

    [Fact]
    public async Task Lote_NoHaceNMasUnoYReutilizaLaRutaBaseEntreEnvios()
    {
        using var f = new Fixture(false, cantidadEnvios: 3); await using var db = f.Db();
        var proveedor = new RutasPrueba(f.CantidadBase); f.Contador.Consultas = 0;
        var listado = await Logica(db, proveedor).ConsultarAsync(7, f.ViajeId);
        Assert.Equal(3, listado!.Evaluaciones.Count);
        Assert.Equal(7, f.Contador.Consultas);
        Assert.Single(proveedor.Pedidos, p => p.Count == f.CantidadBase);
        // Tres envíos distintos, mismos puntos: también se reutiliza la alternativa idéntica, sin duplicar el motor por fila.
        Assert.Equal(2, proveedor.Pedidos.Count);
    }

    [Theory]
    [InlineData("legado")]
    [InlineData("retiro")]
    [InlineData("parada")]
    [InlineData("usuario")]
    [InlineData("texto")]
    public async Task ConfirmacionAusenteOInvalida_IndeterminadoSinHere(string caso)
    {
        using var f = new Fixture(true, defecto: caso); await using var db = f.Db(); var rutas = new RutasPrueba(f.CantidadBase);
        var detalle = await Logica(db, rutas).ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, detalle!.Evaluacion.Estado);
        Assert.False(detalle.Evaluacion.MotorEjecutado);
        Assert.NotEmpty(detalle.Evaluacion.DatosPendientes);
        Assert.Empty(rutas.Pedidos);
    }

    [Fact]
    public async Task PesoSuperaMaximoPublicado_RechazoProbadoSinAsumirPesoRestanteNiConsultarHere()
    {
        using var f = new Fixture(false, pesoMax: 1); await using var db = f.Db(); var rutas = new RutasPrueba(2);
        var detalle = await Logica(db, rutas).ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.Equal(EstadoCompatibilidad.Incompatible, detalle!.Evaluacion.Estado);
        Assert.Contains(MotivoCompatibilidad.PesoMaximoPublicadoExcedido, detalle.Evaluacion.ResultadoMotor!.Motivos);
        Assert.Empty(rutas.Pedidos);
    }

    [Fact]
    public async Task DesvioExcesivo_RechazoGeograficoAunqueFaltenSalidaYCapacidad()
    {
        using var f = new Fixture(false); await using var db = f.Db(); var rutas = new RutasPrueba(2) { Adicional = 5000 };
        var detalle = await Logica(db, rutas).ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.Equal(EstadoCompatibilidad.Incompatible, detalle!.Evaluacion.Estado);
        Assert.Contains(MotivoCompatibilidad.DesvioExcedido, detalle.Evaluacion.ResultadoMotor!.Motivos);
        Assert.Contains(DatoPendienteOportunidad.SalidaConcretaDesconocida, detalle.Evaluacion.DatosPendientes);
    }

    [Fact]
    public async Task ViajeInactivo_RechazoSinHere()
    {
        using var f = new Fixture(false); await using var db = f.Db();
        var viaje = await db.Viajes.SingleAsync(); viaje.Activo = false; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var rutas = new RutasPrueba(2);
        var detalle = await Logica(db, rutas).ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.Equal(EstadoCompatibilidad.Incompatible, detalle!.Evaluacion.Estado);
        Assert.Contains(MotivoCompatibilidad.ViajeInactivo, detalle.Evaluacion.ResultadoMotor!.Motivos);
        Assert.Empty(rutas.Pedidos);
    }

    [Fact]
    public async Task SnapshotGuardadoSigueUtilizableAunqueElComprobanteTemporalYaHayaVencido()
    {
        using var f = new Fixture(false, snapshotAntiguo: true); await using var db = f.Db(); var rutas = new RutasPrueba(2);
        var detalle = await Logica(db, rutas).ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.True(detalle!.Evaluacion.MotorEjecutado); Assert.NotEmpty(rutas.Pedidos);
        Assert.DoesNotContain(DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas, detalle.Evaluacion.DatosPendientes);
    }

    [Theory]
    [InlineData("compatible", EstadoCompatibilidad.Compatible)]
    [InlineData("peso", EstadoCompatibilidad.Incompatible)]
    [InlineData("cupos", EstadoCompatibilidad.Incompatible)]
    [InlineData("llegada", EstadoCompatibilidad.Incompatible)]
    [InlineData("disponibilidad", EstadoCompatibilidad.Incompatible)]
    public async Task ContratosConSalidaYCapacidadExplicitas_ReutilizanMotorExistente(string caso, EstadoCompatibilidad esperado)
    {
        using var f = new Fixture(false); await using var db = f.Db(); var rutas = new RutasPrueba(2);
        // Escenario explícito de prueba. Ninguno de estos suplementos se infiere en la fuente productiva.
        var fuente = new FuenteConDatosCompletos(new DatosConfirmadosOportunidadPersistidosServicio(db), caso);
        var motor = new CompatibilidadServicio(new DesviosServicio(new RutasConsultaServicio(rutas)));
        var logica = new OportunidadesLogica(db, new EvaluacionOportunidadServicio(fuente, motor));
        var detalle = await logica.ObtenerDetalleAsync(7, f.ViajeId, f.EnvioId);
        Assert.Equal(esperado, detalle!.Evaluacion.Estado);
        if (caso is "peso" or "cupos" or "disponibilidad") Assert.Empty(rutas.Pedidos);
        if (caso == "llegada") Assert.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida, detalle.Evaluacion.ResultadoMotor!.Motivos);
    }

    [Fact]
    public async Task ViajeAjenoYEnvioPropioONoDisponible_NoSeEvalúan()
    {
        using var f = new Fixture(false); await using var db = f.Db(); var rutas = new RutasPrueba(2);
        var logica = Logica(db, rutas);
        Assert.Null(await logica.ConsultarAsync(8, f.ViajeId));
        Assert.Null(await logica.ObtenerDetalleAsync(8, f.ViajeId, f.EnvioId));
        Assert.Null(await logica.ObtenerDetalleAsync(7, f.ViajeId, f.EnvioPropioId));
        Assert.Null(await logica.ObtenerDetalleAsync(7, f.ViajeId, f.EnvioNoDisponibleId));
        Assert.Empty(rutas.Pedidos);
    }

    private static OportunidadesLogica Logica(RuteaYaDbContext db, RutasPrueba proveedor) => new(db,
        new EvaluacionOportunidadServicio(new DatosConfirmadosOportunidadPersistidosServicio(db),
            new CompatibilidadServicio(new DesviosServicio(new RutasConsultaServicio(proveedor)))));

    private sealed class FuenteConDatosCompletos(IDatosConfirmadosOportunidadServicio fuente, string caso) : IDatosConfirmadosOportunidadServicio
    {
        public async Task<DatosConfirmadosOportunidadDto?> ObtenerAsync(ViajeOportunidadesDto viaje, EnvioDto envio, CancellationToken cancellationToken = default)
        {
            var datos = (await fuente.ObtenerAsync(viaje, envio, cancellationToken))!;
            var salida = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
            datos.SalidaProgramada = salida; datos.Disponible = caso != "disponibilidad";
            datos.PesoDisponibleKg = caso == "peso" ? 1 : 10; datos.CuposDisponibles = caso == "cupos" ? 0 : 2;
            datos.LlegadaMaxima = caso == "llegada" ? RestriccionTemporalDto.Informada(salida.AddSeconds(500)) : RestriccionTemporalDto.SinRestriccion();
            datos.DisponibleDesde = RestriccionTemporalDto.SinRestriccion();
            datos.FechaLimiteEntrega = RestriccionTemporalDto.Informada(salida.AddDays(1));
            return datos;
        }
    }
    private sealed class RutasPrueba(int cantidadBase) : IRutasServicio
    {
        public List<IReadOnlyList<PuntoRutaDto>> Pedidos = new(); public long Adicional = 1000;
        public Task<ResultadoRutaDto> CalcularAsync(IReadOnlyList<PuntoRutaDto> puntos, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Pedidos.Add(puntos);
            return Task.FromResult(new ResultadoRutaDto { DistanciaMetros = puntos.Count == cantidadBase ? 10000 : 10000 + Adicional,
                DuracionSegundos = puntos.Count == cantidadBase ? 1000 : 1200 });
        }
    }
    private sealed class Contador : DbCommandInterceptor
    {
        public int Consultas;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { Consultas++; return ValueTask.FromResult(result); }
    }
    private sealed class DbPrueba(DbContextOptions<RuteaYaDbContext> opciones) : RuteaYaDbContext(opciones)
    {
        protected override void OnModelCreating(ModelBuilder b) {
            base.OnModelCreating(b); b.Entity<Envio>().Property(e => e.RowVersion).ValueGeneratedNever();
            foreach (var p in new[] { "PosicionLatitud", "PosicionLongitud", "NavegacionLatitud", "NavegacionLongitud" }) b.Entity<UbicacionConfirmada>().Property<decimal>(p).HasConversion<double>();
        }
    }
    private sealed class Fixture : IDisposable
    {
        private SqliteConnection _conexion = new("Data Source=:memory:"); public Contador Contador = new();
        public int ViajeId, EnvioId, EnvioPropioId, EnvioNoDisponibleId, CantidadBase;
        public RuteaYaDbContext Db() => new DbPrueba(new DbContextOptionsBuilder<RuteaYaDbContext>().UseSqlite(_conexion).AddInterceptors(Contador).Options);
        public Fixture(bool paradas, int cantidadEnvios = 1, string? defecto = null, decimal pesoMax = 100, bool snapshotAntiguo = false)
        {
            _conexion.Open(); using var db = Db(); db.Database.EnsureCreated();
            db.Usuarios.AddRange(new Usuario { Id = 7, Nombre = "Conductor", Apellido = "Test", Email = "conductor@test.invalid", PasswordHash = "ficticio" },
                new Usuario { Id = 8, Nombre = "Remitente", Apellido = "Test", Email = "remitente@test.invalid", PasswordHash = "ficticio" }); db.SaveChanges();
            UbicacionConfirmada U(int usuario, TipoRecursoUbicacion tipo, CampoUbicacion campo, string texto, decimal lat) => new() {
                UsuarioId = usuario, TipoRecurso = tipo, Campo = campo, ClaveCampo = Guid.NewGuid().ToString("N"), FormularioId = Guid.NewGuid(),
                DireccionNormalizada = texto, IdHere = "here:af:prueba", PosicionLatitud = lat + .001m, PosicionLongitud = -58.001m,
                NavegacionLatitud = lat, NavegacionLongitud = -58, AccesoElegido = 0, TipoResultado = "houseNumber",
                ConfirmadaEnUtc = DateTimeOffset.UtcNow.AddDays(snapshotAntiguo ? -2 : 0).AddMinutes(-1), ConsumidaEnUtc = DateTimeOffset.UtcNow.AddDays(snapshotAntiguo ? -2 : 0),
                VenceEnUtc = DateTimeOffset.UtcNow.AddDays(snapshotAntiguo ? -2 : 0).AddMinutes(9), TokenHash = Guid.NewGuid().ToString("N"), SeleccionHash = Guid.NewGuid().ToString("N") };
            var v = new Viaje { UsuarioId = 7, Origen = "Origen", Destino = "Destino", Activo = true, PesoMax = pesoMax, CantMaxPaquetes = 2, DesvioMaxKm = 2,
                HoraSalida = TimeSpan.FromHours(8), HoraLlegada = TimeSpan.FromHours(9), Dias = new List<DiaViaje> { new() { DiaSemana = 1 }, new() { DiaSemana = 4 } } };
            v.OrigenUbicacion = U(7, TipoRecursoUbicacion.Viaje, CampoUbicacion.OrigenViaje, v.Origen, -34.6m);
            v.DestinoUbicacion = U(7, TipoRecursoUbicacion.Viaje, CampoUbicacion.DestinoViaje, v.Destino, -35m);
            if (paradas) v.Paradas.Add(new Parada { Orden = 1, Ubicacion = "Parada", UbicacionConfirmada = defecto == "parada" ? null : U(7, TipoRecursoUbicacion.Viaje, CampoUbicacion.Parada, "Parada", -34.7m) });
            if (defecto == "legado") { v.OrigenUbicacion = null; v.DestinoUbicacion = null; }
            CantidadBase = paradas ? 3 : 2; db.Viajes.Add(v);
            for (var i = 0; i < cantidadEnvios; i++) {
                var e = new Envio { UsuarioId = 8, OrigenDir = "Retiro", DestinoDir = "Entrega", Descripcion = "Paquete", Estado = EstadoEnvio.BuscandoConductor,
                    PesoKg = 2.5m, Monto = 100, OrigenLat = 89, DestinoLat = 89, FechaLimite = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Unspecified) };
                e.RetiroUbicacion = defecto == "retiro" ? null : U(defecto == "usuario" ? 7 : 8, TipoRecursoUbicacion.Envio, CampoUbicacion.RetiroEnvio, defecto == "texto" ? "No corresponde" : e.OrigenDir, -34.8m);
                e.EntregaUbicacion = U(8, TipoRecursoUbicacion.Envio, CampoUbicacion.EntregaEnvio, e.DestinoDir, -34.9m);
                db.Envios.Add(e); db.SaveChanges(); if (i == 0) EnvioId = e.Id;
            }
            var propio = new Envio { UsuarioId = 7, OrigenDir = "Propio", DestinoDir = "Entrega", Descripcion = "Propio", Estado = EstadoEnvio.BuscandoConductor, PesoKg = 1 };
            var finalizado = new Envio { UsuarioId = 8, OrigenDir = "No disponible", DestinoDir = "Entrega", Descripcion = "Terminado", Estado = EstadoEnvio.Entregado, PesoKg = 1 };
            db.Envios.AddRange(propio, finalizado); db.SaveChanges(); ViajeId = v.Id; EnvioPropioId = propio.Id; EnvioNoDisponibleId = finalizado.Id;
        }
        public void Dispose() => _conexion.Dispose();
    }
}
