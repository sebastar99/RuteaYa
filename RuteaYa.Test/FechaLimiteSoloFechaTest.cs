using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;
using Fixture = RuteaYa.Test.CompatibilidadPersistidaTest.Fixture;

namespace RuteaYa.Test;

/// <summary>
/// Escenario real de las pruebas manuales: salida un lunes a la mañana y envío publicado solo con fecha límite
/// (sin horario). La fecha sola significa "hasta el final de ese día" únicamente cuando la zona argentina se
/// puede determinar desde las direcciones confirmadas; nunca se fuerza la compatibilidad.
/// </summary>
public class FechaLimiteSoloFechaTest
{
    private const string Retiro = "Retiro, Morón, Argentina", Entrega = "Entrega, Luján, Argentina";

    private static Fixture Argentina() => new(false, retiroDir: Retiro, entregaDir: Entrega);

    private sealed class Rutas(long duracionConEnvio = 1200) : IRutasServicio
    {
        public Task<ResultadoRutaDto> CalcularAsync(IReadOnlyList<PuntoRutaDto> puntos, CancellationToken ct = default) =>
            Task.FromResult(new ResultadoRutaDto { DistanciaMetros = puntos.Count == 2 ? 10000 : 11000, DuracionSegundos = puntos.Count == 2 ? 1000 : duracionConEnvio });
    }

    private static OportunidadesLogica Oportunidades(RuteaYaDbContext db, long duracion = 1200) => new(db,
        new EvaluacionOportunidadServicio(new DatosConfirmadosOportunidadPersistidosServicio(db),
            new CompatibilidadServicio(new DesviosServicio(new RutasConsultaServicio(new Rutas(duracion))))));

