using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using static RuteaYa.Logica.DTOs.RestriccionTemporalDto;

namespace RuteaYa.Test;

public class CompatibilidadServicioTest
{
    private static PuntoRutaDto Punto(decimal latitud = -34.65m, decimal longitud = -58.62m) => new()
    {
        Latitud = latitud,
        Longitud = longitud
    };

    private static ViajeCompatibilidadDto Viaje() => new()
    {
        ViajeId = 10,
        RecorridoBase = new[] { Punto(), Punto(-34.56m, -59.11m) },
        Activo = true,
        Disponible = true,
        DesvioMaxKm = 2,
        PesoDisponibleKg = 10,
        CuposDisponibles = 1,
        LlegadaMaxima = SinRestriccion()
    };

    private static EnvioCompatibilidadDto Envio() => new()
    {
        EnvioId = 20,
        Retiro = Punto(-34.66m, -58.63m),
        Entrega = Punto(-34.57m, -59.12m),
        PesoKg = 2,
        Estado = EstadoEnvio.BuscandoConductor,
        DisponibleDesde = SinRestriccion(),
        FechaLimiteEntrega = SinRestriccion()
    };

    private static ResultadoDesvioDto Desvio(long metros = 0, long segundos = 0, decimal limiteKm = 2) => new()
    {
        DistanciaOriginalMetros = 20000,
        DuracionOriginalSegundos = 1200,
        DistanciaConEnvioMetros = 20000 + metros,
        DuracionConEnvioSegundos = 1200 + segundos,
        DistanciaAdicionalMetros = metros,
        TiempoAdicionalSegundos = segundos,
        CumpleLimiteDesvio = metros <= limiteKm * 1000m
    };

    private static CompatibilidadServicio Servicio()
    {
        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var evaluacion = new Mock<IEvaluacionDesvios>(MockBehavior.Strict);
        evaluacion.Setup(e => e.CalcularAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Desvio());
        desvios.Setup(d => d.PrepararAsync(
                It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<PuntoRutaDto>(), It.IsAny<PuntoRutaDto>(),
                It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluacion.Object);
        return new CompatibilidadServicio(desvios.Object);
    }

    private static ViajeCompatibilidadDto ViajeConParadas(int cantidad)
    {
        var viaje = Viaje();
        var extremos = viaje.RecorridoBase;
        var puntos = new List<PuntoRutaDto> { extremos[0] };
        for (var i = 0; i < cantidad; i++)
            puntos.Add(Punto(-34.64m + i * 0.01m, -58.70m - i * 0.01m));
        puntos.Add(extremos[1]);
        viaje.RecorridoBase = puntos;
        return viaje;
    }

    [Fact]
    public async Task EvaluarAsync_ReglasPreviasYDesvioCumplidos_IndeterminadoConHorarioPendiente()
    {
        ICompatibilidadServicio servicio = Servicio();

        var resultado = await servicio.EvaluarAsync(Viaje(), Envio());

        Assert.Equal(10, resultado.ViajeId);
        Assert.Equal(20, resultado.EnvioId);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(new[]
        {
            MotivoCompatibilidad.DesvioAprobado,
            MotivoCompatibilidad.SalidaNoInformada,
            MotivoCompatibilidad.HorarioNoEvaluado
        }, resultado.Motivos);
        Assert.NotNull(resultado.InsercionSeleccionada);
    }

    [Theory]
    [InlineData(1, EstadoCompatibilidad.Indeterminado)]
    [InlineData(10, EstadoCompatibilidad.Indeterminado)]
    [InlineData(11, EstadoCompatibilidad.Incompatible)]
    public async Task EvaluarAsync_PesoContraCapacidad_PermiteIgualdadExacta(
        int pesoKg, EstadoCompatibilidad esperado)
    {
        var envio = Envio();
        envio.PesoKg = pesoKg;

        var resultado = await Servicio().EvaluarAsync(Viaje(), envio);

        Assert.Equal(esperado, resultado.Estado);
        Assert.Equal(pesoKg > 10, resultado.Motivos.Contains(MotivoCompatibilidad.PesoExcedido));
        Assert.NotEqual(EstadoCompatibilidad.Compatible, resultado.Estado);
    }

    [Fact]
    public async Task EvaluarAsync_PesoDecimalIgualAlDisponible_NoLoRechazaPorRedondeo()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.PesoDisponibleKg = 2.75m;
        envio.PesoKg = 2.75m;

        var resultado = await Servicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.DoesNotContain(MotivoCompatibilidad.PesoExcedido, resultado.Motivos);
    }

