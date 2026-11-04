using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class RutasHereServicioTest
{
    private const string ClaveSimulada = "clave-ficticia-solo-para-tests";
    private const string RutaValida = """
        {"routes":[{"sections":[{"summary":{"length":1500,"duration":120}}]}]}
        """;

    private static PuntoRutaDto Punto(decimal latitud, decimal longitud) => new()
    {
        Latitud = latitud,
        Longitud = longitud
    };

    private static PuntoRutaDto[] Puntos() => new[]
    {
        Punto(-34.60m, -58.38m), Punto(-34.76m, -58.40m)
    };

    private static HttpResponseMessage Respuesta(string json, HttpStatusCode estado = HttpStatusCode.OK) => new(estado)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static RutasHereServicio Servicio(HttpClient cliente, string clave = ClaveSimulada) =>
        new(cliente, Options.Create(new HereRoutingOpciones { ApiKey = clave }));

    [Fact]
    public async Task CalcularAsync_OrigenYDestino_UsaContratoV8YDevuelveMetrosYSegundos()
    {
        using var handler = new HandlerSimulado((solicitud, _) =>
        {
            Assert.Equal(HttpMethod.Get, solicitud.Method);
            Assert.Equal("https", solicitud.RequestUri!.Scheme);
            Assert.Equal("router.hereapi.com", solicitud.RequestUri.Host);
            Assert.Equal("/v8/routes", solicitud.RequestUri.AbsolutePath);
            var query = QueryHelpers.ParseQuery(solicitud.RequestUri.Query);
            Assert.Equal("car", query["transportMode"].ToString());
            Assert.Equal("fast", query["routingMode"].ToString());
            Assert.Equal("any", query["departureTime"].ToString());
            Assert.Equal("summary", query["return"].ToString());
            Assert.Equal("0", query["alternatives"].ToString());
            Assert.Equal("-34.60,-58.38", query["origin"].ToString());
            Assert.Equal("-34.76,-58.40", query["destination"].ToString());
            Assert.False(query.ContainsKey("via"));
            Assert.Equal(ClaveSimulada, query["apiKey"].ToString());
            Assert.Null(solicitud.Headers.Authorization);
            return Task.FromResult(Respuesta(RutaValida));
        });
        using var cliente = new HttpClient(handler);
        IRutasServicio servicio = Servicio(cliente);

        var resultado = await servicio.CalcularAsync(Puntos());

        Assert.Equal(1500L, resultado.DistanciaMetros);
        Assert.Equal(120L, resultado.DuracionSegundos);
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task CalcularAsync_ParadasOrdenadas_SumaTodasLasSeccionesDeLaPrimeraRuta()
    {
        using var handler = new HandlerSimulado((solicitud, _) =>
        {
            var query = QueryHelpers.ParseQuery(solicitud.RequestUri!.Query);
            Assert.Equal(new[] { "-34.62,-58.37", "-34.65,-58.39" }, query["via"].ToArray());
            return Task.FromResult(Respuesta("""
                {"routes":[
                    {"sections":[
                        {"summary":{"length":1000,"duration":100,"baseDuration":80}},
                        {"summary":{"length":2000,"duration":200}},
                        {"summary":{"length":3000,"duration":300}}]},
                    {"sections":[{"summary":{"length":99999,"duration":9999}}]}
                ]}
                """));
        });
        using var cliente = new HttpClient(handler);
        var servicio = Servicio(cliente);
        var extremos = Puntos();

        var resultado = await servicio.CalcularAsync(new[]
        {
            extremos[0], Punto(-34.62m, -58.37m), Punto(-34.65m, -58.39m), extremos[1]
        });

        Assert.Equal(6000L, resultado.DistanciaMetros);
        Assert.Equal(600L, resultado.DuracionSegundos);
    }

    [Fact]
    public async Task CalcularAsync_CulturaConComaDecimalYClaveConReservados_CodificaCorrectamente()
    {
        using var handler = new HandlerSimulado((solicitud, _) =>
        {
            var query = QueryHelpers.ParseQuery(solicitud.RequestUri!.Query);
            Assert.Equal("-34.60,-58.38", query["origin"].ToString());
            Assert.Equal("simulada+&=?/#", query["apiKey"].ToString());
            return Task.FromResult(Respuesta(RutaValida));
        });
        using var cliente = new HttpClient(handler);
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
            await Servicio(cliente, "simulada+&=?/#").CalcularAsync(Puntos());
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task CalcularAsync_HttpFallido_ConservaEstadoSinExponerCuerpoNiClave(int estado)
    {
        using var handler = new HandlerSimulado((_, _) =>
            Task.FromResult(Respuesta(ClaveSimulada, (HttpStatusCode)estado)));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Servicio(cliente).CalcularAsync(Puntos()));

        Assert.Equal((HttpStatusCode)estado, error.StatusCode);
        Assert.DoesNotContain(ClaveSimulada, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"routes\":[]}")]
    [InlineData("{\"routes\":[null]}")]
    [InlineData("{\"routes\":[{}]}")]
    [InlineData("{\"routes\":[{\"sections\":[]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[null]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"summary\":{\"length\":1}}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"summary\":{\"duration\":1}}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"summary\":{\"length\":-1,\"duration\":1}}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"summary\":{\"length\":1,\"duration\":-1}}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"summary\":{\"length\":\"uno\",\"duration\":1}}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"summary\":{\"length\":1.5,\"duration\":1}}]}]}")]
    [InlineData("{\"error\":\"error de HERE\"}")]
    public async Task CalcularAsync_RespuestaInvalidaOIncompleta_NoInventaTotales(string json)
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(json)));
        using var cliente = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(cliente).CalcularAsync(Puntos()));
    }

    [Theory]
    [InlineData("{\"notices\":[{\"code\":\"noRouteFound\"}],\"routes\":[]}")]
    [InlineData("{\"notices\":[{\"severity\":\"critical\"}],\"routes\":[]}")]
    [InlineData("{\"routes\":[{\"notices\":[{\"severity\":\"critical\"}],\"sections\":[{\"summary\":{\"length\":1,\"duration\":1}}]}]}")]
    [InlineData("{\"routes\":[{\"sections\":[{\"notices\":[{\"severity\":\"critical\"}],\"summary\":{\"length\":1,\"duration\":1}}]}]}")]
    public async Task CalcularAsync_AvisoCriticoOErrorHere_RechazaRuta(string json)
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(json)));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Servicio(cliente).CalcularAsync(Puntos()));

        Assert.Contains("crítica", error.Message);
    }

    [Fact]
    public async Task CalcularAsync_AvisoInformativoYTotalesCero_AceptaRespuesta()
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta("""
            {"notices":[{"severity":"info","code":"informacion"}],
             "routes":[{"sections":[{"summary":{"length":0,"duration":0}}]}]}
            """)));
        using var cliente = new HttpClient(handler);

        var resultado = await Servicio(cliente).CalcularAsync(new[] { Punto(-90, -180), Punto(90, 180) });

        Assert.Equal(0L, resultado.DistanciaMetros);
        Assert.Equal(0L, resultado.DuracionSegundos);
    }

    [Fact]
    public async Task CalcularAsync_SumaDesbordaLong_DevuelveError()
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta("""
            {"routes":[{"sections":[
                {"summary":{"length":9223372036854775807,"duration":1}},
                {"summary":{"length":1,"duration":1}}
            ]}]}
            """)));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Servicio(cliente).CalcularAsync(Puntos()));

        Assert.Contains("rango", error.Message);
    }

    public static IEnumerable<object[]> PuntosInvalidos()
    {
        yield return new object[] { null! };
        yield return new object[] { Array.Empty<PuntoRutaDto>() };
        yield return new object[] { new[] { Puntos()[0] } };
        yield return new object[] { new[] { Puntos()[0], null! } };
        foreach (var punto in new[] { Punto(-91, 0), Punto(91, 0), Punto(0, -181), Punto(0, 181) })
            yield return new object[] { new[] { Puntos()[0], punto, Puntos()[1] } };
    }

    [Theory]
    [MemberData(nameof(PuntosInvalidos))]
    public async Task CalcularAsync_PuntosInvalidos_NoEnviaHttp(IReadOnlyList<PuntoRutaDto> puntos)
    {
        using var handler = new HandlerSimulado((_, _) => throw new InvalidOperationException("No debe llamar HTTP."));
        using var cliente = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => Servicio(cliente).CalcularAsync(puntos));

        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task CalcularAsync_SinApiKey_FallaSinEnviarHttp(string? clave)
    {
        using var handler = new HandlerSimulado((_, _) => throw new InvalidOperationException("No debe llamar HTTP."));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Servicio(cliente, clave!).CalcularAsync(Puntos()));

        Assert.Contains("HereRouting:ApiKey", error.Message);
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task CalcularAsync_ErrorDeRed_NoFiltraUrlNiExcepcionInterna()
    {
        using var handler = new HandlerSimulado((_, _) =>
            throw new HttpRequestException($"Fallo en https://router.hereapi.com/?apiKey={ClaveSimulada}"));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Servicio(cliente).CalcularAsync(Puntos()));

        Assert.DoesNotContain(ClaveSimulada, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task CalcularAsync_TokenCancelado_NoEnviaHttp()
    {
        using var handler = new HandlerSimulado((_, _) => throw new InvalidOperationException("No debe llamar HTTP."));
        using var cliente = new HttpClient(handler);
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Servicio(cliente).CalcularAsync(Puntos(), cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task CalcularAsync_CancelacionDuranteHttp_PropagaTokenDelLlamador()
    {
        using var cancelacion = new CancellationTokenSource();
        using var handler = new HandlerSimulado((_, token) =>
        {
            Assert.True(token.CanBeCanceled);
            cancelacion.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        });
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Servicio(cliente).CalcularAsync(Puntos(), cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
    }

    [Fact]
    public async Task CalcularAsync_TimeoutHttp_DevuelveErrorClaro()
    {
        // Simula el vencimiento que HttpClient informa sin cancelar el token del llamador.
        using var handler = new HandlerSimulado((_, _) => throw new TaskCanceledException("Timeout simulado."));
        using var cliente = new HttpClient(handler);

        await Assert.ThrowsAsync<TimeoutException>(() => Servicio(cliente).CalcularAsync(Puntos()));
    }

    [Fact]
    public async Task CalcularAsync_UsadoPorDesviosServicio_ComparaRutasConHereSimulado()
    {
        using var handler = new HandlerSimulado((solicitud, _) =>
        {
            var query = QueryHelpers.ParseQuery(solicitud.RequestUri!.Query);
            return Task.FromResult(Respuesta(query.ContainsKey("via")
                ? """{"routes":[{"sections":[{"summary":{"length":2500,"duration":220}}]}]}"""
                : RutaValida));
        });
        using var cliente = new HttpClient(handler);
        var desvios = new DesviosServicio(Servicio(cliente));

        var resultado = await desvios.CalcularAsync(
            Puntos(), Punto(-34.65m, -58.39m), Punto(-34.70m, -58.41m), 1, 0, 0);

        Assert.Equal(1000L, resultado.DistanciaAdicionalMetros);
        Assert.Equal(100L, resultado.TiempoAdicionalSegundos);
        Assert.True(resultado.CumpleLimiteDesvio);
        Assert.Equal(2, handler.Llamadas);
    }

    private sealed class HandlerSimulado : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        public int Llamadas { get; private set; }

        public HandlerSimulado(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            return _responder(request, cancellationToken);
        }
    }
}
