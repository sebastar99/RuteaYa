using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class DesviosServicioTest
{
    private static PuntoRutaDto Punto(decimal latitud, decimal longitud) => new()
    {
        Latitud = latitud,
        Longitud = longitud
    };

    private static PuntoRutaDto[] RutaBase() => new[]
    {
        Punto(-34.60m, -58.38m), Punto(-34.76m, -58.40m)
    };

    private static PuntoRutaDto Retiro() => Punto(-34.68m, -58.39m);
    private static PuntoRutaDto Entrega() => Punto(-34.72m, -58.41m);

    private static ResultadoRutaDto Resultado(long metros, long segundos) => new()
    {
        DistanciaMetros = metros,
        DuracionSegundos = segundos
    };

    private static RutasMockServicio ConfigurarRutas(long metrosConEnvio, long segundosConEnvio)
    {
        var rutas = new RutasMockServicio();
        var rutaBase = RutaBase();
        rutas.ConfigurarRecorrido(rutaBase, Resultado(20000, 1200));
        rutas.ConfigurarRecorrido(
            new[] { rutaBase[0], Retiro(), Entrega(), rutaBase[1] },
            Resultado(metrosConEnvio, segundosConEnvio));
        return rutas;
    }

    [Theory]
    [InlineData(21499, true)]
    [InlineData(21500, true)]
    [InlineData(21501, false)]
    public async Task CalcularAsync_LimiteEnKm_ComparaDistanciaAdicionalConMetros(
        long metrosConEnvio, bool cumple)
    {
        IDesviosServicio servicio = new DesviosServicio(ConfigurarRutas(metrosConEnvio, 1500));

        var resultado = await servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 1.5m, 0, 0);

        Assert.Equal(20000L, resultado.DistanciaOriginalMetros);
        Assert.Equal(1200L, resultado.DuracionOriginalSegundos);
        Assert.Equal(metrosConEnvio, resultado.DistanciaConEnvioMetros);
        Assert.Equal(1500L, resultado.DuracionConEnvioSegundos);
        Assert.Equal(metrosConEnvio - 20000, resultado.DistanciaAdicionalMetros);
        Assert.Equal(300L, resultado.TiempoAdicionalSegundos);
        Assert.Equal(cumple, resultado.CumpleLimiteDesvio);
    }

    [Fact]
    public async Task CalcularAsync_ConParadas_ConservaSecuenciaEInsertaRetiroAntesDeEntrega()
    {
        var rutas = new RutasMockServicio();
        var extremos = RutaBase();
        var paradaA = Punto(-34.62m, -58.37m);
        var paradaB = Punto(-34.65m, -58.38m);
        var rutaBase = new[] { extremos[0], paradaA, paradaB, extremos[1] };
        var referenciasOriginales = rutaBase.ToArray();
        var coordenadasOriginales = rutaBase.Select(p => (p.Latitud, p.Longitud)).ToArray();
        rutas.ConfigurarRecorrido(rutaBase, Resultado(22000, 1400));
        // Solo este orden está configurado: cualquier inserción distinta falla.
        rutas.ConfigurarRecorrido(
            new[] { extremos[0], paradaA, paradaB, Retiro(), Entrega(), extremos[1] },
            Resultado(23000, 1600));
        var servicio = new DesviosServicio(rutas);

        var resultado = await servicio.CalcularAsync(rutaBase, Retiro(), Entrega(), 1, 2, 2);

        Assert.Equal(1000L, resultado.DistanciaAdicionalMetros);
        Assert.Equal(200L, resultado.TiempoAdicionalSegundos);
        Assert.True(resultado.CumpleLimiteDesvio);
        Assert.Equal(referenciasOriginales, rutaBase);
        Assert.Equal(coordenadasOriginales, rutaBase.Select(p => (p.Latitud, p.Longitud)).ToArray());
    }

    [Theory]
    [InlineData(0, 0, "O,R,E,A,B,D")]
    [InlineData(0, 1, "O,R,A,E,B,D")]
    [InlineData(0, 2, "O,R,A,B,E,D")]
    [InlineData(1, 1, "O,A,R,E,B,D")]
    [InlineData(1, 2, "O,A,R,B,E,D")]
    [InlineData(2, 2, "O,A,B,R,E,D")]
    public async Task CalcularAsync_SegmentosExplicitos_RespetaInsercionesYParadasOriginales(
        int segmentoRetiro, int segmentoEntrega, string ordenEsperado)
    {
        var puntos = new Dictionary<string, PuntoRutaDto>
        {
            ["O"] = RutaBase()[0],
            ["A"] = Punto(-34.62m, -58.37m),
            ["B"] = Punto(-34.65m, -58.38m),
            ["D"] = RutaBase()[1],
            ["R"] = Retiro(),
            ["E"] = Entrega()
        };
        var rutaBase = new[] { puntos["O"], puntos["A"], puntos["B"], puntos["D"] };
        var coordenadasOriginales = rutaBase.Select(p => (p.Latitud, p.Longitud)).ToArray();
        var alternativaEsperada = ordenEsperado.Split(',').Select(p => puntos[p]).ToArray();
        var rutas = new RutasMockServicio();
        rutas.ConfigurarRecorrido(rutaBase, Resultado(20000, 1200));
        // El mock solo conoce el orden esperado para este caso.
        rutas.ConfigurarRecorrido(alternativaEsperada, Resultado(21000, 1350));
        IDesviosServicio servicio = new DesviosServicio(rutas);

        var resultado = await servicio.CalcularAsync(
            rutaBase, puntos["R"], puntos["E"], 1, segmentoRetiro, segmentoEntrega);

        Assert.Equal(1000L, resultado.DistanciaAdicionalMetros);
        Assert.Equal(150L, resultado.TiempoAdicionalSegundos);
        Assert.True(resultado.CumpleLimiteDesvio);
        Assert.Equal(coordenadasOriginales, rutaBase.Select(p => (p.Latitud, p.Longitud)).ToArray());
        Assert.Same(puntos["O"], rutaBase[0]);
        Assert.Same(puntos["A"], rutaBase[1]);
        Assert.Same(puntos["B"], rutaBase[2]);
        Assert.Same(puntos["D"], rutaBase[3]);
    }

    [Theory]
    [InlineData(-1, 0, "segmentoRetiro")]
    [InlineData(0, -1, "segmentoEntrega")]
    [InlineData(3, 2, "segmentoRetiro")]
    [InlineData(0, 3, "segmentoEntrega")]
    [InlineData(int.MaxValue, 0, "segmentoRetiro")]
    [InlineData(0, int.MaxValue, "segmentoEntrega")]
    public async Task CalcularAsync_SegmentoFueraDeRango_FallaSinConsultarProveedor(
        int segmentoRetiro, int segmentoEntrega, string parametro)
    {
        var rutas = new Mock<IRutasServicio>(MockBehavior.Strict);
        var servicio = new DesviosServicio(rutas.Object);
        var extremos = RutaBase();
        var rutaBase = new[]
        {
            extremos[0], Punto(-34.62m, -58.37m), Punto(-34.65m, -58.38m), extremos[1]
        };

        var error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => servicio.CalcularAsync(rutaBase, Retiro(), Entrega(), 1,
                segmentoRetiro, segmentoEntrega));

        Assert.Equal(parametro, error.ParamName);
        rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CalcularAsync_EntregaEnSegmentoAnteriorAlRetiro_RechazaOrdenIncompatible()
    {
        var rutas = new Mock<IRutasServicio>(MockBehavior.Strict);
        var servicio = new DesviosServicio(rutas.Object);
        var extremos = RutaBase();
        var rutaBase = new[] { extremos[0], Punto(-34.65m, -58.38m), extremos[1] };

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => servicio.CalcularAsync(rutaBase, Retiro(), Entrega(), 1, 1, 0));

        Assert.Equal("segmentoEntrega", error.ParamName);
        Assert.Contains("no puede ser anterior", error.Message);
        rutas.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(19000, 1100, -1000, -100)]
    [InlineData(20000, 1200, 0, 0)]
    [InlineData(19000, 1300, -1000, 100)]
    public async Task CalcularAsync_DiferenciasNoPositivas_ConservaValoresReales(
        long metros, long segundos, long distanciaAdicional, long tiempoAdicional)
    {
        var servicio = new DesviosServicio(ConfigurarRutas(metros, segundos));

        var resultado = await servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 0, 0, 0);

        Assert.Equal(distanciaAdicional, resultado.DistanciaAdicionalMetros);
        Assert.Equal(tiempoAdicional, resultado.TiempoAdicionalSegundos);
        Assert.True(resultado.CumpleLimiteDesvio);
    }

    public static IEnumerable<object[]> EntradasInvalidas()
    {
        yield return new object[] { null!, Retiro(), Entrega(), 1m };
        yield return new object[] { Array.Empty<PuntoRutaDto>(), Retiro(), Entrega(), 1m };
        yield return new object[] { new[] { RutaBase()[0] }, Retiro(), Entrega(), 1m };
        yield return new object[] { new[] { RutaBase()[0], null! }, Retiro(), Entrega(), 1m };
        yield return new object[] { RutaBase(), null!, Entrega(), 1m };
        yield return new object[] { RutaBase(), Retiro(), null!, 1m };
        yield return new object[] { RutaBase(), Retiro(), Entrega(), -1m };
        yield return new object[] { RutaBase(), Retiro(), Entrega(), decimal.MaxValue };

        foreach (var invalido in new[] { Punto(-91, 0), Punto(91, 0), Punto(0, -181), Punto(0, 181) })
        {
            yield return new object[] { new[] { RutaBase()[0], invalido }, Retiro(), Entrega(), 1m };
            yield return new object[] { RutaBase(), invalido, Entrega(), 1m };
            yield return new object[] { RutaBase(), Retiro(), invalido, 1m };
        }
    }

    [Theory]
    [MemberData(nameof(EntradasInvalidas))]
    public async Task CalcularAsync_EntradaInvalida_FallaAntesDeConsultarProveedor(
        IReadOnlyList<PuntoRutaDto> rutaBase, PuntoRutaDto retiro, PuntoRutaDto entrega, decimal limite)
    {
        var rutas = new Mock<IRutasServicio>(MockBehavior.Strict);
        var servicio = new DesviosServicio(rutas.Object);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => servicio.CalcularAsync(rutaBase, retiro, entrega, limite, 0, 0));

        rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public void Constructor_ProveedorNulo_SeRechaza()
    {
        Assert.Throws<ArgumentNullException>(() => new DesviosServicio(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalcularAsync_ErrorDelProveedor_PropagaLaMismaExcepcion(bool fallaAlternativa)
    {
        var rutas = new Mock<IRutasServicio>();
        var error = new HttpRequestException("Proveedor no disponible.");
        var secuencia = rutas.SetupSequence(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()));
        if (fallaAlternativa)
            secuencia.ReturnsAsync(Resultado(20000, 1200));
        secuencia.ThrowsAsync(error);
        var servicio = new DesviosServicio(rutas.Object);

        var recibido = await Assert.ThrowsAsync<HttpRequestException>(
            () => servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 1, 0, 0));

        Assert.Same(error, recibido);
        rutas.Verify(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(fallaAlternativa ? 2 : 1));
    }

    [Fact]
    public async Task CalcularAsync_AlternativaNoConfigurada_PropagaErrorDelMock()
    {
        var rutas = new RutasMockServicio();
        rutas.ConfigurarRecorrido(RutaBase(), Resultado(20000, 1200));
        var servicio = new DesviosServicio(rutas);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 1, 0, 0));

        Assert.Contains("No hay un recorrido configurado", error.Message);
    }

    [Theory]
    [InlineData(-1, 1200)]
    [InlineData(20000, -1)]
    public async Task CalcularAsync_TotalesNegativosDelProveedor_SeRechazan(long metros, long segundos)
    {
        var rutas = new Mock<IRutasServicio>();
        rutas.Setup(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resultado(metros, segundos));
        var servicio = new DesviosServicio(rutas.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 1, 0, 0));
    }

    [Fact]
    public async Task CalcularAsync_TokenYaCancelado_NoConsultaProveedor()
    {
        var rutas = new Mock<IRutasServicio>(MockBehavior.Strict);
        var servicio = new DesviosServicio(rutas.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => servicio.CalcularAsync(
            RutaBase(), Retiro(), Entrega(), 1, 0, 0, new CancellationToken(true)));

        rutas.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CalcularAsync_OperacionNormal_PropagaTokenEnAmbasConsultas()
    {
        using var cancelacion = new CancellationTokenSource();
        var rutas = new Mock<IRutasServicio>(MockBehavior.Strict);
        rutas.SetupSequence(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), cancelacion.Token))
            .ReturnsAsync(Resultado(20000, 1200))
            .ReturnsAsync(Resultado(21000, 1300));
        var servicio = new DesviosServicio(rutas.Object);

        var resultado = await servicio.CalcularAsync(
            RutaBase(), Retiro(), Entrega(), 1, 0, 0, cancelacion.Token);

        Assert.True(resultado.CumpleLimiteDesvio);
        rutas.Verify(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), cancelacion.Token), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalcularAsync_CancelacionDuranteConsulta_PropagaCancelacion(bool enAlternativa)
    {
        using var cancelacion = new CancellationTokenSource();
        var llamadas = 0;
        var rutas = new Mock<IRutasServicio>();
        rutas.Setup(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), cancelacion.Token))
            .Returns((IReadOnlyList<PuntoRutaDto> _, CancellationToken token) =>
            {
                llamadas++;
                if (llamadas == (enAlternativa ? 2 : 1))
                {
                    cancelacion.Cancel();
                    return Task.FromCanceled<ResultadoRutaDto>(token);
                }
                return Task.FromResult(Resultado(20000, 1200));
            });
        var servicio = new DesviosServicio(rutas.Object);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 1, 0, 0, cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Equal(enAlternativa ? 2 : 1, llamadas);
    }

    [Fact]
    public async Task CalcularAsync_CancelacionDespuesDeRutaBase_NoConsultaAlternativa()
    {
        using var cancelacion = new CancellationTokenSource();
        var rutas = new Mock<IRutasServicio>();
        rutas.Setup(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), cancelacion.Token))
            .Returns(() =>
            {
                cancelacion.Cancel();
                return Task.FromResult(Resultado(20000, 1200));
            });
        var servicio = new DesviosServicio(rutas.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => servicio.CalcularAsync(RutaBase(), Retiro(), Entrega(), 1, 0, 0, cancelacion.Token));

        rutas.Verify(r => r.CalcularAsync(
            It.IsAny<IReadOnlyList<PuntoRutaDto>>(), cancelacion.Token), Times.Once);
    }
}
