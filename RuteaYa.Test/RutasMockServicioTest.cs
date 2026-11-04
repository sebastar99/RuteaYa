using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Test;

public class RutasMockServicioTest
{
    private static PuntoRutaDto Punto(decimal latitud, decimal longitud) => new()
    {
        Latitud = latitud,
        Longitud = longitud
    };

    private static PuntoRutaDto[] Recorrido() => new[]
    {
        Punto(-34.6037m, -58.3816m),
        Punto(-34.7609m, -58.4063m)
    };

    private static ResultadoRutaDto Resultado(long metros = 25000, long segundos = 1800) => new()
    {
        DistanciaMetros = metros,
        DuracionSegundos = segundos
    };

    [Fact]
    public async Task CalcularAsync_RecorridoConocido_DevuelveMetrosYSegundosDeterministas()
    {
        var mock = new RutasMockServicio();
        mock.ConfigurarRecorrido(Recorrido(), Resultado());
        IRutasServicio servicio = mock;

        var primero = await servicio.CalcularAsync(Recorrido());
        var segundo = await servicio.CalcularAsync(Recorrido());

        Assert.Equal(25000L, primero.DistanciaMetros);
        Assert.Equal(1800L, primero.DuracionSegundos);
        Assert.Equal(primero.DistanciaMetros, segundo.DistanciaMetros);
        Assert.Equal(primero.DuracionSegundos, segundo.DuracionSegundos);
    }

    [Fact]
    public async Task CalcularAsync_ParadasEnDistintoOrden_UsaResultadosIndependientes()
    {
        var servicio = new RutasMockServicio();
        var origen = Recorrido()[0];
        var destino = Recorrido()[1];
        var paradaA = Punto(-34.65m, -58.39m);
        var paradaB = Punto(-34.70m, -58.40m);
        var ida = new[] { origen, paradaA, paradaB, destino };
        var otroOrden = new[] { origen, paradaB, paradaA, destino };
        servicio.ConfigurarRecorrido(ida, Resultado(28000, 2100));
        servicio.ConfigurarRecorrido(otroOrden, Resultado(32000, 2600));

        var primero = await servicio.CalcularAsync(ida);
        var segundo = await servicio.CalcularAsync(otroOrden);

        Assert.Equal(28000L, primero.DistanciaMetros);
        Assert.Equal(2100L, primero.DuracionSegundos);
        Assert.Equal(32000L, segundo.DistanciaMetros);
        Assert.Equal(2600L, segundo.DuracionSegundos);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CalcularAsync(new[] { origen, paradaA, destino }));
    }

    [Fact]
    public async Task CalcularAsync_RecorridoInversoNoConfigurado_DevuelveError()
    {
        var servicio = new RutasMockServicio();
        var puntos = Recorrido();
        servicio.ConfigurarRecorrido(puntos, Resultado());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CalcularAsync(puntos.Reverse().ToArray()));
    }

    [Fact]
    public async Task CalcularAsync_RecorridoDesconocido_DevuelveErrorClaro()
    {
        var servicio = new RutasMockServicio();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servicio.CalcularAsync(Recorrido()));

        Assert.Contains("No hay un recorrido configurado", error.Message);
    }

    [Fact]
    public async Task Recorrido_ListaNula_SeRechazaAlConfigurarYCalcular()
    {
        var servicio = new RutasMockServicio();

        Assert.Throws<ArgumentNullException>(() => servicio.ConfigurarRecorrido(null!, Resultado()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => servicio.CalcularAsync(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Recorrido_MenosDeDosPuntos_SeRechazaAlConfigurarYCalcular(int cantidad)
    {
        var servicio = new RutasMockServicio();
        var puntos = Recorrido().Take(cantidad).ToArray();

        Assert.Throws<ArgumentException>(() => servicio.ConfigurarRecorrido(puntos, Resultado()));
        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CalcularAsync(puntos));
    }

    [Fact]
    public async Task Recorrido_PuntoNulo_SeRechazaAlConfigurarYCalcular()
    {
        var servicio = new RutasMockServicio();
        var puntos = new[] { Recorrido()[0], null! };

        Assert.Throws<ArgumentException>(() => servicio.ConfigurarRecorrido(puntos, Resultado()));
        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CalcularAsync(puntos));
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public async Task Recorrido_CoordenadasFueraDeRango_SeRechazaAlConfigurarYCalcular(
        int latitud, int longitud)
    {
        var servicio = new RutasMockServicio();
        var puntos = new[] { Recorrido()[0], Punto(latitud, longitud) };

        Assert.Throws<ArgumentException>(() => servicio.ConfigurarRecorrido(puntos, Resultado()));
        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CalcularAsync(puntos));
    }

    [Fact]
    public async Task CalcularAsync_CoordenadasEnLimitesInclusivos_SonValidas()
    {
        var servicio = new RutasMockServicio();
        var puntos = new[] { Punto(-90, -180), Punto(90, 180) };
        servicio.ConfigurarRecorrido(puntos, Resultado());

        var resultado = await servicio.CalcularAsync(puntos);

        Assert.Equal(25000L, resultado.DistanciaMetros);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void ConfigurarRecorrido_ResultadoNegativo_SeRechaza(long metros, long segundos)
    {
        var servicio = new RutasMockServicio();

        Assert.Throws<ArgumentException>(
            () => servicio.ConfigurarRecorrido(Recorrido(), Resultado(metros, segundos)));
    }

    [Fact]
    public void ConfigurarRecorrido_ResultadoNulo_SeRechaza()
    {
        var servicio = new RutasMockServicio();

        Assert.Throws<ArgumentNullException>(() => servicio.ConfigurarRecorrido(Recorrido(), null!));
    }

    [Fact]
    public async Task ConfigurarRecorrido_MismaSecuencia_ReemplazaResultado()
    {
        var servicio = new RutasMockServicio();
        servicio.ConfigurarRecorrido(Recorrido(), Resultado());
        servicio.ConfigurarRecorrido(Recorrido(), Resultado(26000, 1900));

        var resultado = await servicio.CalcularAsync(Recorrido());

        Assert.Equal(26000L, resultado.DistanciaMetros);
        Assert.Equal(1900L, resultado.DuracionSegundos);
    }

    [Fact]
    public async Task ConfigurarRecorrido_CambiosPosterioresEnDtos_NoAlteranLaConfiguracion()
    {
        var servicio = new RutasMockServicio();
        var puntos = Recorrido();
        var configurado = Resultado();
        servicio.ConfigurarRecorrido(puntos, configurado);
        puntos[0].Latitud = 0;
        configurado.DistanciaMetros = 1;
        configurado.DuracionSegundos = 1;

        var primero = await servicio.CalcularAsync(Recorrido());
        primero.DistanciaMetros = 2;
        primero.DuracionSegundos = 2;
        var segundo = await servicio.CalcularAsync(Recorrido());

        Assert.Equal(25000L, segundo.DistanciaMetros);
        Assert.Equal(1800L, segundo.DuracionSegundos);
    }

    [Fact]
    public async Task CalcularAsync_TokenCancelado_RespetaLaCancelacion()
    {
        var servicio = new RutasMockServicio();
        servicio.ConfigurarRecorrido(Recorrido(), Resultado());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => servicio.CalcularAsync(Recorrido(), new CancellationToken(true)));
    }
}
