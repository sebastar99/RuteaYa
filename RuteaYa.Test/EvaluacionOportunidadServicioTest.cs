using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using static RuteaYa.Logica.DTOs.RestriccionTemporalDto;

namespace RuteaYa.Test;

public class EvaluacionOportunidadServicioTest
{
    private static PuntoRutaDto Punto(decimal valor) => new() { Latitud = valor, Longitud = valor };
    private static DateTimeOffset Salida() => new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static ViajeOportunidadesDto Viaje() => new() { Id = 10, Activo = true, DesvioMaxKm = 2 };
    private static EnvioDto Envio() => new()
    {
        Id = 25, Estado = EstadoEnvio.BuscandoConductor, PesoKg = 2.5m,
        OrigenDir = "Dirección publicada", DestinoDir = "Entrega publicada",
        FechaLimite = new DateTime(2026, 11, 15, 18, 0, 0, DateTimeKind.Utc)
    };
    private static DatosConfirmadosOportunidadDto Datos() => new()
    {
        ViajeId = 10, EnvioId = 25,
        RecorridoConfirmado = new[] { Punto(1), Punto(2), Punto(3) },
        RetiroConfirmado = Punto(4), EntregaConfirmada = Punto(5), SalidaProgramada = Salida(),
        Disponible = true, PesoDisponibleKg = 10, CuposDisponibles = 2,
        LlegadaMaxima = SinRestriccion(), DisponibleDesde = SinRestriccion(),
        FechaLimiteEntrega = Informada(new DateTimeOffset(Envio().FechaLimite))
    };
    private static ResultadoCompatibilidadDto Resultado() => new()
    {
        ViajeId = 10, EnvioId = 25, Estado = EstadoCompatibilidad.Compatible,
        Motivos = new[] { MotivoCompatibilidad.DesvioAprobado, MotivoCompatibilidad.HorarioAprobado }
    };

    private static Mock<IDatosConfirmadosOportunidadServicio> Fuente(DatosConfirmadosOportunidadDto? datos)
    {
        var fuente = new Mock<IDatosConfirmadosOportunidadServicio>(MockBehavior.Strict);
        fuente.Setup(f => f.ObtenerAsync(It.IsAny<ViajeOportunidadesDto>(), It.IsAny<EnvioDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(datos);
        return fuente;
    }

    [Fact]
    public async Task DatosActuales_DireccionesYFechaSinZona_NoInventaConfirmacionesNiCapacidad()
    {
        var envio = Envio();
        envio.FechaLimite = DateTime.SpecifyKind(envio.FechaLimite, DateTimeKind.Unspecified);
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);
        var servicio = new EvaluacionOportunidadServicio(new DatosConfirmadosOportunidadActualesServicio(), motor.Object);

        var resultado = await servicio.EvaluarAsync(Viaje(), envio);

        Assert.False(resultado.MotorEjecutado);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(10, resultado.DatosPendientes.Count);
        Assert.Contains(DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas, resultado.DatosPendientes);
        Assert.Contains(DatoPendienteOportunidad.PesoDisponibleDesconocido, resultado.DatosPendientes);
        Assert.Contains(DatoPendienteOportunidad.VencimientoEntregaNoInterpretado, resultado.DatosPendientes);
        Assert.Null(resultado.ResultadoMotor);
        motor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified, EstadoRestriccionTemporal.Desconocida)]
    [InlineData(DateTimeKind.Local, EstadoRestriccionTemporal.Desconocida)]
    [InlineData(DateTimeKind.Utc, EstadoRestriccionTemporal.Informada)]
    public async Task FuenteActual_SoloUtcExplicito_ConservaInstanteSinElegirZonaServidor(
        DateTimeKind kind, EstadoRestriccionTemporal estado)
    {
        var envio = Envio();
        envio.FechaLimite = DateTime.SpecifyKind(envio.FechaLimite, kind);

        var datos = await new DatosConfirmadosOportunidadActualesServicio().ObtenerAsync(Viaje(), envio);

        Assert.Equal(estado, datos!.FechaLimiteEntrega.Estado);
        Assert.Null(datos.RecorridoConfirmado);
        Assert.Null(datos.RetiroConfirmado);
        Assert.Null(datos.EntregaConfirmada);
        Assert.Null(datos.PesoDisponibleKg);
        Assert.Null(datos.CuposDisponibles);
        Assert.Null(datos.Disponible);
        if (estado == EstadoRestriccionTemporal.Informada)
            Assert.Equal(new DateTimeOffset(envio.FechaLimite), datos.FechaLimiteEntrega.Valor);
        else Assert.Null(datos.FechaLimiteEntrega.Valor);
    }

