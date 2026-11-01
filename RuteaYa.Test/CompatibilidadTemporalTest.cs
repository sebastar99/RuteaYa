using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class CompatibilidadTemporalTest
{
    private static DateTimeOffset Salida() => new(2026, 11, 2, 8, 0, 0, TimeSpan.FromHours(-3));
    private static PuntoRutaDto Punto(int valor) => new() { Latitud = valor, Longitud = valor };

    private static ViajeCompatibilidadDto Viaje(int paradas = 0) => new()
    {
        ViajeId = 1, Activo = true, Disponible = true,
        RecorridoBase = Enumerable.Range(1, paradas + 2).Select(Punto).ToArray(),
        PesoDisponibleKg = 10, CuposDisponibles = 2, DesvioMaxKm = 2,
        SalidaProgramada = Salida()
    };

    private static EnvioCompatibilidadDto Envio() => new()
    {
        EnvioId = 2, Estado = EstadoEnvio.BuscandoConductor,
        Retiro = Punto(10), Entrega = Punto(11), PesoKg = 1
    };

    private static ResultadoDesvioDto Desvio(long metros = 500, long duracionTotal = 1800) => new()
    {
        DistanciaOriginalMetros = 10000,
        DuracionOriginalSegundos = 600,
        DistanciaConEnvioMetros = 10000 + metros,
        DuracionConEnvioSegundos = duracionTotal,
        DistanciaAdicionalMetros = metros,
        TiempoAdicionalSegundos = duracionTotal - 600,
        CumpleLimiteDesvio = metros <= 2000
    };

    private static (CompatibilidadServicio Servicio, Mock<IDesviosServicio> Desvios,
        Mock<IEvaluacionDesvios> Evaluacion) Motor(
        ViajeCompatibilidadDto viaje, EnvioCompatibilidadDto envio,
        Func<int, int, ResultadoDesvioDto>? calcular = null, CancellationToken token = default)
    {
        var evaluacion = new Mock<IEvaluacionDesvios>(MockBehavior.Strict);
        evaluacion.Setup(e => e.CalcularAsync(It.IsAny<int>(), It.IsAny<int>(), token))
            .Returns((int retiro, int entrega, CancellationToken _) =>
                Task.FromResult(calcular == null ? Desvio() : calcular(retiro, entrega)));
        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        desvios.Setup(d => d.PrepararAsync(viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, token))
            .ReturnsAsync(evaluacion.Object);
        return (new CompatibilidadServicio(desvios.Object), desvios, evaluacion);
    }

    [Fact]
    public async Task EvaluarAsync_SinSalida_NoInventaFinNiHorariosDeParada()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.SalidaProgramada = null;
        envio.DisponibleDesde = Salida();
        envio.FechaLimiteEntrega = Salida().AddHours(1);
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Null(resultado.InsercionSeleccionada!.FinalizacionEstimada);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.InsercionSeleccionada.EstadoTemporal);
        Assert.Contains(MotivoCompatibilidad.SalidaNoInformada, resultado.Motivos);
        Assert.DoesNotContain(MotivoCompatibilidad.HorarioAprobado, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_SalidaConRestriccionesOpcionalesAusentes_Compatible()
    {
        var viaje = Viaje();
        var envio = Envio();
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(Salida().AddMinutes(30), resultado.InsercionSeleccionada!.FinalizacionEstimada);
        Assert.Equal(new[] { MotivoCompatibilidad.DesvioAprobado, MotivoCompatibilidad.HorarioAprobado },
            resultado.Motivos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluarAsync_SalidaPosteriorAlVencimiento_RechazaSinRutasInclusoConCapacidadDesconocida(
        bool capacidadDesconocida)
    {
        var viaje = Viaje();
        var envio = Envio();
        envio.FechaLimiteEntrega = Salida().AddSeconds(-1);
        if (capacidadDesconocida)
        {
            viaje.PesoDisponibleKg = null;
            viaje.CuposDisponibles = null;
        }
        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);

        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.SalidaPosteriorAlVencimiento, resultado.Motivos);
        Assert.Null(resultado.InsercionSeleccionada);
        desvios.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(29, EstadoCompatibilidad.Incompatible)]
    [InlineData(30, EstadoCompatibilidad.Compatible)]
    [InlineData(31, EstadoCompatibilidad.Compatible)]
    public async Task EvaluarAsync_LlegadaMaxima_UsaLimiteInclusivo(int minutos, EstadoCompatibilidad esperado)
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(minutos);
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(esperado, resultado.Estado);
        Assert.Equal(esperado == EstadoCompatibilidad.Incompatible,
            resultado.Motivos.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida));
    }

    [Theory]
    [InlineData(0, EstadoCompatibilidad.Indeterminado)]
    [InlineData(15, EstadoCompatibilidad.Indeterminado)]
    [InlineData(30, EstadoCompatibilidad.Compatible)]
    [InlineData(45, EstadoCompatibilidad.Compatible)]
    public async Task EvaluarAsync_FinRespectoAlVencimiento_NoConfundeDestinoFinalConEntrega(
        int minutos, EstadoCompatibilidad esperado)
    {
        var viaje = Viaje();
        var envio = Envio();
        envio.FechaLimiteEntrega = Salida().AddMinutes(minutos);
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(esperado, resultado.Estado);
        Assert.Equal(esperado == EstadoCompatibilidad.Indeterminado,
            resultado.Motivos.Contains(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable));
        Assert.NotNull(resultado.InsercionSeleccionada);
    }

    [Theory]
    [InlineData(-1, EstadoCompatibilidad.Compatible)]
    [InlineData(0, EstadoCompatibilidad.Compatible)]
    [InlineData(15, EstadoCompatibilidad.Indeterminado)]
    [InlineData(30, EstadoCompatibilidad.Indeterminado)]
    [InlineData(31, EstadoCompatibilidad.Incompatible)]
    public async Task EvaluarAsync_DisponibilidadRetiro_NoInventaSuInstante(
        int minutos, EstadoCompatibilidad esperado)
    {
        var viaje = Viaje();
        var envio = Envio();
        envio.DisponibleDesde = Salida().AddMinutes(minutos);
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(esperado, resultado.Estado);
        if (esperado == EstadoCompatibilidad.Indeterminado)
            Assert.Contains(MotivoCompatibilidad.HorarioRetiroNoVerificable, resultado.Motivos);
        if (esperado == EstadoCompatibilidad.Incompatible)
        {
            Assert.Contains(MotivoCompatibilidad.DisponibilidadPosteriorAlRecorrido, resultado.Motivos);
            Assert.Null(resultado.InsercionSeleccionada);
        }
    }

    [Fact]
    public async Task EvaluarAsync_RechazoTemporalYRestriccionesIndeterminadas_PriorizaRechazo()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(25);
        envio.DisponibleDesde = Salida().AddMinutes(10);
        envio.FechaLimiteEntrega = Salida().AddMinutes(20);
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.HorarioRetiroNoVerificable, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_MismosInstantesConDistintosOffsets_CompatibleSinZonaFija()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(30).ToOffset(TimeSpan.FromHours(2));
        envio.DisponibleDesde = Salida().ToOffset(TimeSpan.FromHours(4));
        envio.FechaLimiteEntrega = Salida().AddMinutes(30).ToUniversalTime();
        var (servicio, _, _) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(envio.FechaLimiteEntrega, resultado.InsercionSeleccionada!.FinalizacionEstimada);
        Assert.Equal(Salida().Offset,
            resultado.InsercionSeleccionada.FinalizacionEstimada!.Value.Offset);
    }

    [Theory]
    [InlineData(10, 2, 9, EstadoCompatibilidad.Compatible)]
    [InlineData(8, -3, 10, EstadoCompatibilidad.Incompatible)]
    public async Task EvaluarAsync_OffsetsDistintos_ComparaInstantesYNoHorasLocales(
        int horaSalida, int offsetSalida, int horaLimiteUtc, EstadoCompatibilidad esperado)
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.SalidaProgramada = new DateTimeOffset(2026, 11, 2, horaSalida, 0, 0, TimeSpan.FromHours(offsetSalida));
        envio.FechaLimiteEntrega = new DateTimeOffset(2026, 11, 2, horaLimiteUtc, 0, 0, TimeSpan.Zero);
        var (servicio, _, evaluacion) = Motor(viaje, envio);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(esperado, resultado.Estado);
        if (esperado == EstadoCompatibilidad.Incompatible)
            evaluacion.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluarAsync_FinCalculadoConDuracionTotal_NoUsaTiempoAdicional()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(25);
        // Duración total 30 minutos, tiempo adicional solo 20 minutos.
        var (servicio, _, _) = Motor(viaje, envio, (_, _) => Desvio(duracionTotal: 1800));

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_DuracionCeroConLimitesEnSalida_PermiteIgualdades()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.LlegadaMaxima = Salida();
        envio.DisponibleDesde = Salida();
        envio.FechaLimiteEntrega = Salida();
        var (servicio, _, _) = Motor(viaje, envio, (_, _) => Desvio(duracionTotal: 0));

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(Salida(), resultado.InsercionSeleccionada!.FinalizacionEstimada);
    }

    [Fact]
    public async Task EvaluarAsync_InsercionMasCortaLlegaTarde_SeleccionaOtraCompatible()
    {
        var viaje = Viaje(1);
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(30);
        var (servicio, desvios, evaluacion) = Motor(viaje, envio, (retiro, entrega) => (retiro, entrega) switch
        {
            (0, 0) => Desvio(100, 3600),
            (0, 1) => Desvio(500, 1200),
            _ => Desvio(300, 2400)
        });

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(0, resultado.InsercionSeleccionada!.SegmentoRetiro);
        Assert.Equal(1, resultado.InsercionSeleccionada.SegmentoEntrega);
        Assert.Equal(Salida().AddMinutes(20), resultado.InsercionSeleccionada.FinalizacionEstimada);
        desvios.Verify(d => d.PrepararAsync(viaje.RecorridoBase, envio.Retiro, envio.Entrega,
            viaje.DesvioMaxKm, default), Times.Once);
        evaluacion.Verify(e => e.CalcularAsync(It.IsAny<int>(), It.IsAny<int>(), default), Times.Exactly(3));
    }

    [Fact]
    public async Task EvaluarAsync_CompatibleMasLarga_PriorizaSobreProvisionalMasCorta()
    {
        var viaje = Viaje(1);
        var envio = Envio();
        envio.FechaLimiteEntrega = Salida().AddMinutes(30);
        var (servicio, _, _) = Motor(viaje, envio, (retiro, entrega) => (retiro, entrega) switch
        {
            (0, 0) => Desvio(100, 2400),
            (0, 1) => Desvio(500, 1200),
            _ => Desvio(300, 2700)
        });

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(500L, resultado.InsercionSeleccionada!.Desvio.DistanciaAdicionalMetros);
        Assert.DoesNotContain(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_ProvisionalesEIncompatibles_ConservaMejorProvisional()
    {
        var viaje = Viaje(1);
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(45);
        envio.FechaLimiteEntrega = Salida().AddMinutes(15);
        var (servicio, _, _) = Motor(viaje, envio, (retiro, entrega) => (retiro, entrega) switch
        {
            (0, 0) => Desvio(100, 3600),
            (0, 1) => Desvio(800, 1800),
            _ => Desvio(700, 2400)
        });

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(1, resultado.InsercionSeleccionada!.SegmentoRetiro);
        Assert.Equal(1, resultado.InsercionSeleccionada.SegmentoEntrega);
        Assert.Equal(Salida().AddMinutes(40), resultado.InsercionSeleccionada.FinalizacionEstimada);
        Assert.Contains(MotivoCompatibilidad.EntregaAntesDelVencimientoNoVerificable, resultado.Motivos);
        Assert.DoesNotContain(MotivoCompatibilidad.LlegadaMaximaExcedida, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_TodasIncompatibles_AgrupaMotivosTemporalesSinDuplicar()
    {
        var viaje = Viaje(1);
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(40);
        envio.DisponibleDesde = Salida().AddMinutes(30);
        var (servicio, _, _) = Motor(viaje, envio, (retiro, entrega) => (retiro, entrega) switch
        {
            (0, 0) => Desvio(100, 1200),
            (0, 1) => Desvio(500, 3600),
            _ => Desvio(300, 1500)
        });

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Null(resultado.InsercionSeleccionada);
        Assert.Contains(MotivoCompatibilidad.DesvioAprobado, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.DisponibilidadPosteriorAlRecorrido, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida, resultado.Motivos);
        Assert.Equal(resultado.Motivos.Count, resultado.Motivos.Distinct().Count());
    }

    [Fact]
    public async Task EvaluarAsync_AlternativaFueraDeDesvio_NoPuedeGanarPorHorario()
    {
        var viaje = Viaje(1);
        var envio = Envio();
        viaje.LlegadaMaxima = Salida().AddMinutes(30);
        var (servicio, _, _) = Motor(viaje, envio, (retiro, entrega) => (retiro, entrega) switch
        {
            (0, 0) => Desvio(3000, 60),
            (0, 1) => Desvio(100, 3600),
            _ => Desvio(200, 1200)
        });

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(1, resultado.InsercionSeleccionada!.SegmentoRetiro);
        Assert.Equal(1, resultado.InsercionSeleccionada.SegmentoEntrega);
    }

    [Theory]
    [MemberData(nameof(CompatibilidadServicioTest.Selecciones), MemberType = typeof(CompatibilidadServicioTest))]
    public async Task EvaluarAsync_CategoriaCompatible_ConservaDesempatesYDiferenciasNegativas(
        long[] metros, long[] segundosAdicionales, int retiroEsperado, int entregaEsperada)
    {
        var viaje = Viaje(2);
        var envio = Envio();
        var parejas = new[] { (0, 0), (0, 1), (0, 2), (1, 1), (1, 2), (2, 2) };
        var respuestas = parejas.Select((p, i) => (p, valor: Desvio(metros[i], 1200 + segundosAdicionales[i])))
            .ToDictionary(x => x.p, x => x.valor);
        var (servicio, _, _) = Motor(viaje, envio, (i, j) => respuestas[(i, j)]);

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Equal(retiroEsperado, resultado.InsercionSeleccionada!.SegmentoRetiro);
        Assert.Equal(entregaEsperada, resultado.InsercionSeleccionada.SegmentoEntrega);
        Assert.Same(respuestas[(retiroEsperado, entregaEsperada)], resultado.InsercionSeleccionada.Desvio);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    public async Task EvaluarAsync_ErrorDespuesDeCandidataCompatible_NoDevuelveResultadoParcial(string tipo)
    {
        var viaje = Viaje(1);
        var envio = Envio();
        Exception esperado = tipo == "http" ? new HttpRequestException("Error simulado.") : new TimeoutException("Timeout simulado.");
        var llamadas = 0;
        var (servicio, _, _) = Motor(viaje, envio, (_, _) =>
        {
            if (++llamadas == 2) throw esperado;
            return Desvio();
        });

        var error = await Record.ExceptionAsync(() => servicio.EvaluarAsync(viaje, envio));

        Assert.Same(esperado, error);
        Assert.Equal(2, llamadas);
    }

    [Fact]
    public async Task EvaluarAsync_CancelacionConCandidataCompatible_NoPublicaResultado()
    {
        var viaje = Viaje(1);
        var envio = Envio();
        using var cancelacion = new CancellationTokenSource();
        var llamadas = 0;
        var (servicio, _, _) = Motor(viaje, envio, (_, _) =>
        {
            if (++llamadas == 2) cancelacion.Cancel();
            return Desvio();
        }, cancelacion.Token);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => servicio.EvaluarAsync(viaje, envio, cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Equal(2, llamadas);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task EvaluarAsync_DuracionNoUtilizable_PropagaErrorTecnico(long segundos)
    {
        var viaje = Viaje();
        var envio = Envio();
        var (servicio, _, _) = Motor(viaje, envio, (_, _) => Desvio(duracionTotal: segundos));

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EvaluarAsync(viaje, envio));
    }

    [Fact]
    public async Task EvaluarAsync_FinalizacionFueraDeRango_NoDesbordaNiDeclaraIncompatibilidad()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.SalidaProgramada = DateTimeOffset.MaxValue.AddSeconds(-1);
        var (servicio, _, _) = Motor(viaje, envio, (_, _) => Desvio(duracionTotal: 2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.EvaluarAsync(viaje, envio));
    }
}