    private static async Task<(int Salida, DateOnly Fecha)> Preparar(Fixture f, RuteaYaDbContext db, int diasLimite)
    {
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(2));
        while (fecha.DayOfWeek != DayOfWeek.Monday) fecha = fecha.AddDays(1);
        var salida = (await new SalidasViajeServicio(db).SeleccionarAsync(7, new() { ViajeId = f.ViajeId, Fecha = fecha,
            ZonaHorariaId = ZonaHorariaViaje.Argentina, LlegadaSinRestriccion = true }))!.Value;
        var e = await db.Envios.SingleAsync(x => x.Id == f.EnvioId);
        e.FechaLimite = fecha.AddDays(diasLimite).ToDateTime(TimeOnly.MinValue);
        e.EntregaLimiteConOffset = null; e.RetiroDisponibleDesde = null; e.RetiroSinRestriccion = true;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return (salida, fecha);
    }

    [Fact]
    public async Task FechaSola_EnElMismoDiaDeLaSalida_EsCompatibleHastaElFinDelDia()
    {
        using var f = Argentina(); await using var db = f.Db();
        var (salida, _) = await Preparar(f, db, diasLimite: 0);
        var detalle = (await Oportunidades(db).ObtenerDetalleSalidaAsync(7, f.ViajeId, salida, f.EnvioId))!;
        Assert.Equal(EstadoCompatibilidad.Compatible, detalle.Evaluacion.Estado);
        Assert.Empty(detalle.Evaluacion.DatosPendientes);
        var listado = (await Oportunidades(db).ConsultarSalidaAsync(7, f.ViajeId, salida))!;
        Assert.Equal(EstadoCompatibilidad.Compatible, listado.Evaluaciones[f.EnvioId].Estado);
        var desvio = detalle.Evaluacion.ResultadoMotor!.InsercionSeleccionada!.Desvio;
        Assert.Equal(1000, desvio.DistanciaAdicionalMetros); Assert.Equal(200, desvio.TiempoAdicionalSegundos);
    }

    [Fact]
    public async Task FechaSola_SeInterpretaConElOffsetArgentinoAutomatico()
    {
        using var f = Argentina(); await using var db = f.Db();
        var (salida, fecha) = await Preparar(f, db, diasLimite: 0);
        var datos = await new DatosConfirmadosOportunidadPersistidosServicio(db).ObtenerAsync(
            new ViajeOportunidadesDto { Id = f.ViajeId, UsuarioId = 7, Origen = "Origen", Destino = "Destino", Activo = true, DesvioMaxKm = 2,
                SalidaSeleccionada = await new SalidasViajeServicio(db).ObtenerAsync(7, f.ViajeId, salida) },
            new EnvioDto { Id = f.EnvioId, UsuarioId = 8, OrigenDir = Retiro, DestinoDir = Entrega, PesoKg = 2.5m, FechaLimite = fecha.ToDateTime(TimeOnly.MinValue) });
        var limite = datos!.FechaLimiteEntrega;
        Assert.Equal(EstadoRestriccionTemporal.Informada, limite.Estado);
        Assert.Equal(new DateTimeOffset(fecha.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.FromHours(-3)), limite.Valor);
    }

    [Fact]
    public async Task FechaSola_SinZonaDeterminable_SigueIndeterminadaYNoInventaUnaZona()
    {
        using var f = new Fixture(false, retiroDir: "Retiro sin país", entregaDir: Entrega); await using var db = f.Db();
        var (salida, _) = await Preparar(f, db, diasLimite: 0);
        var detalle = (await Oportunidades(db).ObtenerDetalleSalidaAsync(7, f.ViajeId, salida, f.EnvioId))!;
        Assert.Equal(EstadoCompatibilidad.Indeterminado, detalle.Evaluacion.Estado);
        Assert.Contains(DatoPendienteOportunidad.VencimientoEntregaNoInterpretado, detalle.Evaluacion.DatosPendientes);
    }

    [Fact]
    public async Task FechaSola_AnteriorALaSalida_EsIncompatibleYNoSeFuerza()
    {
        using var f = Argentina(); await using var db = f.Db();
        var (salida, _) = await Preparar(f, db, diasLimite: -3);
        var detalle = (await Oportunidades(db).ObtenerDetalleSalidaAsync(7, f.ViajeId, salida, f.EnvioId))!;
        Assert.Equal(EstadoCompatibilidad.Incompatible, detalle.Evaluacion.Estado);
        Assert.Contains(MotivoCompatibilidad.SalidaPosteriorAlVencimiento, detalle.Evaluacion.ResultadoMotor!.Motivos);
    }

    [Fact]
    public async Task FechaSola_ConRecorridoQueTerminaDespuesDelDia_NoSeApruebaSinVerificar()
    {
        using var f = Argentina(); await using var db = f.Db();
        var (salida, _) = await Preparar(f, db, diasLimite: 0);
        var detalle = (await Oportunidades(db, duracion: 20 * 3600).ObtenerDetalleSalidaAsync(7, f.ViajeId, salida, f.EnvioId))!;
        Assert.NotEqual(EstadoCompatibilidad.Compatible, detalle.Evaluacion.Estado);
        Assert.Contains(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable, detalle.Evaluacion.ResultadoMotor!.Motivos);
    }

    [Fact]
    public async Task FechaSola_Compatible_PermiteAceptarYDescuentaLaCapacidadRestante()
    {
        using var f = Argentina(); await using var db = f.Db();
        var (salida, _) = await Preparar(f, db, diasLimite: 0);
        var oportunidades = Oportunidades(db);
        var antes = (await new SalidasViajeServicio(db).ObtenerAsync(7, f.ViajeId, salida))!;
        var envios = new EnviosLogica(db, null!, oportunidades: oportunidades);
        Assert.Equal(EstadoAceptacion.Aceptado, (await envios.AceptarSalidaAsync(7, f.ViajeId, salida, f.EnvioId)).Estado);
        Assert.Equal(f.EnvioId, Assert.Single(await envios.ListarTransportesAsync(7)).Id);
        var despues = (await new SalidasViajeServicio(db).ObtenerAsync(7, f.ViajeId, salida))!;
        Assert.Equal(antes.CuposDisponibles - 1, despues.CuposDisponibles);
        Assert.Equal(antes.PesoDisponibleKg - 2.5m, despues.PesoDisponibleKg);
    }
}
