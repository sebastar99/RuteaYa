using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;
using Fixture = RuteaYa.Test.CompatibilidadPersistidaTest.Fixture;

namespace RuteaYa.Test;

public class AceptacionEnviosTest
{
    private static OportunidadesLogica Oportunidades(RuteaYaDbContext db, IRutasServicio? rutas = null) => new(db,
        new EvaluacionOportunidadServicio(new DatosConfirmadosOportunidadPersistidosServicio(db),
            new CompatibilidadServicio(new DesviosServicio(new RutasConsultaServicio(rutas ?? new Rutas())))));
    private static EnviosLogica Envios(RuteaYaDbContext db, IRutasServicio? rutas = null) => new(db, null!, oportunidades: Oportunidades(db, rutas));

    private static async Task<int> Preparar(Fixture f, RuteaYaDbContext db)
    {
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2));
        while (fecha.DayOfWeek != DayOfWeek.Monday) fecha = fecha.AddDays(1);
        var id = (await new SalidasViajeServicio(db).SeleccionarAsync(7, new() { ViajeId = f.ViajeId, Fecha = fecha,
            ZonaHorariaId = ZonaHorariaViaje.Argentina, LlegadaSinRestriccion = true }))!.Value;
        foreach (var e in await db.Envios.Where(e => e.Estado == EstadoEnvio.BuscandoConductor).ToListAsync()) {
            e.FechaLimite = fecha.AddDays(14).ToDateTime(new TimeOnly(18, 0));
            e.EntregaLimiteConOffset = new DateTimeOffset(e.FechaLimite, TimeSpan.FromHours(-3)); e.RetiroSinRestriccion = true;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return id;
    }

    [Fact]
    public async Task AceptaAtomico_SeListaEnTransportes_YReintentoNoReservaNiRetrocedeEstados()
    {
        using var f = new Fixture(false); await using var db = f.Db(); var salida = await Preparar(f, db);
        var version = (await db.SalidasViaje.AsNoTracking().SingleAsync()).Version;
        var logica = Envios(db);
        Assert.Equal(EstadoAceptacion.Aceptado, (await logica.AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Estado);
        var guardado = await db.Envios.AsNoTracking().SingleAsync(e => e.Id == f.EnvioId);
        Assert.Equal(7, guardado.ConductorId); Assert.Equal(salida, guardado.SalidaViajeId); Assert.Equal(EstadoEnvio.ConductorAsignado, guardado.Estado);
        var versionAceptada = (await db.SalidasViaje.AsNoTracking().SingleAsync()).Version; Assert.NotEqual(version, versionAceptada);
        Assert.Equal(f.EnvioId, Assert.Single(await logica.ListarTransportesAsync(7)).Id);
        Assert.True(await logica.ActualizarEstadoConductorAsync(7, f.EnvioId, EstadoEnvio.Retirado));
        Assert.Equal(EstadoAceptacion.YaAceptado, (await logica.AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Estado);
        Assert.Equal(EstadoEnvio.Retirado, (await db.Envios.AsNoTracking().SingleAsync(e => e.Id == f.EnvioId)).Estado);
        Assert.Equal(versionAceptada, (await db.SalidasViaje.AsNoTracking().SingleAsync()).Version);
        Assert.Single(await logica.ListarTransportesAsync(7));
    }

    [Theory]
    [InlineData("viaje_ajeno", EstadoAceptacion.NoEncontrado)]
    [InlineData("propio", EstadoAceptacion.NoEncontrado)]
    [InlineData("asignado", EstadoAceptacion.EnvioNoDisponible)]
    [InlineData("inactivo", EstadoAceptacion.SalidaNoValida)]
    [InlineData("pasada", EstadoAceptacion.SalidaNoValida)]
    [InlineData("sin_disponibilidad", EstadoAceptacion.SalidaNoValida)]
    [InlineData("dia", EstadoAceptacion.SalidaNoValida)]
    [InlineData("coordenadas", EstadoAceptacion.DatosPendientes)]
    [InlineData("horarios", EstadoAceptacion.DatosPendientes)]
    [InlineData("peso", EstadoAceptacion.CapacidadInsuficiente)]
    [InlineData("cupos", EstadoAceptacion.CapacidadInsuficiente)]
    [InlineData("desvio", EstadoAceptacion.Incompatible)]
    [InlineData("vencimiento", EstadoAceptacion.Incompatible)]
    public async Task RevalidaDatosActuales_NoAsignaAlFallar(string caso, EstadoAceptacion esperado)
    {
        using var f = new Fixture(false); await using var db = f.Db(); var salidaId = await Preparar(f, db);
        // La pantalla anterior podía haber sido Compatible. Los datos cambian antes del POST.
        Assert.Equal(EstadoCompatibilidad.Compatible, (await Oportunidades(db).ObtenerDetalleSalidaAsync(7, f.ViajeId, salidaId, f.EnvioId))!.Evaluacion.Estado);
        var envioId = caso == "propio" ? f.EnvioPropioId : f.EnvioId;
        var e = await db.Envios.SingleAsync(e => e.Id == envioId); var s = await db.SalidasViaje.SingleAsync();
        var v = await db.Viajes.Include(v => v.Dias).SingleAsync();
        if (caso == "asignado") { e.ConductorId = 8; e.Estado = EstadoEnvio.ConductorAsignado; }
        if (caso == "inactivo") v.Activo = false;
        if (caso == "pasada") s.FechaHora = DateTimeOffset.UtcNow.AddDays(-1);
        if (caso == "sin_disponibilidad") s.Disponible = false;
        if (caso == "dia") v.Dias.Clear();
        if (caso == "coordenadas") e.RetiroUbicacionId = null;
        if (caso == "horarios") { e.EntregaLimiteConOffset = null; e.RetiroSinRestriccion = false; }
        if (caso == "peso") v.PesoMax = 1;
        if (caso == "cupos") v.CantMaxPaquetes = 0;
        if (caso == "vencimiento") { e.EntregaLimiteConOffset = s.FechaHora.AddHours(-1); e.FechaLimite = e.EntregaLimiteConOffset.Value.DateTime; }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); var version = s.Version;
        var resultado = await Envios(db, new Rutas { Adicional = caso == "desvio" ? 99999 : 100 }).AceptarSalidaAsync(
            caso == "viaje_ajeno" ? 8 : 7, f.ViajeId, salidaId, envioId);
        Assert.Equal(esperado, resultado.Estado);
        Assert.Null((await db.Envios.AsNoTracking().SingleAsync(e => e.Id == envioId)).SalidaViajeId);
        Assert.Equal(version, (await db.SalidasViaje.AsNoTracking().SingleAsync()).Version);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SegundoPaquete_UsaCapacidadComprometida_YNoInventaRecorridoCombinado(bool sinCupo)
    {
        using var f = new Fixture(false, cantidadEnvios: 2); await using var db = f.Db(); var salida = await Preparar(f, db);
        if (sinCupo) { (await db.Viajes.SingleAsync()).CantMaxPaquetes = 1; await db.SaveChangesAsync(); }
        var logica = Envios(db); Assert.True((await logica.AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Exito);
        var segundo = await db.Envios.AsNoTracking().SingleAsync(e => e.UsuarioId == 8 && e.Estado == EstadoEnvio.BuscandoConductor);
        var rutas = new Rutas(); var evaluacion = (await Oportunidades(db, rutas).ObtenerDetalleSalidaAsync(7, f.ViajeId, salida, segundo.Id))!.Evaluacion;
        Assert.Equal(sinCupo ? EstadoCompatibilidad.Incompatible : EstadoCompatibilidad.Indeterminado, evaluacion.Estado);
        var resultado = await Envios(db, rutas).AceptarSalidaAsync(7, f.ViajeId, salida, segundo.Id);
        Assert.Equal(sinCupo ? EstadoAceptacion.CapacidadInsuficiente : EstadoAceptacion.DatosPendientes, resultado.Estado);
        Assert.Empty(rutas.Pedidos);
        Assert.Single(await logica.ListarTransportesAsync(7));
        Assert.Null((await db.Envios.AsNoTracking().SingleAsync(e => e.Id == segundo.Id)).SalidaViajeId);
        if (!sinCupo) Assert.Contains(DatoPendienteOportunidad.RecorridoConCompromisosNoVerificable, resultado.Evaluacion!.DatosPendientes);
    }

    [Fact]
    public async Task FalloAlConfirmar_RevierteInclusoElUpdateDelEnvioYLaVersion_YLimpiaTracker()
    {
        using var f = Compartida(); await using (var inicial = f.Db()) { await Preparar(f, inicial); }
        await using var db = f.DbIndependiente(new FalloCommit()); var s = await db.SalidasViaje.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Envios(db).AceptarSalidaAsync(7, f.ViajeId, s.Id, f.EnvioId));
        Assert.Empty(db.ChangeTracker.Entries());
        await using var verificacion = f.DbIndependiente(); var e = await verificacion.Envios.SingleAsync(e => e.Id == f.EnvioId);
        Assert.Null(e.ConductorId); Assert.Null(e.SalidaViajeId); Assert.Equal(EstadoEnvio.BuscandoConductor, e.Estado);
        Assert.Equal(s.Version, (await verificacion.SalidasViaje.SingleAsync()).Version);
    }

    [Fact]
    public async Task ConflictoOptimista_RevierteYPermiteReintentarEnElMismoContexto()
    {
        using var f = Compartida(); await using (var inicial = f.Db()) { await Preparar(f, inicial); }
        await using var db = f.DbIndependiente(new ConflictoEnvio()); var salida = (await db.SalidasViaje.AsNoTracking().SingleAsync()).Id;
        var logica = Envios(db);
        Assert.Equal(EstadoAceptacion.ConflictoConcurrencia, (await logica.AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Estado);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(EstadoAceptacion.Aceptado, (await logica.AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Estado);
    }

    [Fact]
    public async Task ProveedorFallido_NoAsignaNiConservaVersionFallida()
    {
        using var f = new Fixture(false); await using var db = f.Db(); var salida = await Preparar(f, db);
        var version = (await db.SalidasViaje.AsNoTracking().SingleAsync()).Version;
        await Assert.ThrowsAsync<HttpRequestException>(() => Envios(db, new Rutas { Fallar = true }).AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId));
        Assert.Empty(await Envios(db).ListarTransportesAsync(7));
        Assert.Equal(version, (await db.SalidasViaje.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task Simultaneas_MismaSalida_NoSuperanCuposNiAsignanElSegundoComoSiNoExistieraElPrimero()
    {
        using var f = Compartida(2); int salida, segundo;
        await using (var inicial = f.Db()) {
            salida = await Preparar(f, inicial); (await inicial.Viajes.SingleAsync()).CantMaxPaquetes = 1; await inicial.SaveChangesAsync();
            segundo = await inicial.Envios.Where(e => e.UsuarioId == 8 && e.Estado == EstadoEnvio.BuscandoConductor && e.Id != f.EnvioId).Select(e => e.Id).SingleAsync();
        }
        await using var db1 = f.DbIndependiente(); await using var db2 = f.DbIndependiente();
        var pausa = new RutasEnPausa();
        var primera = Task.Run(() => Envios(db1, pausa).AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId));
        await pausa.Entro.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var segunda = Task.Run(() => Envios(db2).AceptarSalidaAsync(7, f.ViajeId, salida, segundo));
        try { await Task.Delay(150); } finally { pausa.Liberar.TrySetResult(); }
        var resultados = await Task.WhenAll(primera, segunda).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(resultados, r => r.Estado == EstadoAceptacion.Aceptado);
        Assert.Contains(resultados, r => r.Estado is EstadoAceptacion.CapacidadInsuficiente or EstadoAceptacion.ConflictoConcurrencia);
        await using var verificacion = f.DbIndependiente();
        Assert.Equal(1, await verificacion.Envios.CountAsync(e => e.SalidaViajeId == salida));
        Assert.Equal(0, (await new SalidasViajeServicio(verificacion).ObtenerAsync(7, f.ViajeId, salida))!.CuposDisponibles);
    }

    [Fact]
    public async Task DosConductoresSimultaneos_MismoEnvio_SoloUnoPuedeAsignarlo()
    {
        using var f = Compartida(); int salida1, salida2, viaje2;
        await using (var inicial = f.Db()) {
            salida1 = await Preparar(f, inicial);
            var original = await inicial.Viajes.AsNoTracking().Include(v => v.OrigenUbicacion).Include(v => v.DestinoUbicacion).SingleAsync();
            inicial.Usuarios.Add(new Usuario { Id = 9, Nombre = "Otro conductor", Apellido = "Prueba", Email = "otro@test.invalid", PasswordHash = "ficticio" });
            UbicacionConfirmada Copiar(UbicacionConfirmada u) => new() { UsuarioId = 9, TipoRecurso = u.TipoRecurso, Campo = u.Campo,
                ClaveCampo = Guid.NewGuid().ToString("N"), FormularioId = Guid.NewGuid(), DireccionNormalizada = u.DireccionNormalizada,
                IdHere = u.IdHere, PosicionLatitud = u.PosicionLatitud, PosicionLongitud = u.PosicionLongitud,
                NavegacionLatitud = u.NavegacionLatitud, NavegacionLongitud = u.NavegacionLongitud, AccesoElegido = u.AccesoElegido,
                ConfirmadaEnUtc = u.ConfirmadaEnUtc, ConsumidaEnUtc = u.ConsumidaEnUtc, VenceEnUtc = u.VenceEnUtc,
                TokenHash = Guid.NewGuid().ToString("N"), SeleccionHash = Guid.NewGuid().ToString("N") };
            var viaje = new Viaje { UsuarioId = 9, Origen = original.Origen, Destino = original.Destino,
                OrigenUbicacion = Copiar(original.OrigenUbicacion!), DestinoUbicacion = Copiar(original.DestinoUbicacion!),
                Activo = true, PesoMax = 100, CantMaxPaquetes = 2, DesvioMaxKm = 2, HoraSalida = TimeSpan.FromHours(8),
                Dias = new List<DiaViaje> { new() { DiaSemana = 1 } } };
            inicial.Viajes.Add(viaje); await inicial.SaveChangesAsync(); viaje2 = viaje.Id;
            var fecha = (await inicial.SalidasViaje.SingleAsync()).Fecha;
            salida2 = (await new SalidasViajeServicio(inicial).SeleccionarAsync(9, new() { ViajeId = viaje2, Fecha = fecha, ZonaHorariaId = ZonaHorariaViaje.Argentina, LlegadaSinRestriccion = true }))!.Value;
            Assert.Equal(EstadoCompatibilidad.Compatible, (await Oportunidades(inicial).ObtenerDetalleSalidaAsync(9, viaje2, salida2, f.EnvioId))!.Evaluacion.Estado);
        }
        await using var db1 = f.DbIndependiente(); await using var db2 = f.DbIndependiente();
        var pausa = new RutasEnPausa(); var primera = Task.Run(() => Envios(db1, pausa).AceptarSalidaAsync(7, f.ViajeId, salida1, f.EnvioId));
        await pausa.Entro.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var segunda = Task.Run(() => Envios(db2).AceptarSalidaAsync(9, viaje2, salida2, f.EnvioId));
        try { await Task.Delay(150); } finally { pausa.Liberar.TrySetResult(); }
        var resultados = await Task.WhenAll(primera, segunda).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Single(resultados, r => r.Estado == EstadoAceptacion.Aceptado);
        Assert.Contains(resultados, r => r.Estado is EstadoAceptacion.EnvioNoDisponible or EstadoAceptacion.ConflictoConcurrencia);
        await using var verificacion = f.DbIndependiente();
        var envio = await verificacion.Envios.SingleAsync(e => e.Id == f.EnvioId);
        Assert.Equal(7, envio.ConductorId); Assert.Equal(salida1, envio.SalidaViajeId);
        Assert.Equal(EstadoAceptacion.EnvioNoDisponible, (await Envios(db2).AceptarSalidaAsync(9, viaje2, salida2, f.EnvioId)).Estado);
    }

    [Fact]
    public async Task VersionSalida_ImpideGuardarUnaReservaConUnaVersionObsoleta()
    {
        using var f = Compartida(); int salida;
        await using (var inicial = f.Db()) { salida = await Preparar(f, inicial); }
        await using var obsoleto = f.DbIndependiente(); var vieja = await obsoleto.SalidasViaje.SingleAsync();
        await using (var actual = f.DbIndependiente()) { Assert.True((await Envios(actual).AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Exito); }
        vieja.Version = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => obsoleto.SaveChangesAsync());
    }

    [Theory]
    [InlineData("peso", "capacidad suficiente")]
    [InlineData("datos", "datos pendientes")]
    [InlineData("asignado", "ya fue asignado")]
    public async Task Http_PostRevalidaPantallaAnterior_YExplicaElRechazoSinAsignar(string caso, string mensaje)
    {
        using var f = new Fixture(false); await using var db = f.Db(); var salida = await Preparar(f, db);
        var oportunidades = Oportunidades(db); var envios = new EnviosLogica(db, null!, oportunidades: oportunidades);
        await using var app = OportunidadesVistaTest.CrearHost(oportunidades, envios); await app.StartAsync();
        try {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            var html = await cliente.GetStringAsync($"/Oportunidades/Detalle?viajeId={f.ViajeId}&salidaId={salida}&envioId={f.EnvioId}");
            Assert.Contains("Aceptar envío", WebUtility.HtmlDecode(html));
            if (caso == "peso") (await db.Viajes.SingleAsync()).PesoMax = 1;
            var envio = await db.Envios.SingleAsync(e => e.Id == f.EnvioId);
            if (caso == "datos") envio.RetiroUbicacionId = null;
            if (caso == "asignado") { envio.ConductorId = 8; envio.Estado = EstadoEnvio.ConductorAsignado; }
            await db.SaveChangesAsync();
            using var respuesta = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["viajeId"] = f.ViajeId.ToString(), ["salidaId"] = salida.ToString(), ["envioId"] = f.EnvioId.ToString(), ["compatible"] = "true",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value) }));
            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
            Assert.Contains(mensaje, WebUtility.HtmlDecode(await cliente.GetStringAsync(respuesta.Headers.Location)));
            Assert.Empty(await envios.ListarTransportesAsync(7));
            Assert.Null((await db.Envios.AsNoTracking().SingleAsync(e => e.Id == f.EnvioId)).SalidaViajeId);
        } finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task Http_DetalleAceptarTransportes_FlujoCompletoConReintentoYProteccionCsrf()
    {
        using var f = new Fixture(false); await using var db = f.Db(); var salida = await Preparar(f, db);
        var oportunidades = Oportunidades(db); var envios = new EnviosLogica(db, null!, oportunidades: oportunidades);
        await using var app = OportunidadesVistaTest.CrearHost(oportunidades, envios); await app.StartAsync();
        try {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
            var datos = new Dictionary<string, string> { ["viajeId"] = f.ViajeId.ToString(), ["salidaId"] = salida.ToString(), ["envioId"] = f.EnvioId.ToString(), ["conductorId"] = "8", ["compatible"] = "true" };
            using var anonimo = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(datos)); Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
            cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            var html = await cliente.GetStringAsync($"/Oportunidades/Detalle?viajeId={f.ViajeId}&salidaId={salida}&envioId={f.EnvioId}");
            Assert.Contains("Aceptar envío", WebUtility.HtmlDecode(html));
            using var sinToken = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(datos)); Assert.Equal(HttpStatusCode.BadRequest, sinToken.StatusCode);
            datos["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            using var respuesta = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(datos));
            Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode); Assert.Contains("Transportes", respuesta.Headers.Location!.ToString());
            var transportes = WebUtility.HtmlDecode(await cliente.GetStringAsync(respuesta.Headers.Location));
            Assert.Contains("quedó asignado correctamente", transportes); Assert.Contains("Conductor asignado", transportes); Assert.Contains("Marcar retirado", transportes);
            using var repetido = await cliente.PostAsync("/Oportunidades/Aceptar", new FormUrlEncodedContent(datos));
            Assert.Equal(HttpStatusCode.Redirect, repetido.StatusCode);
            Assert.Contains("ya estaba asignado", WebUtility.HtmlDecode(await cliente.GetStringAsync(repetido.Headers.Location)));
            Assert.Single(await envios.ListarTransportesAsync(7)); Assert.Empty(await envios.ListarTransportesAsync(8));
        } finally { await app.StopAsync(); }
    }

    private static Fixture Compartida(int cantidad = 1) => new(false, cantidadEnvios: cantidad,
        conexion: $"Data Source=aceptacion-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=2");
    private sealed class Rutas : IRutasServicio
    {
        public long Adicional = 100; public bool Fallar;
        public List<IReadOnlyList<PuntoRutaDto>> Pedidos = new();
        public Task<ResultadoRutaDto> CalcularAsync(IReadOnlyList<PuntoRutaDto> puntos, CancellationToken cancellationToken = default)
        {
            if (Fallar) throw new HttpRequestException("Error de proveedor simulado");
            Pedidos.Add(puntos); return Task.FromResult(new ResultadoRutaDto { DistanciaMetros = puntos.Count == 2 ? 1000 : 1000 + Adicional, DuracionSegundos = puntos.Count == 2 ? 100 : 120 });
        }
    }
    private sealed class RutasEnPausa : IRutasServicio
    {
        public TaskCompletionSource Entro = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Liberar = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ResultadoRutaDto> CalcularAsync(IReadOnlyList<PuntoRutaDto> puntos, CancellationToken cancellationToken = default)
        {
            Entro.TrySetResult(); await Liberar.Task.WaitAsync(cancellationToken);
            return new() { DistanciaMetros = puntos.Count == 2 ? 1000 : 1100, DuracionSegundos = puntos.Count == 2 ? 100 : 120 };
        }
    }
    private sealed class FalloCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fallo de commit simulado");
    }
    private sealed class ConflictoEnvio : SaveChangesInterceptor
    {
        private bool _fallar = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (_fallar && eventData.Context!.ChangeTracker.Entries<Envio>().Any(e => e.State == EntityState.Modified)) {
                _fallar = false; throw new DbUpdateConcurrencyException("RowVersion cambió (simulado)");
            }
            return ValueTask.FromResult(result);
        }
    }
}
