using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class DesviosPreparadosTest
{
    private static PuntoRutaDto Punto(decimal valor) => new() { Latitud = valor, Longitud = valor };
    private static ResultadoRutaDto Ruta(long metros, long segundos) => new()
    {
        DistanciaMetros = metros,
        DuracionSegundos = segundos
    };

    private static (ViajeCompatibilidadDto Viaje, EnvioCompatibilidadDto Envio, RutasMockServicio Rutas) Escenario()
    {
        var puntos = new Dictionary<string, PuntoRutaDto>
        {
            ["O"] = Punto(1), ["A"] = Punto(2), ["B"] = Punto(3),
            ["D"] = Punto(4), ["R"] = Punto(5), ["E"] = Punto(6)
        };
        var viaje = new ViajeCompatibilidadDto
        {
            ViajeId = 1, Activo = true, Disponible = true, PesoDisponibleKg = 10,
            CuposDisponibles = 2, DesvioMaxKm = 2,
            RecorridoBase = new[] { puntos["O"], puntos["A"], puntos["B"], puntos["D"] }
        };
        var envio = new EnvioCompatibilidadDto
        {
            EnvioId = 2, Estado = EstadoEnvio.BuscandoConductor,
            PesoKg = 1, Retiro = puntos["R"], Entrega = puntos["E"]
        };
        var rutas = new RutasMockServicio();
        rutas.ConfigurarRecorrido(viaje.RecorridoBase, Ruta(20000, 1200));
        var alternativas = new[]
        {
            ("O,R,E,A,B,D", 23000L, 1400L),
            ("O,R,A,E,B,D", 21000L, 1300L),
            ("O,R,A,B,E,D", 22000L, 1250L),
            ("O,A,R,E,B,D", 20500L, 1350L),
            ("O,A,R,B,E,D", 20500L, 1300L),
            ("O,A,B,R,E,D", 21500L, 1400L)
        };
        foreach (var (orden, metros, segundos) in alternativas)
            rutas.ConfigurarRecorrido(orden.Split(',').Select(p => puntos[p]).ToArray(), Ruta(metros, segundos));
        return (viaje, envio, rutas);
    }

    private static Mock<IRutasServicio> ContarRutas(
        RutasMockServicio rutas, List<(decimal Latitud, decimal Longitud)[]> llamadas)
    {
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.Setup(r => r.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PuntoRutaDto> puntos, CancellationToken token) =>
            {
                llamadas.Add(puntos.Select(p => (p.Latitud, p.Longitud)).ToArray());
                return rutas.CalcularAsync(puntos, token);
            });
        return proveedor;
    }

    [Fact]
    public async Task Compatibilidad_CuatroPuntos_CalculaUnaBaseYSeisAlternativasConMismoResultado()
    {
        var (viaje, envio, rutas) = Escenario();
        var llamadas = new List<(decimal, decimal)[]>();
        var proveedor = ContarRutas(rutas, llamadas);
        var servicio = new CompatibilidadServicio(new DesviosServicio(proveedor.Object));

        var resultado = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(7, llamadas.Count);
        Assert.Single(llamadas, p => p.Length == 4);
        Assert.Equal(6, llamadas.Count(p => p.Length == 6));
        Assert.Equal(6, llamadas.Skip(1).Select(p => string.Join(";", p)).Distinct().Count());
        var elegida = Assert.IsType<InsercionCompatibilidadDto>(resultado.InsercionSeleccionada);
        Assert.Equal(1, elegida.SegmentoRetiro);
        Assert.Equal(2, elegida.SegmentoEntrega);
        Assert.Equal(20000L, elegida.Desvio.DistanciaOriginalMetros);
        Assert.Equal(1200L, elegida.Desvio.DuracionOriginalSegundos);
        Assert.Equal(20500L, elegida.Desvio.DistanciaConEnvioMetros);
        Assert.Equal(1300L, elegida.Desvio.DuracionConEnvioSegundos);
        Assert.Equal(500L, elegida.Desvio.DistanciaAdicionalMetros);
        Assert.Equal(100L, elegida.Desvio.TiempoAdicionalSegundos);
        Assert.True(elegida.Desvio.CumpleLimiteDesvio);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.DesvioAprobado, resultado.Motivos);
        Assert.Contains(MotivoCompatibilidad.HorarioNoEvaluado, resultado.Motivos);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 7)]
    [InlineData(3, 11)]
    [InlineData(5, 22)]
    public async Task Compatibilidad_DistintasCantidadesDeParadas_ReutilizaBase(int paradas, int consultas)
    {
        var (viaje, envio, _) = Escenario();
        var cantidadPuntos = paradas + 2;
        viaje.RecorridoBase = Enumerable.Range(1, cantidadPuntos).Select(i => Punto(i)).ToArray();
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.Setup(r => r.CalcularAsync(
                It.Is<IReadOnlyList<PuntoRutaDto>>(p => p.Count == cantidadPuntos), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ruta(20000, 1200));
        proveedor.Setup(r => r.CalcularAsync(
                It.Is<IReadOnlyList<PuntoRutaDto>>(p => p.Count == cantidadPuntos + 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ruta(21000, 1300));

        var resultado = await new CompatibilidadServicio(new DesviosServicio(proveedor.Object))
            .EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(1000L, resultado.InsercionSeleccionada!.Desvio.DistanciaAdicionalMetros);
        proveedor.Verify(r => r.CalcularAsync(
            It.Is<IReadOnlyList<PuntoRutaDto>>(p => p.Count == cantidadPuntos), It.IsAny<CancellationToken>()), Times.Once);
        proveedor.Verify(r => r.CalcularAsync(
            It.Is<IReadOnlyList<PuntoRutaDto>>(p => p.Count == cantidadPuntos + 2), It.IsAny<CancellationToken>()),
            Times.Exactly(consultas - 1));
    }

    [Fact]
    public async Task Compatibilidad_DosEvaluacionesEnMismoServicio_NoComparteBase()
    {
        var (viaje, envio, rutas) = Escenario();
        var llamadas = new List<(decimal, decimal)[]>();
        var proveedor = ContarRutas(rutas, llamadas);
        var servicio = new CompatibilidadServicio(new DesviosServicio(proveedor.Object));

        await servicio.EvaluarAsync(viaje, envio);
        // Cambia la respuesta de la misma ruta: una caché entre evaluaciones sería incorrecta.
        rutas.ConfigurarRecorrido(viaje.RecorridoBase, Ruta(19000, 1100));
        var segundo = await servicio.EvaluarAsync(viaje, envio);

        Assert.Equal(14, llamadas.Count);
        Assert.Equal(2, llamadas.Count(p => p.Length == 4));
        Assert.Equal(19000L, segundo.InsercionSeleccionada!.Desvio.DistanciaOriginalMetros);
        Assert.Equal(1500L, segundo.InsercionSeleccionada.Desvio.DistanciaAdicionalMetros);
    }

    [Theory]
    [InlineData("inactivo")]
    [InlineData("noDisponible")]
    [InlineData("envioAsignado")]
    [InlineData("excesoPeso")]
    [InlineData("sinCupos")]
    [InlineData("pesoDesconocido")]
    [InlineData("cuposDesconocidos")]
    public async Task Compatibilidad_RechazoOCapacidadDesconocida_CeroCalculosDeRutas(string caso)
    {
        var (viaje, envio, _) = Escenario();
        switch (caso)
        {
            case "inactivo": viaje.Activo = false; break;
            case "noDisponible": viaje.Disponible = false; break;
            case "envioAsignado": envio.Estado = EstadoEnvio.ConductorAsignado; break;
            case "excesoPeso": envio.PesoKg = 11; break;
            case "sinCupos": viaje.CuposDisponibles = 0; break;
            case "pesoDesconocido": viaje.PesoDisponibleKg = null; break;
            case "cuposDesconocidos": viaje.CuposDisponibles = null; break;
        }
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);

        var resultado = await new CompatibilidadServicio(new DesviosServicio(proveedor.Object))
            .EvaluarAsync(viaje, envio);

        Assert.Null(resultado.InsercionSeleccionada);
        proveedor.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(DesviosServicioTest.EntradasInvalidas), MemberType = typeof(DesviosServicioTest))]
    public async Task PrepararAsync_DatosInvalidos_NoConsultaProveedor(
        IReadOnlyList<PuntoRutaDto> rutaBase, PuntoRutaDto retiro, PuntoRutaDto entrega, decimal limite)
    {
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new DesviosServicio(proveedor.Object).PrepararAsync(rutaBase, retiro, entrega, limite));

        proveedor.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 2)]
    [InlineData(0, 3)]
    [InlineData(1, 0)]
    public async Task Preparada_SegmentosInvalidos_NoCalculaAlternativa(int retiro, int entrega)
    {
        var (viaje, envio, rutas) = Escenario();
        var llamadas = new List<(decimal, decimal)[]>();
        var proveedor = ContarRutas(rutas, llamadas);
        var evaluacion = await new DesviosServicio(proveedor.Object).PrepararAsync(
            viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => evaluacion.CalcularAsync(retiro, entrega));

        Assert.Single(llamadas); // Solo la base de la preparación.
    }

    [Fact]
    public async Task Preparada_EIndividual_CompartenConstruccionYResultadosPeroNoCalculoBase()
    {
        var (viaje, envio, rutas) = Escenario();
        var llamadas = new List<(decimal, decimal)[]>();
        var proveedor = ContarRutas(rutas, llamadas);
        var servicio = new DesviosServicio(proveedor.Object);
        var parejas = new[] { (0, 0), (0, 1), (0, 2), (1, 1), (1, 2), (2, 2) };
        var individuales = new List<ResultadoDesvioDto>();
        foreach (var (retiro, entrega) in parejas)
            individuales.Add(await servicio.CalcularAsync(
                viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, retiro, entrega));
        Assert.Equal(12, llamadas.Count);
        llamadas.Clear();

        var preparada = await servicio.PrepararAsync(viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm);
        foreach (var (pareja, individual) in parejas.Zip(individuales))
        {
            var resultado = await preparada.CalcularAsync(pareja.Item1, pareja.Item2);
            Assert.Equal(individual.DistanciaOriginalMetros, resultado.DistanciaOriginalMetros);
            Assert.Equal(individual.DuracionOriginalSegundos, resultado.DuracionOriginalSegundos);
            Assert.Equal(individual.DistanciaConEnvioMetros, resultado.DistanciaConEnvioMetros);
            Assert.Equal(individual.DuracionConEnvioSegundos, resultado.DuracionConEnvioSegundos);
            Assert.Equal(individual.DistanciaAdicionalMetros, resultado.DistanciaAdicionalMetros);
            Assert.Equal(individual.TiempoAdicionalSegundos, resultado.TiempoAdicionalSegundos);
            Assert.Equal(individual.CumpleLimiteDesvio, resultado.CumpleLimiteDesvio);
        }
        Assert.Equal(7, llamadas.Count);
    }

    [Fact]
    public async Task Preparada_CopiaEntradasYTotalesBase_NoCambiaPorMutacionesPosteriores()
    {
        var origen = Punto(1);
        var destino = Punto(2);
        var retiro = Punto(3);
        var entrega = Punto(4);
        var baseDevuelta = Ruta(20000, 1200);
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.SetupSequence(r => r.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseDevuelta)
            .ReturnsAsync(Ruta(19000, 1100))
            .ReturnsAsync(Ruta(19000, 1100));
        var preparada = await new DesviosServicio(proveedor.Object)
            .PrepararAsync(new[] { origen, destino }, retiro, entrega, 0);
        origen.Latitud = 80;
        destino.Longitud = 81;
        retiro.Latitud = 82;
        entrega.Longitud = 83;
        baseDevuelta.DistanciaMetros = 1;
        baseDevuelta.DuracionSegundos = 1;

        var primero = await preparada.CalcularAsync(0, 0);
        primero.DistanciaOriginalMetros = 0;
        var segundo = await preparada.CalcularAsync(0, 0);

        Assert.Equal(20000L, segundo.DistanciaOriginalMetros);
        Assert.Equal(1200L, segundo.DuracionOriginalSegundos);
        Assert.Equal(-1000L, segundo.DistanciaAdicionalMetros);
        Assert.Equal(-100L, segundo.TiempoAdicionalSegundos);
        Assert.True(segundo.CumpleLimiteDesvio);
        proveedor.Verify(r => r.CalcularAsync(It.Is<IReadOnlyList<PuntoRutaDto>>(p =>
            p.Count == 4 && p[0].Latitud == 1 && p[1].Latitud == 3 &&
            p[2].Longitud == 4 && p[3].Longitud == 2), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("http", 1)]
    [InlineData("http", 4)]
    [InlineData("timeout", 1)]
    [InlineData("timeout", 4)]
    public async Task Compatibilidad_ErrorEnBaseOAlternativa_PropagaSinResultadoParcial(string tipo, int falloEn)
    {
        var (viaje, envio, rutas) = Escenario();
        Exception esperado = tipo == "http" ? new HttpRequestException("Fallo simulado.") : new TimeoutException("Timeout simulado.");
        var llamadas = 0;
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.Setup(r => r.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<PuntoRutaDto> puntos, CancellationToken token) =>
            {
                if (++llamadas == falloEn) return Task.FromException<ResultadoRutaDto>(esperado);
                return rutas.CalcularAsync(puntos, token);
            });

        var error = await Record.ExceptionAsync(() =>
            new CompatibilidadServicio(new DesviosServicio(proveedor.Object)).EvaluarAsync(viaje, envio));

        Assert.Same(esperado, error);
        Assert.Equal(falloEn, llamadas);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task Compatibilidad_CancelacionEnBaseOAlternativas_NoDevuelveResultado(int cancelarEn)
    {
        var (viaje, envio, rutas) = Escenario();
        using var cancelacion = new CancellationTokenSource();
        var llamadas = 0;
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.Setup(r => r.CalcularAsync(It.IsAny<IReadOnlyList<PuntoRutaDto>>(), cancelacion.Token))
            .Returns((IReadOnlyList<PuntoRutaDto> puntos, CancellationToken token) =>
            {
                var resultado = rutas.CalcularAsync(puntos, token);
                if (++llamadas == cancelarEn) cancelacion.Cancel();
                return resultado;
            });

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CompatibilidadServicio(new DesviosServicio(proveedor.Object))
                .EvaluarAsync(viaje, envio, cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Equal(cancelarEn, llamadas);
    }

    [Fact]
    public async Task PrepararAsync_CanceladoAntesDeEmpezar_CeroConsultas()
    {
        var (viaje, envio, _) = Escenario();
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DesviosServicio(proveedor.Object).PrepararAsync(viaje.RecorridoBase,
                envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, new CancellationToken(true)));

        proveedor.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Preparada_CancelacionAntesDeAlternativaYTokenIndependiente_NoRecalculaBase()
    {
        var (viaje, envio, _) = Escenario();
        using var preparacion = new CancellationTokenSource();
        using var alternativa = new CancellationTokenSource();
        var proveedor = new Mock<IRutasServicio>(MockBehavior.Strict);
        proveedor.Setup(r => r.CalcularAsync(It.Is<IReadOnlyList<PuntoRutaDto>>(p => p.Count == 4), preparacion.Token))
            .ReturnsAsync(Ruta(20000, 1200));
        proveedor.Setup(r => r.CalcularAsync(It.Is<IReadOnlyList<PuntoRutaDto>>(p => p.Count == 6), alternativa.Token))
            .ReturnsAsync(Ruta(21000, 1300));
        var preparada = await new DesviosServicio(proveedor.Object).PrepararAsync(
            viaje.RecorridoBase, envio.Retiro, envio.Entrega, viaje.DesvioMaxKm, preparacion.Token);
        preparacion.Cancel(); // El contexto no retiene el token de la preparación ya completada.

        var resultado = await preparada.CalcularAsync(0, 1, alternativa.Token);
        alternativa.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => preparada.CalcularAsync(0, 1, alternativa.Token));

        Assert.Equal(1000L, resultado.DistanciaAdicionalMetros);
        Assert.Equal(2, proveedor.Invocations.Count);
    }
}