    [Theory]
    [InlineData("recorrido", DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas)]
    [InlineData("retiro", DatoPendienteOportunidad.RetiroSinCoordenadasConfirmadas)]
    [InlineData("entrega", DatoPendienteOportunidad.EntregaSinCoordenadasConfirmadas)]
    [InlineData("salida", DatoPendienteOportunidad.SalidaConcretaDesconocida)]
    [InlineData("disponible", DatoPendienteOportunidad.DisponibilidadSalidaDesconocida)]
    [InlineData("peso", DatoPendienteOportunidad.PesoDisponibleDesconocido)]
    [InlineData("cupos", DatoPendienteOportunidad.CuposDisponiblesDesconocidos)]
    [InlineData("llegada", DatoPendienteOportunidad.LlegadaMaximaDesconocida)]
    [InlineData("desde", DatoPendienteOportunidad.InicioRetiroDesconocido)]
    [InlineData("vencimiento", DatoPendienteOportunidad.VencimientoEntregaNoInterpretado)]
    public async Task EvaluarAsync_UnRequisitoFaltante_ImpideTodaConsultaAlMotor(string campo, DatoPendienteOportunidad motivo)
    {
        var datos = Datos();
        switch (campo)
        {
            case "recorrido": datos.RecorridoConfirmado = null; break;
            case "retiro": datos.RetiroConfirmado = null; break;
            case "entrega": datos.EntregaConfirmada = null; break;
            case "salida": datos.SalidaProgramada = null; break;
            case "disponible": datos.Disponible = null; break;
            case "peso": datos.PesoDisponibleKg = null; break;
            case "cupos": datos.CuposDisponibles = null; break;
            case "llegada": datos.LlegadaMaxima = Desconocida(); break;
            case "desde": datos.DisponibleDesde = Desconocida(); break;
            case "vencimiento": datos.FechaLimiteEntrega = Desconocida(); break;
        }
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor.Object)
            .EvaluarAsync(Viaje(), Envio());

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Contains(motivo, resultado.DatosPendientes);
        Assert.False(resultado.MotorEjecutado);
        motor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("recorrido", DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas)]
    [InlineData("retiro", DatoPendienteOportunidad.RetiroSinCoordenadasConfirmadas)]
    [InlineData("entrega", DatoPendienteOportunidad.EntregaSinCoordenadasConfirmadas)]
    [InlineData("capacidad", DatoPendienteOportunidad.CapacidadRestanteInvalida)]
    [InlineData("pesoEnvio", DatoPendienteOportunidad.PesoEnvioInvalido)]
    [InlineData("desvio", DatoPendienteOportunidad.DesvioMaximoInvalido)]
    [InlineData("fechaPublicada", DatoPendienteOportunidad.VencimientoEntregaNoInterpretado)]
    [InlineData("salida", DatoPendienteOportunidad.SalidaConcretaDesconocida)]
    public async Task EvaluarAsync_DatosInvalidos_NoLosEnviaComoConfirmadosAlMotor(string campo, DatoPendienteOportunidad motivo)
    {
        var datos = Datos();
        var viaje = Viaje();
        var envio = Envio();
        switch (campo)
        {
            case "recorrido": datos.RecorridoConfirmado = new[] { Punto(1) }; break;
            case "retiro": datos.RetiroConfirmado = Punto(91); break;
            case "entrega": datos.EntregaConfirmada = new PuntoRutaDto { Latitud = 0, Longitud = 181 }; break;
            case "capacidad": datos.PesoDisponibleKg = -1; break;
            case "pesoEnvio": envio.PesoKg = 0; break;
            case "desvio": viaje.DesvioMaxKm = -1; break;
            case "fechaPublicada": envio.FechaLimite = default; break;
            case "salida": datos.SalidaProgramada = default(DateTimeOffset); break;
        }
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor.Object).EvaluarAsync(viaje, envio);

        Assert.Contains(motivo, resultado.DatosPendientes);
        Assert.False(resultado.MotorEjecutado);
        motor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("llegada")]
    [InlineData("retiro")]
    [InlineData("vencimientoUtc")]
    public async Task EvaluarAsync_FechasInconsistentes_QuedanPendientesSinInventarCorrecciones(string caso)
    {
        var datos = Datos();
        if (caso == "llegada") datos.LlegadaMaxima = Informada(Salida().AddSeconds(-1));
        if (caso == "retiro") datos.DisponibleDesde = Informada(datos.FechaLimiteEntrega.Valor!.Value.AddSeconds(1));
        if (caso == "vencimientoUtc") datos.FechaLimiteEntrega = Informada(Salida().AddHours(1));
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor.Object).EvaluarAsync(Viaje(), Envio());

        Assert.Contains(DatoPendienteOportunidad.RestriccionesTemporalesInconsistentes, resultado.DatosPendientes);
        motor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluarAsync_VencimientoObligatorio_NoSeDescartaComoSinRestriccion()
    {
        var datos = Datos();
        datos.FechaLimiteEntrega = SinRestriccion();
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor.Object).EvaluarAsync(Viaje(), Envio());

        Assert.Contains(DatoPendienteOportunidad.VencimientoEntregaNoInterpretado, resultado.DatosPendientes);
        motor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluarAsync_DatosCompletos_MapeaValoresRealesYHaceUnaSolaEvaluacion()
    {
        var viaje = Viaje();
        var envio = Envio();
        var datos = Datos();
        var fuente = Fuente(datos);
        using var cancelacion = new CancellationTokenSource();
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);
        motor.Setup(m => m.EvaluarAsync(It.IsAny<ViajeCompatibilidadDto>(), It.IsAny<EnvioCompatibilidadDto>(), cancelacion.Token))
            .Callback((ViajeCompatibilidadDto v, EnvioCompatibilidadDto e, CancellationToken _) =>
            {
                Assert.Equal(viaje.Id, v.ViajeId);
                Assert.Equal(viaje.DesvioMaxKm, v.DesvioMaxKm);
                Assert.True(v.Activo);
                Assert.True(v.Disponible);
                Assert.Equal(10m, v.PesoDisponibleKg);
                Assert.Equal(2, v.CuposDisponibles);
                Assert.Equal(Salida(), v.SalidaProgramada);
                Assert.Equal(new[] { 1m, 2m, 3m }, v.RecorridoBase.Select(p => p.Latitud));
                Assert.NotSame(datos.RecorridoConfirmado![0], v.RecorridoBase[0]);
                Assert.NotSame(datos.RetiroConfirmado, e.Retiro);
                Assert.Equal(envio.PesoKg, e.PesoKg);
                Assert.Equal(envio.Estado, e.Estado);
                Assert.Equal(4m, e.Retiro.Latitud);
                Assert.Equal(5m, e.Entrega.Latitud);
                Assert.Equal(datos.FechaLimiteEntrega.Valor, e.FechaLimiteEntrega.Valor);
            }).ReturnsAsync(Resultado());

        var resultado = await new EvaluacionOportunidadServicio(fuente.Object, motor.Object)
            .EvaluarAsync(viaje, envio, cancelacion.Token);

        Assert.True(resultado.MotorEjecutado);
        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        Assert.Empty(resultado.DatosPendientes);
        motor.Verify(m => m.EvaluarAsync(It.IsAny<ViajeCompatibilidadDto>(), It.IsAny<EnvioCompatibilidadDto>(), cancelacion.Token), Times.Once);
        fuente.Verify(f => f.ObtenerAsync(viaje, envio, cancelacion.Token), Times.Once);
    }

    [Theory]
    [InlineData(false, 10, 2, MotivoCompatibilidad.ViajeNoDisponible)]
    [InlineData(true, 0, 2, MotivoCompatibilidad.PesoExcedido)]
    [InlineData(true, 10, 0, MotivoCompatibilidad.SinCupo)]
    public async Task EvaluarAsync_FalsoOCeroConfirmados_NoLosConfundeConDesconocidos(
        bool disponible, int peso, int cupos, MotivoCompatibilidad motivo)
    {
        var datos = Datos();
        datos.Disponible = disponible;
        datos.PesoDisponibleKg = peso;
        datos.CuposDisponibles = cupos;
        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var motor = new CompatibilidadServicio(desvios.Object);

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor).EvaluarAsync(Viaje(), Envio());

        Assert.True(resultado.MotorEjecutado);
        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(motivo, resultado.ResultadoMotor!.Motivos);
        desvios.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluarAsync_CeroCeroConfirmado_EsUnaCoordenadaValida()
    {
        var datos = Datos();
        datos.RetiroConfirmado = Punto(0);
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);
        motor.Setup(m => m.EvaluarAsync(It.IsAny<ViajeCompatibilidadDto>(),
            It.Is<EnvioCompatibilidadDto>(e => e.Retiro.Latitud == 0 && e.Retiro.Longitud == 0), default)).ReturnsAsync(Resultado());

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor.Object).EvaluarAsync(Viaje(), Envio());

        Assert.True(resultado.MotorEjecutado);
    }

    [Fact]
    public async Task EvaluarAsync_IntegracionEnMemoria_UsaMotorYDesviosRealesSinHttpNiSql()
    {
        var datos = Datos();
        datos.RecorridoConfirmado = new[] { Punto(1), Punto(3) };
        var rutas = new RutasMockServicio();
        rutas.ConfigurarRecorrido(datos.RecorridoConfirmado, new ResultadoRutaDto { DistanciaMetros = 10000, DuracionSegundos = 600 });
        rutas.ConfigurarRecorrido(new[] { Punto(1), Punto(4), Punto(5), Punto(3) },
            new ResultadoRutaDto { DistanciaMetros = 10500, DuracionSegundos = 660 });
        var motor = new CompatibilidadServicio(new DesviosServicio(rutas));

        var resultado = await new EvaluacionOportunidadServicio(Fuente(datos).Object, motor).EvaluarAsync(Viaje(), Envio());

        Assert.True(resultado.MotorEjecutado);
        Assert.Equal(EstadoCompatibilidad.Compatible, resultado.Estado);
        var elegida = Assert.IsType<InsercionCompatibilidadDto>(resultado.ResultadoMotor!.InsercionSeleccionada);
        Assert.Equal(500L, elegida.Desvio.DistanciaAdicionalMetros);
        Assert.Equal(60L, elegida.Desvio.TiempoAdicionalSegundos);
        Assert.Equal(0, elegida.SegmentoRetiro);
        Assert.Equal(0, elegida.SegmentoEntrega);
    }

    [Fact]
    public async Task EvaluarAsync_SinFuente_NoDeclaraCompatibleNiLlamaMotor()
    {
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);

        var resultado = await new EvaluacionOportunidadServicio(Fuente(null).Object, motor.Object).EvaluarAsync(Viaje(), Envio());

        Assert.Contains(DatoPendienteOportunidad.FuenteDeDatosNoDisponible, resultado.DatosPendientes);
        Assert.False(resultado.MotorEjecutado);
        motor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluarAsync_ErrorDeFuenteOMotor_SePropagaSinResultadoFicticio(bool fallaMotor)
    {
        var fuente = Fuente(Datos());
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);
        var error = new HttpRequestException("Fallo simulado.");
        if (fallaMotor)
            motor.Setup(m => m.EvaluarAsync(It.IsAny<ViajeCompatibilidadDto>(), It.IsAny<EnvioCompatibilidadDto>(), default)).ThrowsAsync(error);
        else
            fuente.Setup(f => f.ObtenerAsync(It.IsAny<ViajeOportunidadesDto>(), It.IsAny<EnvioDto>(), default)).ThrowsAsync(error);

        var recibido = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new EvaluacionOportunidadServicio(fuente.Object, motor.Object).EvaluarAsync(Viaje(), Envio()));

        Assert.Same(error, recibido);
        if (!fallaMotor) motor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluarAsync_ContextoOResultadoDeOtroCandidato_NoSePublica(bool resultadoAjeno)
    {
        var datos = Datos();
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);
        if (resultadoAjeno)
        {
            var resultado = Resultado();
            resultado.EnvioId = 99;
            motor.Setup(m => m.EvaluarAsync(It.IsAny<ViajeCompatibilidadDto>(), It.IsAny<EnvioCompatibilidadDto>(), default)).ReturnsAsync(resultado);
        }
        else datos.ViajeId = 99;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EvaluacionOportunidadServicio(Fuente(datos).Object, motor.Object).EvaluarAsync(Viaje(), Envio()));

        if (!resultadoAjeno) motor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluarAsync_CancelacionAntesDeEmpezar_NoConsultaNingunComponente()
    {
        var fuente = new Mock<IDatosConfirmadosOportunidadServicio>(MockBehavior.Strict);
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EvaluacionOportunidadServicio(fuente.Object, motor.Object).EvaluarAsync(Viaje(), Envio(), new CancellationToken(true)));

        fuente.VerifyNoOtherCalls();
        motor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluarAsync_CancelacionEnFuenteOMotor_NoPublicaResultado(bool enMotor)
    {
        using var cancelacion = new CancellationTokenSource();
        var fuente = Fuente(Datos());
        var motor = new Mock<ICompatibilidadServicio>(MockBehavior.Strict);
        if (enMotor)
            motor.Setup(m => m.EvaluarAsync(It.IsAny<ViajeCompatibilidadDto>(), It.IsAny<EnvioCompatibilidadDto>(), cancelacion.Token))
                .Returns(() => { cancelacion.Cancel(); return Task.FromResult(Resultado()); });
        else
            fuente.Setup(f => f.ObtenerAsync(It.IsAny<ViajeOportunidadesDto>(), It.IsAny<EnvioDto>(), cancelacion.Token))
                .Returns(() => { cancelacion.Cancel(); return Task.FromResult<DatosConfirmadosOportunidadDto?>(Datos()); });

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EvaluacionOportunidadServicio(fuente.Object, motor.Object).EvaluarAsync(Viaje(), Envio(), cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        if (!enMotor) motor.VerifyNoOtherCalls();
    }

    [Fact]
    public void PayloadConFaltantes_NuncaSeMuestraComoCompatibleAunqueIncluyaResultado()
    {
        var evaluacion = new EvaluacionOportunidadDto
        {
            DatosPendientes = new[] { DatoPendienteOportunidad.PesoDisponibleDesconocido }, ResultadoMotor = Resultado()
        };

        Assert.Equal(EstadoCompatibilidad.Indeterminado, evaluacion.Estado);
        Assert.True(evaluacion.MotorEjecutado);
    }
}
