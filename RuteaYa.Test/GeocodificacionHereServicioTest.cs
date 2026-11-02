using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RuteaYa.Logica;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class GeocodificacionHereServicioTest
{
    private const string ClaveSimulada = "clave-ficticia-geocodificacion-tests";
    private const string ItemValido = """
        {
            "id":"here:simulado:direccion-1", "resultType":"houseNumber", "houseNumberType":"PA",
            "address":{"label":"Belgrano 100, Morón, Buenos Aires, Argentina","city":"Morón"},
            "position":{"lat":-34.6509,"lng":-58.6198},
            "access":[{"lat":-34.6510,"lng":-58.6199},{"lat":-34.6511,"lng":-58.6200}],
            "scoring":{"queryScore":0.98,"fieldScore":{"city":1,"streets":[1,0.9],"houseNumber":1}}
        }
        """;

    private static HttpResponseMessage Respuesta(string json, HttpStatusCode estado = HttpStatusCode.OK) => new(estado)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static GeocodificacionHereServicio Servicio(HttpClient cliente, string? clave = ClaveSimulada) =>
        new(cliente, Options.Create(new HereRoutingOpciones { ApiKey = clave! }));

    private static string ConItem(string item) => "{\"items\":[" + item + "]}";

    [Fact]
    public async Task GeocodificarAsync_DireccionConReservados_CodificaQYUsaEndpointOficial()
    {
        const string direccion = "Av. Rivadavia 18 & España + Nº 1, Morón? #entrada";
        using var handler = new HandlerSimulado((solicitud, _) =>
        {
            Assert.Equal(HttpMethod.Get, solicitud.Method);
            Assert.Equal("https", solicitud.RequestUri!.Scheme);
            Assert.Equal("geocode.search.hereapi.com", solicitud.RequestUri.Host);
            Assert.Equal("/v1/geocode", solicitud.RequestUri.AbsolutePath);
            var query = QueryHelpers.ParseQuery(solicitud.RequestUri.Query);
            Assert.Equal(direccion, query["q"].ToString());
            Assert.Equal("normalized", query["addressNamesMode"].ToString());
            Assert.Equal("simulada+&=?/#", query["apiKey"].ToString());
            Assert.False(query.ContainsKey("limit"));
            Assert.False(query.ContainsKey("types"));
            Assert.Equal(3, query.Count);
            Assert.Null(solicitud.Headers.Authorization);
            return Task.FromResult(Respuesta(ConItem(ItemValido)));
        });
        using var cliente = new HttpClient(handler);
        IGeocodificacionServicio servicio = Servicio(cliente, "simulada+&=?/#");

        var resultados = await servicio.GeocodificarAsync("  " + direccion + "  ");

        Assert.Single(resultados);
        Assert.Equal(1, handler.Llamadas);
    }

    [Fact]
    public async Task GeocodificarAsync_ItemCompleto_SeparaPosicionYAccesosYConservaCalidad()
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(ConItem(ItemValido))));
        using var cliente = new HttpClient(handler);

        var resultados = await Servicio(cliente).GeocodificarAsync("Belgrano 100, Morón");

        var candidato = Assert.Single(resultados);
        Assert.Equal("here:simulado:direccion-1", candidato.IdHere);
        Assert.Equal("Belgrano 100, Morón, Buenos Aires, Argentina", candidato.DireccionNormalizada);
        Assert.Equal(-34.6509m, candidato.Posicion.Latitud);
        Assert.Equal(-58.6198m, candidato.Posicion.Longitud);
        Assert.Equal(2, candidato.Accesos.Count);
        Assert.Equal(-34.6510m, candidato.Accesos[0].Latitud);
        Assert.Equal(-58.6199m, candidato.Accesos[0].Longitud);
        Assert.Equal(-34.6511m, candidato.Accesos[1].Latitud);
        Assert.Equal("houseNumber", candidato.TipoResultado);
        Assert.Equal("PA", candidato.TipoNumeroPuerta);
        Assert.False(candidato.NumeroPuertaCorregido);
        Assert.False(candidato.PosicionEstimada);
        Assert.Equal(0.98m, candidato.PuntajeConsulta);
        Assert.Equal(new[] { 1m }, candidato.PuntajesCampos["city"]);
        Assert.Equal(new[] { 1m, 0.9m }, candidato.PuntajesCampos["streets"]);
        Assert.False(candidato.PuntajesCampos.ContainsKey("postalCode"));
        Assert.True(candidato.RequiereConfirmacion);
    }

    [Fact]
    public async Task GeocodificarAsync_DosCoincidenciasConPuntajeUno_ConservaAmbasSinElegir()
    {
        var primero = JsonNode.Parse(ItemValido)!;
        primero["scoring"]!["queryScore"] = 1;
        var segundo = primero.DeepClone();
        segundo["id"] = "here:simulado:direccion-2";
        segundo["address"]!["label"] = "Belgrano 100, Luján, Buenos Aires, Argentina";
        segundo["position"]!["lat"] = -34.5644m;
        var json = new JsonObject { ["items"] = new JsonArray(primero, segundo) }.ToJsonString();
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(json)));
        using var cliente = new HttpClient(handler);

        var candidatos = await Servicio(cliente).GeocodificarAsync("Belgrano 100");

        Assert.Equal(2, candidatos.Count);
        Assert.Equal("here:simulado:direccion-1", candidatos[0].IdHere);
        Assert.Equal("here:simulado:direccion-2", candidatos[1].IdHere);
        Assert.All(candidatos, c => Assert.True(c.RequiereConfirmacion));
    }

    [Theory]
    [InlineData("street", null)]
    [InlineData("locality", null)]
    [InlineData("houseNumber", "interpolated")]
    [InlineData("houseNumber", "PA")]
    [InlineData("tipoFuturo", "subtipoFuturo")]
    public async Task GeocodificarAsync_GranularidadDistintaOSinCalidad_NoAfirmaExactitud(
        string tipo, string? tipoNumero)
    {
        var item = JsonNode.Parse(ItemValido)!;
        item["resultType"] = tipo;
        item["houseNumberType"] = tipoNumero;
        item["scoring"] = null;
        item["access"] = null;
        item["houseNumberFallback"] = true;
        item["estimatedPointAddress"] = true;
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(ConItem(item.ToJsonString()))));
        using var cliente = new HttpClient(handler);

        var candidato = Assert.Single(await Servicio(cliente).GeocodificarAsync("Belgrano, Morón"));

        Assert.Equal(tipo, candidato.TipoResultado);
        Assert.Equal(tipoNumero, candidato.TipoNumeroPuerta);
        Assert.True(candidato.NumeroPuertaCorregido);
        Assert.True(candidato.PosicionEstimada);
        Assert.True(candidato.RequiereConfirmacion);
        Assert.Null(candidato.PuntajeConsulta);
        Assert.Empty(candidato.PuntajesCampos);
        Assert.Empty(candidato.Accesos);
        Assert.Equal(-34.6509m, candidato.Posicion.Latitud);
    }

    [Fact]
    public async Task GeocodificarAsync_SinCamposOpcionales_AceptaCandidatoConConfirmacion()
    {
        const string json = """
            {"items":[{"id":"here:simulado:area","resultType":"locality",
            "address":{"label":"Morón, Argentina"},"position":{"lat":-90,"lng":180}}]}
            """;
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(json)));
        using var cliente = new HttpClient(handler);

        var candidato = Assert.Single(await Servicio(cliente).GeocodificarAsync("Morón"));

        Assert.Empty(candidato.Accesos);
        Assert.Null(candidato.PuntajeConsulta);
        Assert.Null(candidato.TipoNumeroPuerta);
        Assert.True(candidato.RequiereConfirmacion);
        Assert.Equal(-90m, candidato.Posicion.Latitud);
        Assert.Equal(180m, candidato.Posicion.Longitud);
    }

    [Fact]
    public async Task GeocodificarAsync_SinResultados_DevuelveListaVacia()
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta("{\"items\":[]}")));
        using var cliente = new HttpClient(handler);

        Assert.Empty(await Servicio(cliente).GeocodificarAsync("Dirección sin coincidencias"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData("Morón\nLuján")]
    [InlineData("Morón\0")]
    public async Task GeocodificarAsync_EntradaInvalida_NoEnviaHttp(string? direccion)
    {
        using var handler = new HandlerSimulado((_, _) => throw new InvalidOperationException("No debe enviar HTTP."));
        using var cliente = new HttpClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(cliente).GeocodificarAsync(direccion!));

        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GeocodificarAsync_ClaveAusente_NoEnviaHttp(string? clave)
    {
        using var handler = new HandlerSimulado((_, _) => throw new InvalidOperationException("No debe enviar HTTP."));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Servicio(cliente, clave).GeocodificarAsync("Morón"));

        Assert.Contains("HereRouting:ApiKey", error.Message);
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task GeocodificarAsync_HttpFallido_ConservaEstadoSinFiltrarDatos(int estado)
    {
        using var handler = new HandlerSimulado((_, _) =>
            Task.FromResult(Respuesta(ClaveSimulada + " dirección privada", (HttpStatusCode)estado)));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Servicio(cliente).GeocodificarAsync("dirección privada"));

        Assert.Equal((HttpStatusCode)estado, error.StatusCode);
        Assert.DoesNotContain(ClaveSimulada, error.ToString());
        Assert.DoesNotContain("dirección privada", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("JSON inválido")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":{}}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{}]}")]
    [InlineData("{\"status\":400,\"title\":\"error simulado\"}")]
    public async Task GeocodificarAsync_JsonInvalidoOItemsAusentes_DevuelveErrorSeguro(string json)
    {
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(json)));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Servicio(cliente).GeocodificarAsync("Morón"));

        Assert.Contains("inválida o incompleta", error.Message);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData("id", "null")]
    [InlineData("resultType", "\"\"")]
    [InlineData("address", "null")]
    [InlineData("address.label", "null")]
    [InlineData("position", "null")]
    [InlineData("position.lat", "null")]
    [InlineData("position.lng", "null")]
    [InlineData("position.lat", "91")]
    [InlineData("position.lat", "-91")]
    [InlineData("position.lng", "181")]
    [InlineData("position.lng", "-181")]
    [InlineData("position.lat", "\"no-numérico\"")]
    [InlineData("access", "[null]")]
    [InlineData("access", "[{}]")]
    [InlineData("access", "[{\"lat\":-34,\"lng\":181}]")]
    [InlineData("access", "[{\"lat\":-91,\"lng\":-58}]")]
    [InlineData("scoring.queryScore", "1.1")]
    [InlineData("scoring.queryScore", "-0.1")]
    [InlineData("scoring.fieldScore.city", "2")]
    [InlineData("scoring.fieldScore.streets", "[1,-1]")]
    [InlineData("scoring.fieldScore.city", "\"inválido\"")]
    [InlineData("scoring.fieldScore.city", "null")]
    public async Task GeocodificarAsync_CandidatoMalformado_RechazaSinOcultarAmbiguedad(
        string rutaCampo, string valorJson)
    {
        var item = JsonNode.Parse(ItemValido)!;
        var campos = rutaCampo.Split('.');
        var padre = item;
        foreach (var campo in campos.SkipLast(1))
            padre = padre[campo]!;
        padre[campos[^1]] = JsonNode.Parse(valorJson);
        // Un candidato válido seguido de uno inválido no debe convertirse en una coincidencia única.
        var json = "{\"items\":[" + ItemValido + "," + item.ToJsonString() + "]}";
        using var handler = new HandlerSimulado((_, _) => Task.FromResult(Respuesta(json)));
        using var cliente = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(cliente).GeocodificarAsync("Morón"));
    }

    [Fact]
    public async Task GeocodificarAsync_ErrorDeRed_NoExponeUrlConsultaNiClave()
    {
        using var handler = new HandlerSimulado((_, _) =>
            throw new HttpRequestException("URL privada con " + ClaveSimulada));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Servicio(cliente).GeocodificarAsync("dirección privada"));

        Assert.DoesNotContain(ClaveSimulada, error.ToString());
        Assert.DoesNotContain("dirección privada", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task GeocodificarAsync_CanceladoAntesDeEmpezar_NoEnviaHttp()
    {
        using var handler = new HandlerSimulado((_, _) => throw new InvalidOperationException("No debe enviar HTTP."));
        using var cliente = new HttpClient(handler);
        using var cancelacion = new CancellationTokenSource();
        cancelacion.Cancel();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Servicio(cliente).GeocodificarAsync("Morón", cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task GeocodificarAsync_CanceladoDuranteHttp_PropagaTokenDelLlamador()
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
            () => Servicio(cliente).GeocodificarAsync("Morón", cancelacion.Token));

        Assert.Equal(cancelacion.Token, error.CancellationToken);
    }

    [Fact]
    public async Task GeocodificarAsync_Timeout_DaErrorSeguro()
    {
        using var handler = new HandlerSimulado((_, _) => throw new TaskCanceledException(ClaveSimulada));
        using var cliente = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<TimeoutException>(
            () => Servicio(cliente).GeocodificarAsync("Morón"));

        Assert.DoesNotContain(ClaveSimulada, error.ToString());
        Assert.Null(error.InnerException);
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
