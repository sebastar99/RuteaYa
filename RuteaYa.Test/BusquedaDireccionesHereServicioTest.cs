using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RuteaYa.Web.Services;
using Microsoft.Extensions.Logging;

namespace RuteaYa.Test;

public class BusquedaDireccionesHereServicioTest
{
    [Fact]
    public async Task DiagnosticoSeguro_RegistraServicioYHttpSinUrlClaveCuerpoNiExcepcion()
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            { Content = new StringContent("apiKey=" + Key) }));
        using var client = new HttpClient(handler);
        var logger = new LoggerPrueba();
        var servicio = new BusquedaDireccionesHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Key }), logger);
        await Assert.ThrowsAsync<HttpRequestException>(() => servicio.SugerirAsync("calle prueba"));
        var mensaje = Assert.Single(logger.Mensajes);
        Assert.Contains("autocomplete", mensaje);
        Assert.Contains("403", mensaje);
        Assert.DoesNotContain(Key, mensaje);
        Assert.DoesNotContain("https://", mensaje);
        Assert.All(logger.Excepciones, e => Assert.Null(e));
    }

    private sealed class LoggerPrueba : ILogger<BusquedaDireccionesHereServicio>
    {
        public List<string> Mensajes = new(); public List<Exception?> Excepciones = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Mensajes.Add(formatter(state, exception)); Excepciones.Add(exception); }
    }

    private const string Key = "ficticia-para-pruebas+&";
    private const string Id = "here:af:street:prueba+/&?";
    private const string Sugerencias = """
        {"items":[{"id":"here:af:street:prueba+/&?","title":"Av. Rivadavia 1234", "resultType":"houseNumber",
            "address":{"label":"Av. Rivadavia 1234, Morón, Argentina","city":"Morón"}}]}
        """;
    private const string Lookup = """
        {"id":"here:af:street:prueba+/&?","title":"Av. Rivadavia 1234", "resultType":"houseNumber", "houseNumberType":"interpolated",
         "estimatedPointAddress":true,"address":{"label":"Av. Rivadavia 1234, Morón, Argentina"},
         "position":{"lat":-34.6,"lng":-58.4},"access":[{"lat":-34.61,"lng":-58.41},{"lat":-34.62,"lng":-58.42}]}
        """;

    [Fact]
    public async Task Sugerir_EndpointOficialSinCoordenadasArtificiales_PreservaNombreYLocalidad()
    {
        using var handler = new Handler((r, _) =>
        {
            Assert.Equal("https://autocomplete.search.hereapi.com/v1/autocomplete", r.RequestUri!.GetLeftPart(UriPartial.Path));
            var q = QueryHelpers.ParseQuery(r.RequestUri.Query);
            Assert.Equal("Rivadavia & Morón", q["q"].ToString());
            Assert.Equal("5", q["limit"].ToString());
            Assert.Equal("es", q["lang"].ToString());
            Assert.Equal(Key, q["apiKey"].ToString());
            Assert.False(q.ContainsKey("at"));
            return Task.FromResult(Json(Sugerencias));
        });
        using var client = new HttpClient(handler);
        var resultado = Assert.Single(await Servicio(client).SugerirAsync(" Rivadavia & Morón "));
        Assert.Equal(Id, resultado.IdHere);
        Assert.Equal("Av. Rivadavia 1234", resultado.Nombre);
        Assert.Equal("Morón", resultado.Localidad);
        Assert.Equal("Av. Rivadavia 1234, Morón, Argentina", resultado.Direccion);
    }

    [Fact]
    public async Task Resolver_UsaLookupPorId_PreservaPositionAccessYCalidadSinConfirmar()
    {
        using var handler = new Handler((r, _) =>
        {
            Assert.Equal("https://lookup.search.hereapi.com/v1/lookup", r.RequestUri!.GetLeftPart(UriPartial.Path));
            Assert.Equal(Id, QueryHelpers.ParseQuery(r.RequestUri.Query)["id"].ToString());
            return Task.FromResult(Json(Lookup));
        });
        using var client = new HttpClient(handler);
        var resultado = await Servicio(client).ResolverAsync(Id);
        Assert.Equal(Id, resultado.IdHere);
        Assert.Equal(-34.6m, resultado.Posicion.Latitud);
        Assert.Equal(new[] { -34.61m, -34.62m }, resultado.Accesos.Select(p => p.Latitud));
        Assert.Equal("interpolated", resultado.TipoNumeroPuerta);
        Assert.True(resultado.PosicionEstimada);
        Assert.True(resultado.RequiereConfirmacion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("---")]
    [InlineData("calle\n123")]
    public async Task Sugerir_TextoInvalido_NoEnviaHttp(string texto)
    {
        using var handler = new Handler((_, _) => throw new Exception("No debe consultar"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(client).SugerirAsync(texto));
        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task Sugerir_TextoDemasiadoLargo_NoEnviaHttp()
    {
        using var handler = new Handler((_, _) => throw new Exception("No debe consultar"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(client).SugerirAsync(new string('a', 201)));
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://no-permitido/1")]
    [InlineData("id-arbitrario")]
    [InlineData("here:abc\n")]
    public async Task Resolver_IdInvalido_NoEnviaHttp(string id)
    {
        using var handler = new Handler((_, _) => throw new Exception("No debe consultar"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(client).ResolverAsync(id));
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaveAusente_NoEnviaHttp(bool lookup)
    {
        using var handler = new Handler((_, _) => throw new Exception("No debe consultar"));
        using var client = new HttpClient(handler);
        var servicio = new BusquedaDireccionesHereServicio(client, Options.Create(new HereRoutingOpciones()));
        if (lookup) await Assert.ThrowsAsync<HereNoConfiguradoException>(() => servicio.ResolverAsync(Id));
        else await Assert.ThrowsAsync<HereNoConfiguradoException>(() => servicio.SugerirAsync("Rivadavia"));
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(false, 302)]
    [InlineData(false, 401)]
    [InlineData(false, 429)]
    [InlineData(true, 404)]
    [InlineData(true, 500)]
    public async Task HttpFallido_NoExponeClaveCuerpoNiUrl(bool lookup, int estado)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)estado)
            { Content = new StringContent(Key) }));
        using var client = new HttpClient(handler);
        var servicio = Servicio(client);
        var error = lookup ? await Assert.ThrowsAsync<HttpRequestException>(() => servicio.ResolverAsync(Id)) :
            await Assert.ThrowsAsync<HttpRequestException>(() => servicio.SugerirAsync("Rivadavia"));
        Assert.Equal((HttpStatusCode)estado, error.StatusCode);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConexionFallida_NoExponeExcepcionInterna(bool lookup)
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("https://here/?apiKey=" + Key));
        using var client = new HttpClient(handler);
        var servicio = Servicio(client);
        var error = lookup ? await Assert.ThrowsAsync<HttpRequestException>(() => servicio.ResolverAsync(Id)) :
            await Assert.ThrowsAsync<HttpRequestException>(() => servicio.SugerirAsync("Rivadavia"));
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Theory]
    [InlineData(false, "{")]
    [InlineData(false, "{}")]
    [InlineData(false, "{\"items\":[{\"id\":\"here:x\"}]}")]
    [InlineData(true, "{}")]
    [InlineData(true, "{\"items\":[]}")]
    public async Task JsonIncompleto_NoInventaUbicacion(bool lookup, string json)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(json)));
        using var client = new HttpClient(handler);
        var servicio = Servicio(client);
        if (lookup) await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ResolverAsync(Id));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.SugerirAsync("Rivadavia"));
    }

    [Fact]
    public async Task Lookup_IdDistintoOCoordenadasInvalidas_SeRechaza()
    {
        foreach (var json in new[] { Lookup.Replace(Id, "here:otro"), Lookup.Replace("-34.6,", "-94.6,") })
        {
            using var handler = new Handler((_, _) => Task.FromResult(Json(json)));
            using var client = new HttpClient(handler);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(client).ResolverAsync(Id));
        }
    }

    [Fact]
    public async Task SinResultados_DevuelveListaVacia()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("{\"items\":[]}")));
        using var client = new HttpClient(handler);
        Assert.Empty(await Servicio(client).SugerirAsync("Rivadavia"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelacionDelLlamador_PropagaTokenSinFiltrarUrl(bool lookup)
    {
        using var cts = new CancellationTokenSource();
        using var handler = new Handler(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Json(Lookup);
        });
        using var client = new HttpClient(handler);
        var servicio = Servicio(client);
        var error = lookup ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => servicio.ResolverAsync(Id, cts.Token)) :
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => servicio.SugerirAsync("Rivadavia", cts.Token));
        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.DoesNotContain(Key, error.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_NoLoConfundeConCancelacionDelUsuario(bool lookup)
    {
        using var handler = new Handler((_, _) => throw new TaskCanceledException(Key));
        using var client = new HttpClient(handler);
        var servicio = Servicio(client);
        var error = lookup ? await Assert.ThrowsAsync<TimeoutException>(() => servicio.ResolverAsync(Id)) :
            await Assert.ThrowsAsync<TimeoutException>(() => servicio.SugerirAsync("Rivadavia"));
        Assert.DoesNotContain(Key, error.ToString());
    }

    private static BusquedaDireccionesHereServicio Servicio(HttpClient client) => new(client, Options.Create(new HereRoutingOpciones { ApiKey = Key }));
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        public int Llamadas { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Llamadas++; return response(r, ct); }
    }
}