    [Theory]
    [InlineData(EstadoEnvio.ConductorAsignado)]
    [InlineData(EstadoEnvio.Retirado)]
    [InlineData(EstadoEnvio.EnCamino)]
    [InlineData(EstadoEnvio.Entregado)]
    [InlineData(EstadoEnvio.Cancelado)]
    public async Task EvaluarAsync_EnvioNoBuscandoConductor_Incompatible(EstadoEnvio estado)
    {
        var envio = Envio();
        envio.Estado = estado;

        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(Viaje(), envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.EnvioNoDisponible, resultado.Motivos);
        Assert.Null(resultado.InsercionSeleccionada);
        desvios.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("inactivo", MotivoCompatibilidad.ViajeInactivo)]
    [InlineData("noDisponible", MotivoCompatibilidad.ViajeNoDisponible)]
    [InlineData("sinPeso", MotivoCompatibilidad.PesoExcedido)]
    [InlineData("sinCupos", MotivoCompatibilidad.SinCupo)]
    public async Task EvaluarAsync_RechazoPrevio_Incompatible(string caso, MotivoCompatibilidad motivo)
    {
        var viaje = Viaje();
        switch (caso)
        {
            case "inactivo": viaje.Activo = false; break;
            case "noDisponible": viaje.Disponible = false; break;
            case "sinPeso": viaje.PesoDisponibleKg = 0; break;
            case "sinCupos": viaje.CuposDisponibles = 0; break;
        }

        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, Envio());

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(motivo, resultado.Motivos);
        Assert.Null(resultado.InsercionSeleccionada);
        desvios.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task EvaluarAsync_CapacidadDesconocida_NoLaSuponeDisponibleNiAgotada(
        bool pesoDesconocido, bool cuposDesconocidos)
    {
        var viaje = Viaje();
        if (pesoDesconocido) viaje.PesoDisponibleKg = null;
        if (cuposDesconocidos) viaje.CuposDisponibles = null;

        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, Envio());

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(pesoDesconocido,
            resultado.Motivos.Contains(MotivoCompatibilidad.PesoDisponibleDesconocido));
        Assert.Equal(cuposDesconocidos,
            resultado.Motivos.Contains(MotivoCompatibilidad.CuposDisponiblesDesconocidos));
        Assert.DoesNotContain(MotivoCompatibilidad.SinCupo, resultado.Motivos);
        Assert.DoesNotContain(MotivoCompatibilidad.PesoExcedido, resultado.Motivos);
        Assert.Null(resultado.InsercionSeleccionada);
        desvios.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("inactivo", MotivoCompatibilidad.ViajeInactivo)]
    [InlineData("noDisponible", MotivoCompatibilidad.ViajeNoDisponible)]
    [InlineData("envioAsignado", MotivoCompatibilidad.EnvioNoDisponible)]
    [InlineData("excesoPeso", MotivoCompatibilidad.PesoExcedido)]
    [InlineData("sinCupos", MotivoCompatibilidad.SinCupo)]
    public async Task EvaluarAsync_RechazoYDatosDesconocidos_PriorizaIncompatibilidad(
        string caso, MotivoCompatibilidad motivo)
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.PesoDisponibleKg = null;
        viaje.CuposDisponibles = null;
        switch (caso)
        {
            case "inactivo": viaje.Activo = false; break;
            case "noDisponible": viaje.Disponible = false; break;
            case "envioAsignado": envio.Estado = EstadoEnvio.ConductorAsignado; break;
            case "excesoPeso": viaje.PesoDisponibleKg = 1; break;
            case "sinCupos": viaje.CuposDisponibles = 0; break;
        }

        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(motivo, resultado.Motivos);
        Assert.True(resultado.Motivos.Contains(MotivoCompatibilidad.PesoDisponibleDesconocido) ||
            resultado.Motivos.Contains(MotivoCompatibilidad.CuposDisponiblesDesconocidos));
        Assert.Contains(MotivoCompatibilidad.DesvioNoEvaluado, resultado.Motivos);
        Assert.Null(resultado.InsercionSeleccionada);
        desvios.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluarAsync_VariosRechazos_ConservaTodosLosMotivosSinDuplicarlos()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.Activo = false;
        viaje.Disponible = false;
        viaje.PesoDisponibleKg = 0;
        viaje.CuposDisponibles = 0;
        envio.Estado = EstadoEnvio.Cancelado;

        var resultado = await Servicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Equal(new[]
        {
            MotivoCompatibilidad.ViajeInactivo,
            MotivoCompatibilidad.ViajeNoDisponible,
            MotivoCompatibilidad.EnvioNoDisponible,
            MotivoCompatibilidad.PesoExcedido,
            MotivoCompatibilidad.SinCupo,
            MotivoCompatibilidad.DesvioNoEvaluado,
            MotivoCompatibilidad.HorarioNoEvaluado
        }, resultado.Motivos);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    public async Task EvaluarAsync_RestriccionesExplicitas_InformadasOSinLimiteSegunCorresponda(
        bool salida, bool llegada, bool desde, bool limite)
    {
        var viaje = Viaje();
        var envio = Envio();
        var fecha = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(-3));
        viaje.SalidaProgramada = salida ? fecha : null;
        viaje.LlegadaMaxima = llegada ? Informada(fecha.AddHours(2)) : SinRestriccion();
        envio.DisponibleDesde = desde ? Informada(fecha) : SinRestriccion();
        envio.FechaLimiteEntrega = limite ? Informada(fecha.AddHours(1)) : SinRestriccion();

        var resultado = await Servicio().EvaluarAsync(viaje, envio);

        Assert.Equal(salida ? EstadoCompatibilidad.Compatible : EstadoCompatibilidad.Indeterminado,
            resultado.Estado);
        Assert.Contains(salida ? MotivoCompatibilidad.HorarioAprobado : MotivoCompatibilidad.SalidaNoInformada,
            resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_LimitesTemporalesEnMismoInstanteConDistintoOffset_SonCoherentes()
    {
        var viaje = Viaje();
        var envio = Envio();
        var fecha = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(-3));
        viaje.SalidaProgramada = fecha;
        viaje.LlegadaMaxima = Informada(fecha.ToUniversalTime());
        envio.DisponibleDesde = Informada(fecha);
        envio.FechaLimiteEntrega = Informada(fecha.ToUniversalTime());

        var resultado = await Servicio().EvaluarAsync(viaje, envio);

        // Las ventanas son estructuralmente válidas, pero el recorrido dura más de cero.
        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.LlegadaMaximaExcedida, resultado.Motivos);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvaluarAsync_VentanaInternaInvertida_RechazaDatos(bool ventanaViaje)
    {
        var viaje = Viaje();
        var envio = Envio();
        var fecha = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        if (ventanaViaje)
        {
            viaje.SalidaProgramada = fecha;
            viaje.LlegadaMaxima = Informada(fecha.AddSeconds(-1));
        }
        else
        {
            envio.DisponibleDesde = Informada(fecha);
            envio.FechaLimiteEntrega = Informada(fecha.AddSeconds(-1));
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().EvaluarAsync(viaje, envio));
    }

    [Theory]
    [InlineData("viajeId")]
    [InlineData("envioId")]
    [InlineData("rutaNula")]
    [InlineData("rutaVacia")]
    [InlineData("unPunto")]
    [InlineData("puntoNulo")]
    [InlineData("retiroNulo")]
    [InlineData("entregaNula")]
    [InlineData("pesoCero")]
    [InlineData("pesoNegativo")]
    [InlineData("capacidadNegativa")]
    [InlineData("cuposNegativos")]
    [InlineData("desvioNegativo")]
    [InlineData("desvioNoConvertible")]
    [InlineData("estadoInvalido")]
    public async Task EvaluarAsync_DatosEstructuralesInvalidos_LanzaErrorDeArgumento(string caso)
    {
        var viaje = Viaje();
        var envio = Envio();
        switch (caso)
        {
            case "viajeId": viaje.ViajeId = 0; break;
            case "envioId": envio.EnvioId = -1; break;
            case "rutaNula": viaje.RecorridoBase = null!; break;
            case "rutaVacia": viaje.RecorridoBase = Array.Empty<PuntoRutaDto>(); break;
            case "unPunto": viaje.RecorridoBase = new[] { Punto() }; break;
            case "puntoNulo": viaje.RecorridoBase = new[] { Punto(), null!, Punto() }; break;
            case "retiroNulo": envio.Retiro = null!; break;
            case "entregaNula": envio.Entrega = null!; break;
            case "pesoCero": envio.PesoKg = 0; break;
            case "pesoNegativo": envio.PesoKg = -1; break;
            case "capacidadNegativa": viaje.PesoDisponibleKg = -1; break;
            case "cuposNegativos": viaje.CuposDisponibles = -1; break;
            case "desvioNegativo": viaje.DesvioMaxKm = -1; break;
            case "desvioNoConvertible": viaje.DesvioMaxKm = decimal.MaxValue; break;
            case "estadoInvalido": envio.Estado = (EstadoEnvio)255; break;
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().EvaluarAsync(viaje, envio));
    }

    public static IEnumerable<object[]> CoordenadasInvalidas()
    {
        foreach (var lugar in new[] { "ruta", "retiro", "entrega" })
        {
            yield return new object[] { lugar, -91, 0 };
            yield return new object[] { lugar, 91, 0 };
            yield return new object[] { lugar, 0, -181 };
            yield return new object[] { lugar, 0, 181 };
        }
    }

    [Theory]
    [MemberData(nameof(CoordenadasInvalidas))]
    public async Task EvaluarAsync_CoordenadasFueraDeRango_ValidaRutaRetiroYEntrega(
        string lugar, int latitud, int longitud)
    {
        var viaje = Viaje();
        var envio = Envio();
        var punto = Punto(latitud, longitud);
        if (lugar == "ruta") viaje.RecorridoBase = new[] { Punto(), punto, Punto() };
        if (lugar == "retiro") envio.Retiro = punto;
        if (lugar == "entrega") envio.Entrega = punto;

        await Assert.ThrowsAsync<ArgumentException>(
            () => Servicio().EvaluarAsync(viaje, envio));
    }

    [Fact]
    public async Task EvaluarAsync_LimitesInclusivosYCeroCero_NoConfundeConDatosAusentes()
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.RecorridoBase = new[] { Punto(-90, -180), Punto(0, 0), Punto(90, 180) };
        viaje.DesvioMaxKm = 0;
        envio.Retiro = Punto(0, 0);
        envio.Entrega = Punto(90, -180);

        var resultado = await Servicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
    }

    [Fact]
    public async Task EvaluarAsync_ObjetosNulos_SeRechazan()
    {
        var servicio = Servicio();

        await Assert.ThrowsAsync<ArgumentNullException>(() => servicio.EvaluarAsync(null!, Envio()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => servicio.EvaluarAsync(Viaje(), null!));
    }

    [Fact]
    public async Task EvaluarAsync_Cancelacion_PropagaElToken()
    {
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();

        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CompatibilidadServicio(desvios.Object).EvaluarAsync(Viaje(), Envio(), cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        desvios.VerifyNoOtherCalls();
    }

    [Fact]
    public void Constructor_DesviosNulo_SeRechaza()
    {
        Assert.Throws<ArgumentNullException>(() => new CompatibilidadServicio(null!));
    }

    private static Mock<IDesviosServicio> DesviosConfigurados(
        ViajeCompatibilidadDto viaje,
        EnvioCompatibilidadDto envio,
        Func<int, int, ResultadoDesvioDto> calcular,
        CancellationToken cancellationToken = default)
    {
        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var evaluacion = new Mock<IEvaluacionDesvios>(MockBehavior.Strict);
        evaluacion.Setup(e => e.CalcularAsync(It.IsAny<int>(), It.IsAny<int>(), cancellationToken))
            .Returns((int retiro, int entrega, CancellationToken _) =>
                Task.FromResult(calcular(retiro, entrega)));
        desvios.Setup(d => d.PrepararAsync(
                viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, cancellationToken))
            .ReturnsAsync(evaluacion.Object);
        return desvios;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EvaluarAsync_Enumeracion_ConsultaTodasLasParejasValidasUnaVez(int paradas)
    {
        var viaje = ViajeConParadas(paradas);
        var envio = Envio();
        var llamadas = new List<(int Retiro, int Entrega)>();
        using var cancelacion = new CancellationTokenSource();
        var desvios = DesviosConfigurados(viaje, envio, (retiro, entrega) =>
        {
            llamadas.Add((retiro, entrega));
            return Desvio();
        }, cancelacion.Token);

        var resultado = await new CompatibilidadServicio(desvios.Object)
            .EvaluarAsync(viaje, envio, cancelacion.Token);

        var esperadas = paradas switch
        {
            0 => new[] { (0, 0) },
            1 => new[] { (0, 0), (0, 1), (1, 1) },
            _ => new[] { (0, 0), (0, 1), (0, 2), (1, 1), (1, 2), (2, 2) }
        };
        Assert.Equal(esperadas, llamadas);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(0, resultado.InsercionSeleccionada!.SegmentoRetiro);
        Assert.Equal(0, resultado.InsercionSeleccionada.SegmentoEntrega);
    }

    public static IEnumerable<object[]> Selecciones()
    {
        // Parejas: (0,0), (0,1), (0,2), (1,1), (1,2), (2,2).
        yield return new object[] { new long[] { 1800, 1500, 1000, 200, 1700, 500 },
            new long[] { 60, 60, 60, 60, 60, 60 }, 1, 1 }; // Medio mejor que final.
        yield return new object[] { new long[] { 1500, 200, 1800, 1400, 1600, 1700 },
            new long[] { 60, 60, 60, 60, 60, 60 }, 0, 1 }; // Segmentos diferentes.
        yield return new object[] { new long[] { 1800, 1700, 1600, 1400, 1300, 100 },
            new long[] { 60, 60, 60, 60, 60, 60 }, 2, 2 }; // También puede ganar el final.
        yield return new object[] { new long[] { 500, 500, 500, 500, 500, 500 },
            new long[] { 90, 80, 70, 60, 50, 40 }, 2, 2 }; // Desempate por tiempo.
        yield return new object[] { new long[] { 3000, 500, 3000, 500, 3000, 3000 },
            new long[] { 60, 60, 60, 60, 60, 60 }, 0, 1 }; // Menor índice de retiro.
        yield return new object[] { new long[] { 3000, 500, 500, 3000, 3000, 3000 },
            new long[] { 60, 60, 60, 60, 60, 60 }, 0, 1 }; // Menor índice de entrega.
        yield return new object[] { new long[] { -100, -500, -300, 0, 200, 400 },
            new long[] { 60, 60, 60, 60, 60, 60 }, 0, 1 }; // Diferencias negativas reales.
        yield return new object[] { new long[] { 500, 400, 600, 700, 800, 900 },
            new long[] { -100, 200, 60, 60, 60, 60 }, 0, 1 }; // Distancia antes que tiempo.
        yield return new object[] { new long[] { 500, 500, 500, 500, 500, 500 },
            new long[] { 60, -100, -50, 60, 60, 60 }, 0, 1 }; // Tiempo negativo sin normalizar.
    }

    [Theory]
    [MemberData(nameof(Selecciones))]
    public async Task EvaluarAsync_Seleccion_OrdenaPorDistanciaTiempoEIndices(
        long[] metros, long[] segundos, int retiroEsperado, int entregaEsperada)
    {
        var viaje = ViajeConParadas(2);
        var envio = Envio();
        var parejas = new[] { (0, 0), (0, 1), (0, 2), (1, 1), (1, 2), (2, 2) };
        var respuestas = parejas.Select((p, i) => (p, desvio: Desvio(metros[i], segundos[i])))
            .ToDictionary(x => x.p, x => x.desvio);
        var llamadas = 0;
        var desvios = DesviosConfigurados(viaje, envio, (retiro, entrega) =>
        {
            llamadas++;
            return respuestas[(retiro, entrega)];
        });

        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio);

        Assert.Equal(6, llamadas);
        var elegida = Assert.IsType<InsercionCompatibilidadDto>(resultado.InsercionSeleccionada);
        Assert.Equal(retiroEsperado, elegida.SegmentoRetiro);
        Assert.Equal(entregaEsperada, elegida.SegmentoEntrega);
        Assert.Same(respuestas[(retiroEsperado, entregaEsperada)], elegida.Desvio);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.DesvioAprobado, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.HorarioNoEvaluado, resultado.Motivos);
        Assert.DoesNotContain(MotivoCompatibilidad.DesvioNoEvaluado, resultado.Motivos);
        Assert.DoesNotContain(MotivoCompatibilidad.DesvioExcedido, resultado.Motivos);
    }

    [Theory]
    [InlineData(1499, true)]
    [InlineData(1500, true)]
    [InlineData(1501, false)]
    public async Task EvaluarAsync_LimiteEnKm_EvaluaDistanciaAdicionalConIgualdadInclusiva(
        long metros, bool aprobado)
    {
        var viaje = Viaje();
        var envio = Envio();
        viaje.DesvioMaxKm = 1.5m;
        var desvio = Desvio(metros, 60, viaje.DesvioMaxKm);
        var desvios = DesviosConfigurados(viaje, envio, (_, _) => desvio);

        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio);

        Assert.Equal(aprobado ? EstadoCompatibilidad.Indeterminado : EstadoCompatibilidad.Incompatible,
            resultado.Estado);
        Assert.Equal(aprobado, resultado.InsercionSeleccionada != null);
        Assert.Contains(aprobado ? MotivoCompatibilidad.DesvioAprobado : MotivoCompatibilidad.DesvioExcedido,
            resultado.Motivos);
        if (aprobado)
            Assert.Same(desvio, resultado.InsercionSeleccionada!.Desvio);
    }

    [Fact]
    public async Task EvaluarAsync_TodasLasAlternativasExceden_RechazaSinSeleccion()
    {
        var viaje = ViajeConParadas(2);
        var envio = Envio();
        var llamadas = 0;
        var desvios = DesviosConfigurados(viaje, envio, (_, _) =>
        {
            llamadas++;
            return Desvio(2001, 60);
        });

        var resultado = await new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio);

        Assert.Equal(6, llamadas);
        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Null(resultado.InsercionSeleccionada);
        Assert.Equal(new[] { MotivoCompatibilidad.DesvioExcedido, MotivoCompatibilidad.HorarioNoEvaluado },
            resultado.Motivos);
    }

    [Theory]
    [InlineData("http", 1)]
    [InlineData("http", 2)]
    [InlineData("timeout", 1)]
    [InlineData("timeout", 2)]
    [InlineData("proveedor", 1)]
    [InlineData("proveedor", 2)]
    public async Task EvaluarAsync_ErrorDuranteBusqueda_PropagaSinDevolverResultadoParcial(
        string tipoError, int llamadaFallida)
    {
        var viaje = ViajeConParadas(1);
        var envio = Envio();
        Exception error = tipoError switch
        {
            "http" => new HttpRequestException("Fallo HTTP simulado."),
            "timeout" => new TimeoutException("Timeout simulado."),
            _ => new InvalidOperationException("Fallo de desvíos simulado.")
        };
        var llamadas = 0;
        var desvios = DesviosConfigurados(viaje, envio, (_, _) =>
        {
            llamadas++;
            if (llamadas == llamadaFallida) throw error;
            return Desvio(); // Una inserción anterior válida no justifica devolver un mínimo parcial.
        });

        var recibido = await Record.ExceptionAsync(
            () => new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio));

        Assert.Same(error, recibido);
        Assert.Equal(llamadaFallida, llamadas);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EvaluarAsync_CancelacionDuranteProveedor_InterrumpeSinResultadoParcial(int llamadaCancelada)
    {
        var viaje = ViajeConParadas(1);
        var envio = Envio();
        using var cancelacion = new CancellationTokenSource();
        var llamadas = 0;
        var desvios = new Mock<IDesviosServicio>(MockBehavior.Strict);
        var evaluacion = new Mock<IEvaluacionDesvios>(MockBehavior.Strict);
        evaluacion.Setup(e => e.CalcularAsync(It.IsAny<int>(), It.IsAny<int>(), cancelacion.Token))
            .Returns((int _, int _, CancellationToken token) =>
            {
                llamadas++;
                if (llamadas == llamadaCancelada)
                {
                    cancelacion.Cancel();
                    return Task.FromCanceled<ResultadoDesvioDto>(token);
                }
                return Task.FromResult(Desvio());
            });
        desvios.Setup(d => d.PrepararAsync(
                viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, cancelacion.Token))
            .ReturnsAsync(evaluacion.Object);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio, cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Equal(llamadaCancelada, llamadas);
    }

    [Fact]
    public async Task EvaluarAsync_CancelacionAlRecibirUltimaAlternativa_NoPublicaResultado()
    {
        var viaje = Viaje();
        var envio = Envio();
        using var cancelacion = new CancellationTokenSource();
        var llamadas = 0;
        var desvios = DesviosConfigurados(viaje, envio, (_, _) =>
        {
            llamadas++;
            cancelacion.Cancel();
            return Desvio();
        }, cancelacion.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio, cancelacion.Token));

        Assert.Equal(1, llamadas);
    }

    [Fact]
    public async Task EvaluarAsync_ProveedorDevuelveNulo_PropagaErrorTecnico()
    {
        var viaje = Viaje();
        var envio = Envio();
        var desvios = DesviosConfigurados(viaje, envio, (_, _) => null!);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CompatibilidadServicio(desvios.Object).EvaluarAsync(viaje, envio));
    }

    [Fact]
    public async Task EvaluarAsync_IntegracionEnMemoria_SeleccionaRetiroYEntregaEnSegmentosDiferentes()
    {
        var viaje = ViajeConParadas(1);
        var envio = Envio();
        var origen = viaje.RecorridoBase[0];
        var parada = viaje.RecorridoBase[1];
        var destino = viaje.RecorridoBase[2];
        var rutas = new RutasMockServicio();
        rutas.ConfigurarRecorrido(viaje.RecorridoBase,
            new ResultadoRutaDto { DistanciaMetros = 20000, DuracionSegundos = 1200 });
        rutas.ConfigurarRecorrido(new[] { origen, envio.Retiro, envio.Entrega, parada, destino },
            new ResultadoRutaDto { DistanciaMetros = 23500, DuracionSegundos = 1500 });
        rutas.ConfigurarRecorrido(new[] { origen, envio.Retiro, parada, envio.Entrega, destino },
            new ResultadoRutaDto { DistanciaMetros = 20500, DuracionSegundos = 1300 });
        rutas.ConfigurarRecorrido(new[] { origen, parada, envio.Retiro, envio.Entrega, destino },
            new ResultadoRutaDto { DistanciaMetros = 21500, DuracionSegundos = 1400 });
        var servicio = new CompatibilidadServicio(new DesviosServicio(rutas));

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        var elegida = Assert.IsType<InsercionCompatibilidadDto>(resultado.InsercionSeleccionada);
        Assert.Equal(0, elegida.SegmentoRetiro);
        Assert.Equal(1, elegida.SegmentoEntrega);
        Assert.Equal(500L, elegida.Desvio.DistanciaAdicionalMetros);
        Assert.Equal(100L, elegida.Desvio.TiempoAdicionalSegundos);
        Assert.Equal(20500L, elegida.Desvio.DistanciaConEnvioMetros);
        Assert.True(elegida.Desvio.CumpleLimiteDesvio);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.DesvioAprobado, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.HorarioNoEvaluado, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_DatosDeEntrada_NoModificaCoordenadasCapacidadNiEstados()
    {
        var viaje = Viaje();
        var envio = Envio();
        var puntosOriginales = viaje.RecorridoBase.ToArray();
        var coordenadas = puntosOriginales.Select(p => (p.Latitud, p.Longitud)).ToArray();
        var retiro = (envio.Retiro.Latitud, envio.Retiro.Longitud);
        var entrega = (envio.Entrega.Latitud, envio.Entrega.Longitud);
        var servicio = Servicio();

        var primero = await servicio.EvaluarAsync(viaje, envio);
        var segundo = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(primero.Estado, segundo.Estado);
        Assert.Equal(primero.Motivos, segundo.Motivos);
        Assert.Equal(puntosOriginales, viaje.RecorridoBase);
        Assert.Equal(coordenadas, viaje.RecorridoBase.Select(p => (p.Latitud, p.Longitud)).ToArray());
        Assert.Equal(retiro, (envio.Retiro.Latitud, envio.Retiro.Longitud));
        Assert.Equal(entrega, (envio.Entrega.Latitud, envio.Entrega.Longitud));
        Assert.Equal(10m, viaje.PesoDisponibleKg);
        Assert.Equal(1, viaje.CuposDisponibles);
        Assert.True(viaje.Activo);
        Assert.True(viaje.Disponible);
        Assert.Equal(EstadoEnvio.BuscandoConductor, envio.Estado);
    }
}
