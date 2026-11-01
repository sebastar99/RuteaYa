using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

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
        CuposDisponibles = 1
    };

    private static EnvioCompatibilidadDto Envio() => new()
    {
        EnvioId = 20,
        Retiro = Punto(-34.66m, -58.63m),
        Entrega = Punto(-34.57m, -59.12m),
        PesoKg = 2,
        Estado = EstadoEnvio.BuscandoConductor
    };

    [Fact]
    public async Task EvaluarAsync_ReglasPreviasCumplidas_IndeterminadoConEvaluacionesPendientes()
    {
        ICompatibilidadServicio servicio = new CompatibilidadServicio();

        var resultado = await servicio.EvaluarAsync(Viaje(), Envio());

        Assert.Equal(10, resultado.ViajeId);
        Assert.Equal(20, resultado.EnvioId);
        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(new[]
        {
            MotivoCompatibilidad.DesvioNoEvaluado,
            MotivoCompatibilidad.HorarioNoEvaluado
        }, resultado.Motivos);
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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(Viaje(), envio);

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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, envio);

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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(Viaje(), envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.EnvioNoDisponible, resultado.Motivos);
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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, Envio());

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(motivo, resultado.Motivos);
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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, Envio());

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Equal(pesoDesconocido,
            resultado.Motivos.Contains(MotivoCompatibilidad.PesoDisponibleDesconocido));
        Assert.Equal(cuposDesconocidos,
            resultado.Motivos.Contains(MotivoCompatibilidad.CuposDisponiblesDesconocidos));
        Assert.DoesNotContain(MotivoCompatibilidad.SinCupo, resultado.Motivos);
        Assert.DoesNotContain(MotivoCompatibilidad.PesoExcedido, resultado.Motivos);
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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Incompatible, resultado.Estado);
        Assert.Contains(motivo, resultado.Motivos);
        Assert.True(resultado.Motivos.Contains(MotivoCompatibilidad.PesoDisponibleDesconocido) ||
            resultado.Motivos.Contains(MotivoCompatibilidad.CuposDisponiblesDesconocidos));
        Assert.Contains(MotivoCompatibilidad.DesvioNoEvaluado, resultado.Motivos);
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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, envio);

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
    public async Task EvaluarAsync_FechasOpcionales_NulosSonValidosYSiguePendienteElHorario(
        bool salida, bool llegada, bool desde, bool limite)
    {
        var viaje = Viaje();
        var envio = Envio();
        var fecha = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(-3));
        viaje.SalidaProgramada = salida ? fecha : null;
        viaje.LlegadaMaxima = llegada ? fecha.AddHours(2) : null;
        envio.DisponibleDesde = desde ? fecha : null;
        envio.FechaLimiteEntrega = limite ? fecha.AddHours(1) : null;

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
        Assert.Contains(MotivoCompatibilidad.HorarioNoEvaluado, resultado.Motivos);
    }

    [Fact]
    public async Task EvaluarAsync_LimitesTemporalesEnMismoInstanteConDistintoOffset_SonCoherentes()
    {
        var viaje = Viaje();
        var envio = Envio();
        var fecha = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(-3));
        viaje.SalidaProgramada = fecha;
        viaje.LlegadaMaxima = fecha.ToUniversalTime();
        envio.DisponibleDesde = fecha;
        envio.FechaLimiteEntrega = fecha.ToUniversalTime();

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
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
            viaje.LlegadaMaxima = fecha.AddSeconds(-1);
        }
        else
        {
            envio.DisponibleDesde = fecha;
            envio.FechaLimiteEntrega = fecha.AddSeconds(-1);
        }

        await Assert.ThrowsAsync<ArgumentException>(
            () => new CompatibilidadServicio().EvaluarAsync(viaje, envio));
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
            () => new CompatibilidadServicio().EvaluarAsync(viaje, envio));
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
            () => new CompatibilidadServicio().EvaluarAsync(viaje, envio));
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

        var resultado = await new CompatibilidadServicio().EvaluarAsync(viaje, envio);

        Assert.Equal(EstadoCompatibilidad.Indeterminado, resultado.Estado);
    }

    [Fact]
    public async Task EvaluarAsync_ObjetosNulos_SeRechazan()
    {
        var servicio = new CompatibilidadServicio();

        await Assert.ThrowsAsync<ArgumentNullException>(() => servicio.EvaluarAsync(null!, Envio()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => servicio.EvaluarAsync(Viaje(), null!));
    }

    [Fact]
    public async Task EvaluarAsync_Cancelacion_PropagaElToken()
    {
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CompatibilidadServicio().EvaluarAsync(Viaje(), Envio(), cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
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
        var servicio = new CompatibilidadServicio();

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
