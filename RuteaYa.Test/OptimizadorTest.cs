using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using Fixture = RuteaYa.Test.CompatibilidadPersistidaTest.Fixture;

namespace RuteaYa.Test;

public class OptimizadorTest
{
    private static PuntoRutaDto P(decimal lat, decimal lng = -58) => new() { Latitud = lat, Longitud = lng };
    private static PaqueteRecorridoDto Paquete(int id, decimal retiro, decimal entrega,
        DateTimeOffset? limite = null, RestriccionTemporalDto? desde = null) => new()
        {
            EnvioId = id, Descripcion = "P" + id, OrigenDir = "R" + id, DestinoDir = "E" + id,
            Retiro = P(retiro), Entrega = P(entrega),
            DisponibleDesde = desde ?? RestriccionTemporalDto.SinRestriccion(),
            FechaLimiteEntrega = RestriccionTemporalDto.Informada(limite ?? new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero))
        };

    private sealed class Rutas(Func<IReadOnlyList<PuntoRutaDto>, ResultadoRutaDto> f) : IRutasServicio
    {
        public List<IReadOnlyList<PuntoRutaDto>> Pedidos = new(); public bool Fallar;
        public Task<ResultadoRutaDto> CalcularAsync(IReadOnlyList<PuntoRutaDto> p, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Fallar) throw new HttpRequestException("Proveedor de rutas caído");
            Pedidos.Add(p);
            return Task.FromResult(f(p));
        }
    }

    private static ContextoRecorridoDto Contexto(decimal desvioKm = 5, DateTimeOffset? salida = null,
        RestriccionTemporalDto? llegada = null, params decimal[] lats) => new()
        {
            Base = (lats.Length > 0 ? lats : new[] { -34.6m, -35m }).Select(l => P(l)).ToArray(),
            EtiquetasBase = (lats.Length > 0 ? lats : new[] { -34.6m, -35m }).Select(l => "p" + l).ToArray(),
            DesvioMaxKm = desvioKm, Salida = salida ?? new DateTimeOffset(2027, 1, 4, 8, 0, 0, TimeSpan.FromHours(-3)),
            LlegadaMaxima = llegada ?? RestriccionTemporalDto.SinRestriccion()
        };

    private static async Task<ContextoRecorridoDto> ConBase(IRecorridoCombinadoServicio s, ContextoRecorridoDto c, PresupuestoRutas b)
    { Assert.True(await s.PrepararBaseAsync(c, b)); return c; }

    // ---------- Recorrido combinado ----------

    [Fact]
    public async Task Recorrido_RetiroAntesDeEntrega_YParadasOriginalesConservadas()
    {
        var rutas = new Rutas(p => new() { DistanciaMetros = 10000, DuracionSegundos = 1000 });
        var s = new RecorridoCombinadoServicio(rutas);
        var c = await ConBase(s, Contexto(desvioKm: 500, lats: new[] { -34.6m, -34.7m, -35m }), new PresupuestoRutas());
        var r = await s.EvaluarAsync(c, new[] { Paquete(1, -34.61m, -34.69m), Paquete(2, -34.71m, -34.99m) }, new PresupuestoRutas());
        Assert.Equal(EstadoCompatibilidad.Compatible, r.Estado);
        var tipos = r.Orden.Select(o => o.Tipo).ToList();
        Assert.Equal("Origen", tipos.First()); Assert.Equal("Destino", tipos.Last());
        // La parada original queda entre origen y destino en el orden del viaje.
        Assert.True(tipos.IndexOf("Parada") < tipos.Count - 1);
        foreach (var id in new[] { 1, 2 })
        {
            var retiro = r.Orden.ToList().FindIndex(o => o.EnvioId == id && o.Tipo == "Retiro");
            var entrega = r.Orden.ToList().FindIndex(o => o.EnvioId == id && o.Tipo == "Entrega");
            Assert.True(retiro >= 0 && entrega > retiro, $"El paquete {id} no respeta retiro antes de entrega.");
        }
        // La ruta consultada conserva la secuencia base: ningún punto original aparece dos veces.
        Assert.Equal(3, r.Orden.Count(o => o.Tipo is "Origen" or "Destino" or "Parada"));
    }

    [Fact]
    public async Task Recorrido_DesvioCombinado_NoEsSumaDeDesviosIndividuales()
    {
        // El recorrido compartido cuesta menos que la suma de insertar cada paquete por separado.
        // El tramo compartido abarata el recorrido combinado: 4 puntos -> 11600, 6 puntos -> 12000.
        var rutas = new Rutas(p => new() { DistanciaMetros = p.Count == 2 ? 10000 : p.Count == 4 ? 11600 : 12000, DuracionSegundos = 1000 });
        var s = new RecorridoCombinadoServicio(rutas);
        var c = await ConBase(s, Contexto(desvioKm: 500), new PresupuestoRutas());
        var paqA = Paquete(1, -34.61m, -34.9m); var paqB = Paquete(2, -34.62m, -34.95m);
        var solo = await s.EvaluarAsync(c, new[] { paqA }, new PresupuestoRutas());
        var ambos = await s.EvaluarAsync(c, new[] { paqA, paqB }, new PresupuestoRutas());
        Assert.Equal(EstadoCompatibilidad.Compatible, ambos.Estado);
        Assert.NotEqual(ambos.DesvioMetros, solo.DesvioMetros * 2);
        Assert.Equal(10000, ambos.DistanciaOriginalMetros); // siempre contra la ruta original
    }

    [Fact]
    public async Task Recorrido_Poda_SinConsultarAlProveedor()
    {
        var rutas = new Rutas(p => new() { DistanciaMetros = 10000, DuracionSegundos = 1000 });
        var s = new RecorridoCombinadoServicio(rutas);
        var c = await ConBase(s, Contexto(desvioKm: 1), new PresupuestoRutas());
        var lejano = Paquete(1, 0m, 1m); // a miles de km: ni en línea recta entra
        Assert.True(s.ImposibleProbado(c, new[] { lejano }));
        var antes = rutas.Pedidos.Count;
        var r = await s.EvaluarAsync(c, new[] { lejano }, new PresupuestoRutas());
        Assert.Equal(EstadoCompatibilidad.Incompatible, r.Estado);
        Assert.True(r.ImposibleProbado);
        Assert.Equal(antes, rutas.Pedidos.Count); // ningún pedido extra al proveedor
    }

    [Fact]
    public async Task Recorrido_DesvioExcedido_Incompatible()
    {
        var rutas = new Rutas(p => new() { DistanciaMetros = p.Count == 2 ? 10000 : 10000 + 61000, DuracionSegundos = 1000 });
        var s = new RecorridoCombinadoServicio(rutas);
        var c = await ConBase(s, Contexto(desvioKm: 60), new PresupuestoRutas()); // cota recta no descarta; la ruta real excede
        var r = await s.EvaluarAsync(c, new[] { Paquete(1, -34.7m, -34.8m) }, new PresupuestoRutas());
        Assert.Equal(EstadoCompatibilidad.Incompatible, r.Estado);
        Assert.Contains(MotivoCompatibilidad.DesvioExcedido, r.Motivos);
    }

    [Fact]
    public async Task Recorrido_Horarios_RechazaFechaLimiteYLlegada()
    {
        var rutas = new Rutas(p => new() { DistanciaMetros = 10000, DuracionSegundos = 7200 });
        var s = new RecorridoCombinadoServicio(rutas);
        var salida = new DateTimeOffset(2027, 1, 4, 8, 0, 0, TimeSpan.FromHours(-3));
        var c = await ConBase(s, Contexto(desvioKm: 500, salida: salida, llegada: RestriccionTemporalDto.SinRestriccion()), new PresupuestoRutas());
        var venceAntes = Paquete(1, -34.7m, -34.8m, limite: salida.AddHours(1));
        var r = await s.EvaluarAsync(c, new[] { venceAntes }, new PresupuestoRutas());
        // El sistema no afirma compatibilidad: no puede verificar que la entrega llegue antes del vencimiento.
        Assert.Equal(EstadoCompatibilidad.Indeterminado, r.Estado);
        Assert.Contains(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable, r.Motivos);

        var c2 = await ConBase(s, Contexto(desvioKm: 500, salida: salida, llegada: RestriccionTemporalDto.Informada(salida.AddHours(1))), new PresupuestoRutas());
        var r2 = await s.EvaluarAsync(c2, new[] { Paquete(1, -34.7m, -34.8m) }, new PresupuestoRutas());
        Assert.Equal(EstadoCompatibilidad.Incompatible, r2.Estado);
        Assert.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida, r2.Motivos);
    }

    [Fact]
    public async Task Recorrido_PresupuestoAgotado_BusquedaIncompleta()
    {
        var rutas = new Rutas(p => new() { DistanciaMetros = 10000, DuracionSegundos = 1000 });
        var s = new RecorridoCombinadoServicio(rutas);
        var presupuesto = new PresupuestoRutas(maxSolicitudes: 1); // solo alcanza para la base
        var c = await ConBase(s, Contexto(desvioKm: 500), presupuesto);
        var r = await s.EvaluarAsync(c, new[] { Paquete(1, -34.7m, -34.8m) }, presupuesto);
        Assert.False(r.Completo);
        Assert.NotEqual(EstadoCompatibilidad.Compatible, r.Estado);
    }

    // ---------- Optimizador con EF (SQLite) ----------

    private sealed class Caso
    {
        public int SalidaId = 0;
        public readonly List<int> Envios = new();
    }

    private static async Task<Caso> Preparar(Fixture f, RuteaYaDbContext db, (decimal Monto, decimal Peso, string Desc, decimal LatR, decimal LatE)[] paquetes)
    {
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2));
        while (fecha.DayOfWeek != DayOfWeek.Monday) fecha = fecha.AddDays(1);
        var salida = (await new SalidasViajeServicio(db).SeleccionarAsync(7, new()
        { ViajeId = f.ViajeId, Fecha = fecha, ZonaHorariaId = ZonaHorariaViaje.Argentina, LlegadaSinRestriccion = true }))!.Value;
        var caso = new Caso { SalidaId = salida };
        var disponibles = await db.Envios.Include(e => e.RetiroUbicacion).Include(e => e.EntregaUbicacion)
            .Where(e => e.Estado == EstadoEnvio.BuscandoConductor && e.UsuarioId == 8).OrderBy(e => e.Id).ToListAsync();
        for (var i = 0; i < paquetes.Length && i < disponibles.Count; i++)
        {
            var e = disponibles[i]; var (monto, peso, desc, latR, latE) = paquetes[i];
            e.Monto = monto; e.PesoKg = peso; e.Descripcion = desc;
            e.FechaLimite = fecha.AddDays(14).ToDateTime(new TimeOnly(18, 0));
            e.EntregaLimiteConOffset = new DateTimeOffset(e.FechaLimite, TimeSpan.FromHours(-3));
            e.RetiroSinRestriccion = true;
            var retiroId = e.RetiroUbicacion!.Id; var entregaId = e.EntregaUbicacion!.Id;
            // Las ubicaciones son snapshots inmutables: el ajuste de fixture se hace fuera de EF.
            await db.Database.ExecuteSqlAsync(
                $"UPDATE UbicacionesConfirmadas SET NavegacionLatitud = {latR}, PosicionLatitud = {latR} WHERE Id = '{retiroId}'");
            await db.Database.ExecuteSqlAsync(
                $"UPDATE UbicacionesConfirmadas SET NavegacionLatitud = {latE}, PosicionLatitud = {latE} WHERE Id = '{entregaId}'");
            caso.Envios.Add(e.Id);
        }
        var viaje = await db.Viajes.SingleAsync(v => v.Id == f.ViajeId);
        viaje.DesvioMaxKm = 50;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return caso;
    }

    private static OptimizadorServicio Optimizador(RuteaYaDbContext db, IRutasServicio rutas) =>
        new(db, new DatosConfirmadosOportunidadPersistidosServicio(db), new RecorridoCombinadoServicio(rutas));

    private static EnviosLogica Envios(RuteaYaDbContext db, IRutasServicio rutas) => new(db, null!,
        oportunidades: new OportunidadesLogica(db, new EvaluacionOportunidadServicio(
            new DatosConfirmadosOportunidadPersistidosServicio(db),
            new CompatibilidadServicio(new DesviosServicio(rutas)), new RecorridoCombinadoServicio(rutas))),
        optimizador: Optimizador(db, rutas));

    private static Rutas RutasOk() => new(p => new() { DistanciaMetros = 10000 + (p.Count - 2) * 400, DuracionSegundos = 1000 + (p.Count - 2) * 60 });

    [Fact]
    public async Task Optimizador_GreedyIncorrecto_EligeCombinacionDeMayorGanancia()
    {
        // Ejemplo de la consigna: A $10.000/10kg, B y C $6.000/5kg, 2 cupos y 10 kg -> B + C ($12.000).
        using var f = new Fixture(false, cantidadEnvios: 3, pesoMax: 10); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (10000m, 10m, "A", -34.61m, -34.91m), (6000m, 5m, "B", -34.62m, -34.92m), (6000m, 5m, "C", -34.63m, -34.93m) });
        var r = await Optimizador(db, RutasOk()).OptimizarAsync(7, f.ViajeId, caso.SalidaId);
        Assert.True(r!.Recomendada != null, $"bloqueo={r.Bloqueo} aviso={r.Aviso} cand={r.CandidatosDisponibles}/{r.CandidatosEvaluados} desc=[{string.Join("|", r.Descartados.Select(d => d.EnvioId + ":" + d.Motivo))}]");
        Assert.Equal(12000, r.Recomendada.GananciaTotal);
        Assert.Equal(new[] { caso.Envios[1], caso.Envios[2] }.OrderBy(x => x), r.Recomendada.Paquetes.Select(p => p.EnvioId).OrderBy(x => x));
        Assert.DoesNotContain(r.Recomendada.Paquetes, p => p.EnvioId == caso.Envios[0]);
    }

    [Fact]
    public async Task Optimizador_Empate_EligeMenorDesvio()
    {
        using var f = new Fixture(false, cantidadEnvios: 2); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (5000m, 3m, "A", -34.61m, -34.91m), (5000m, 3m, "B", -34.62m, -34.92m) });
        var viaje = await db.Viajes.SingleAsync(v => v.Id == f.ViajeId); viaje.CantMaxPaquetes = 1; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        // B entra con 400 m de desvío; A con 1400 m. Misma ganancia -> gana B.
        var rutas = new Rutas(p => new()
        {
            DistanciaMetros = p.Count == 2 ? 10000 : p.Any(x => x.Latitud == -34.61m || x.Latitud == -34.91m) ? 11400 : 10400,
            DuracionSegundos = 1000
        });
        var r = await Optimizador(db, rutas).OptimizarAsync(7, f.ViajeId, caso.SalidaId);
        Assert.NotNull(r!.Recomendada);
        Assert.Equal(caso.Envios[1], r.Recomendada.Paquetes.Single().EnvioId);
        Assert.Equal(400, r.Recomendada.DesvioMetros);
    }

    [Fact]
    public async Task Optimizador_Limites_PesoYCupos()
    {
        using var f = new Fixture(false, cantidadEnvios: 3, pesoMax: 10); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (9000m, 60m, "Pesado", -34.61m, -34.91m), (6000m, 5m, "B", -34.62m, -34.92m), (6000m, 5m, "C", -34.63m, -34.93m) });
        var viaje = await db.Viajes.SingleAsync(v => v.Id == f.ViajeId); viaje.PesoMax = 10; viaje.CantMaxPaquetes = 1; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var r = await Optimizador(db, RutasOk()).OptimizarAsync(7, f.ViajeId, caso.SalidaId);
        Assert.NotNull(r!.Recomendada);
        var paquete = Assert.Single(r.Recomendada.Paquetes); // un solo cupo
        Assert.NotEqual(caso.Envios[0], paquete.EnvioId);  // el pesado queda descartado
        Assert.Contains(r.Descartados, d => d.EnvioId == caso.Envios[0] && d.Motivo.Contains("peso"));
    }

    [Fact]
    public async Task Optimizador_SinCandidatos_NoInventaCombinacion()
    {
        using var f = new Fixture(false, cantidadEnvios: 0); await using var db = f.Db();
        var caso = await Preparar(f, db, Array.Empty<(decimal, decimal, string, decimal, decimal)>());
        var r = await Optimizador(db, RutasOk()).OptimizarAsync(7, f.ViajeId, caso.SalidaId);
        Assert.NotNull(r); Assert.Null(r.Recomendada); Assert.Equal(0, r.CandidatosDisponibles);
    }

    [Fact]
    public async Task Optimizador_ViajeAjeno_NoFiltraDatos()
    {
        using var f = new Fixture(false); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (100m, 1m, "X", -34.61m, -34.91m) });
        Assert.Null(await Optimizador(db, RutasOk()).OptimizarAsync(8, f.ViajeId, caso.SalidaId)); // el dueño es 7
        Assert.Null(await Optimizador(db, RutasOk()).OptimizarAsync(7, 9999, caso.SalidaId));
    }

    [Fact]
    public async Task Optimizador_EnvioPropioNoEsCandidato_YAsignadosQuedanFuera()
    {
        using var f = new Fixture(false, cantidadEnvios: 1); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (100m, 1m, "Ajeno", -34.61m, -34.91m) });
        var r = await Optimizador(db, RutasOk()).OptimizarAsync(7, f.ViajeId, caso.SalidaId);
        // Solo el envío ajeno cuenta como candidato; el propio y el entregado no.
        Assert.Equal(1, r!.CandidatosDisponibles);
        Assert.Equal(caso.Envios[0], r.Recomendada!.Paquetes.Single().EnvioId);
        Assert.DoesNotContain(r.Descartados, d => d.EnvioId == f.EnvioPropioId);
    }

    [Fact]
    public async Task Optimizador_CompromisoPrevio_SumaSinDuplicarGanancia()
    {
        using var f = new Fixture(false, cantidadEnvios: 2); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (4000m, 2m, "Tomado", -34.61m, -34.91m), (3000m, 2m, "Nuevo", -34.62m, -34.92m) });
        var tomado = await db.Envios.SingleAsync(e => e.Id == caso.Envios[0]);
        tomado.ConductorId = 7; tomado.SalidaViajeId = caso.SalidaId; tomado.Estado = EstadoEnvio.ConductorAsignado;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var r = await Optimizador(db, RutasOk()).OptimizarAsync(7, f.ViajeId, caso.SalidaId);
        Assert.NotNull(r!.Recomendada);
        Assert.Equal(3000, r.Recomendada.GananciaTotal);          // solo la ganancia nueva
        Assert.Equal(caso.Envios[1], r.Recomendada.Paquetes.Single().EnvioId);
        Assert.Equal(1, r.CuposComprometidos);
        Assert.Contains(r.Recomendada.Orden, o => o.EnvioId == caso.Envios[0]); // el compromiso sigue en el recorrido
    }

    [Fact]
    public async Task Optimizador_ErrorProveedor_PropagaFalla()
    {
        using var f = new Fixture(false, cantidadEnvios: 1); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (100m, 1m, "X", -34.61m, -34.91m) });
        var rutas = RutasOk(); rutas.Fallar = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Optimizador(db, rutas).OptimizarAsync(7, f.ViajeId, caso.SalidaId));
    }

    // ---------- Reserva de la combinación ----------

    [Fact]
    public async Task Reserva_ConfirmarVariosPaquetes_EnUnaTransaccion()
    {
        using var f = new Fixture(false, cantidadEnvios: 2); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (4000m, 2m, "A", -34.61m, -34.91m), (3000m, 2m, "B", -34.62m, -34.92m) });
        var logica = Envios(db, RutasOk());
        var eval = await Optimizador(db, RutasOk()).EvaluarSeleccionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios);
        Assert.NotNull(eval); Assert.True(eval!.Valida, eval.Motivo);
        var r = await logica.ReservarCombinacionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios, eval.Huella);
        Assert.Equal(EstadoReserva.Reservado, r.Estado);
        var guardados = await db.Envios.AsNoTracking().Where(e => caso.Envios.Contains(e.Id)).ToListAsync();
        Assert.All(guardados, e =>
        {
            Assert.Equal(7, e.ConductorId); Assert.Equal(caso.SalidaId, e.SalidaViajeId); Assert.Equal(EstadoEnvio.ConductorAsignado, e.Estado);
        });
        // Reintento idempotente: no duplica ni retrocede.
        Assert.Equal(EstadoReserva.YaReservado, (await Envios(db, RutasOk()).ReservarCombinacionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios, null)).Estado);
        var transportes = await logica.ListarTransportesAsync(7);
        Assert.Equal(2, transportes.Count(t => caso.Envios.Contains(t.Id)));
    }

    [Fact]
    public async Task Reserva_Conflicto_RevierteTodo()
    {
        using var f = new Fixture(false, cantidadEnvios: 2); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (4000m, 2m, "A", -34.61m, -34.91m), (3000m, 2m, "B", -34.62m, -34.92m) });
        var eval = await Optimizador(db, RutasOk()).EvaluarSeleccionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios);
        Assert.True(eval!.Valida);
        // Otro conductor toma el envío B entre el cálculo y la confirmación.
        await db.Database.ExecuteSqlAsync($"UPDATE Envios SET ConductorId = 8, Estado = 1 WHERE Id = {caso.Envios[1]}");
        db.ChangeTracker.Clear();
        var r = await Envios(db, RutasOk()).ReservarCombinacionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios, eval.Huella);
        Assert.Equal(EstadoReserva.EnvioNoDisponible, r.Estado);
        Assert.Contains(caso.Envios[1], r.EnviosEnConflicto);
        var a = await db.Envios.AsNoTracking().SingleAsync(e => e.Id == caso.Envios[0]);
        Assert.Null(a.ConductorId); Assert.Null(a.SalidaViajeId); Assert.Equal(EstadoEnvio.BuscandoConductor, a.Estado);
    }

    [Fact]
    public async Task Reserva_HuellaObsoleta_PideRecalcular()
    {
        using var f = new Fixture(false, cantidadEnvios: 1); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (4000m, 2m, "A", -34.61m, -34.91m) });
        var eval = await Optimizador(db, RutasOk()).EvaluarSeleccionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios);
        Assert.True(eval!.Valida);
        var envio = await db.Envios.SingleAsync(e => e.Id == caso.Envios[0]);
        envio.Monto = 5000; envio.RowVersion = new byte[] { 9, 9 }; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var r = await Envios(db, RutasOk()).ReservarCombinacionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios, eval.Huella);
        Assert.Equal(EstadoReserva.Cambio, r.Estado);
        Assert.Equal(EstadoEnvio.BuscandoConductor, (await db.Envios.AsNoTracking().SingleAsync(e => e.Id == caso.Envios[0])).Estado);
    }

    [Fact]
    public async Task Reserva_SinHuella_IgualRevalidaEnServidor()
    {
        using var f = new Fixture(false, cantidadEnvios: 1); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (4000m, 2m, "A", -34.61m, -34.91m) });
        var r = await Envios(db, RutasOk()).ReservarCombinacionAsync(7, f.ViajeId, caso.SalidaId, caso.Envios, null);
        Assert.Equal(EstadoReserva.Reservado, r.Estado);
    }

    [Fact]
    public async Task SegundoPaquete_AceptacionIndividual_EvaluaRecorridoCombinado()
    {
        using var f = new Fixture(false, cantidadEnvios: 2); await using var db = f.Db();
        var caso = await Preparar(f, db, new[] { (4000m, 2m, "A", -34.61m, -34.91m), (3000m, 2m, "B", -34.62m, -34.92m) });
        var tomado = await db.Envios.SingleAsync(e => e.Id == caso.Envios[0]);
        tomado.ConductorId = 7; tomado.SalidaViajeId = caso.SalidaId; tomado.Estado = EstadoEnvio.ConductorAsignado;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        // Antes: salida con compromisos -> pendiente. Ahora: el segundo paquete se evalúa en el recorrido combinado.
        var evaluacion = new EvaluacionOportunidadServicio(new DatosConfirmadosOportunidadPersistidosServicio(db),
            new CompatibilidadServicio(new DesviosServicio(RutasOk())), new RecorridoCombinadoServicio(RutasOk()));
        var viaje = await new OportunidadesLogica(db, evaluacion).ObtenerDetalleSalidaAsync(7, f.ViajeId, caso.SalidaId, caso.Envios[1]);
        Assert.NotNull(viaje);
        Assert.Equal(EstadoCompatibilidad.Compatible, viaje!.Evaluacion.Estado);
    }
}
