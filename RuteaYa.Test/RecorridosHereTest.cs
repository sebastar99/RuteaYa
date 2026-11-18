using System.Net;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;

namespace RuteaYa.Test;

public class RecorridosHereTest
{
    private const string EjemploOficial = "BFoz5xJ67i1B1B7PzIhaxL7Y";
    private const string Clave = "ficticia-no-es-un-secreto";

    [Fact]
    public void FlexiblePolyline_EjemploOficial_PreservaPrecisionYTodosLosVertices()
    {
        var puntos = FlexiblePolyline.Decodificar(EjemploOficial);
        Assert.Equal(new[] { 50.10228m, 50.10201m, 50.10063m, 50.09878m }, puntos.Select(p => p.Latitud));
        Assert.Equal(new[] { 8.69821m, 8.69567m, 8.69150m, 8.68752m }, puntos.Select(p => p.Longitud));
    }

    [Fact]
    public void FlexiblePolyline_DimensionAdicional_NoLaConfundeConLatitudOLongitud()
    {
        // Precisión 0, altitude: (1,2,10), (2,3,11).
        var puntos = FlexiblePolyline.Decodificar("BgBCEUCCC");
        Assert.Equal(new[] { 1m, 2m }, puntos.Select(p => p.Latitud));
        Assert.Equal(new[] { 2m, 3m }, puntos.Select(p => p.Longitud));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CFoz5xJ67i1B1B7PzIhaxL7Y")]
    [InlineData("BFoz5xJ67i1B1B7PzIhaxL7")]
    [InlineData("BFoz5xJ67i1B!B7PzIhaxL7Y")]
    [InlineData("BFoz5xJ67i1B")]
    [InlineData("B______________")]
    [InlineData("BgCAAAA")]
    public void FlexiblePolyline_DatosMalformados_NoInventaGeometria(string? texto) =>
        Assert.Throws<InvalidOperationException>(() => FlexiblePolyline.Decodificar(texto));

    [Fact]
    public async Task Geometria_ConParadas_UsaSummaryPolylineYDecodificaCadaSeccion()
    {
        using var handler = new Handler(request =>
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("summary,polyline", query["return"].ToString());
            Assert.Equal("any", query["departureTime"].ToString());
            Assert.Equal(new[] { "-35,-59", "-36,-60" }, query["via"].ToArray());
            return Json($$"""
                {"routes":[{"sections":[
                    {"summary":{"length":100,"duration":10},"polyline":"{{EjemploOficial}}"},
                    {"summary":{"length":200,"duration":20},"polyline":"{{EjemploOficial}}"},
                    {"summary":{"length":300,"duration":30},"polyline":"{{EjemploOficial}}"}]}]}
                """);
        });
        using var client = new HttpClient(handler);
        var service = new RutasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        var ruta = await service.CalcularGeometriaAsync(new[] { P(-34, -58), P(-35, -59), P(-36, -60), P(-37, -61) });
        Assert.Equal(600, ruta.Totales.DistanciaMetros);
        Assert.Equal(60, ruta.Totales.DuracionSegundos);
        Assert.Equal(3, ruta.Secciones.Count);
        Assert.All(ruta.Secciones, s => Assert.Equal(4, s.Count));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(",\"polyline\":\"BF\"", "")]
    [InlineData(",\"polyline\":\"BFoz5xJ67i1B1B7PzIhaxL7Y\"", ",\"notices\":[{\"severity\":\"critical\"}]")]
    public async Task Geometria_SinPolylineOConAvisoCritico_RechazaRecorrido(string polyline, string notices)
    {
        using var handler = new Handler(_ => Json("{\"routes\":[{\"sections\":[{\"summary\":{\"length\":100,\"duration\":10}" + polyline + notices + "}]}]}"));
        using var client = new HttpClient(handler);
        var service = new RutasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalcularGeometriaAsync(new[] { P(1, 2), P(2, 3) }));
    }

    [Fact]
    public async Task Geometria_SeccionesInsuficientesParaLasParadas_NoMuestraRecorridoParcial()
    {
        using var handler = new Handler(_ => Json($$"""
            {"routes":[{"sections":[{"summary":{"length":100,"duration":10},"polyline":"{{EjemploOficial}}"}]}]}
            """));
        using var client = new HttpClient(handler);
        var service = new RutasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CalcularGeometriaAsync(new[] { P(1, 2), P(2, 3), P(3, 4) }));
    }

    [Fact]
    public async Task Mapas_JsonSinAtribuciones_ImpideUsarFondoSinAvisos()
    {
        using var handler = new Handler(_ => Json("{\"error\":\"" + Clave + "\"}"));
        using var client = new HttpClient(handler);
        var service = new MapasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AtribucionesAsync(default));
        Assert.DoesNotContain(Clave, error.ToString());
    }

    [Fact]
    public async Task Mapas_RespuestaHtmlCon200_NoLaReenviaComoImagen()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK)
            { Content = new StringContent("<html>" + Clave + "</html>", Encoding.UTF8, "text/html") });
        using var client = new HttpClient(handler);
        var service = new MapasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TeselaAsync(1, 0, 0, default));
        Assert.DoesNotContain(Clave, error.ToString());
    }

    [Fact]
    public async Task Mapas_Copyright_SeleccionaSoloGruposDeBaseExploreDay()
    {
        using var handler = new Handler(request =>
        {
            Assert.Equal("https://maps.hereapi.com/v3/copyright", request.RequestUri!.GetLeftPart(UriPartial.Path));
            Assert.Equal(Clave, QueryHelpers.ParseQuery(request.RequestUri.Query)["apiKey"].ToString());
            return Json("""
                {"resources":{"base":{"styles":{"explore.day":["in"],"explore.satellite.day":["sat"]}}},
                 "copyrights":{"in":[{"label":"IGN","copyrightText":"Aviso IGN","minLevel":10,"maxLevel":20,
                    "boundingBoxes":[{"west":-70,"east":-50,"south":-40,"north":-20}]}],
                    "sat":[{"label":"No debe aparecer"}]},"debug":"no copiar"}
                """);
        });
        using var client = new HttpClient(handler);
        var servicio = new MapasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        var avisos = await servicio.AtribucionesAsync(default);
        var aviso = Assert.Single(avisos);
        Assert.Equal("IGN", aviso.GetProperty("label").GetString());
        Assert.DoesNotContain(Clave, aviso.GetRawText());
        Assert.DoesNotContain("debug", aviso.GetRawText());
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(19, 0, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(1, 0, -1)]
    public async Task Mapas_TeselaFueraDeRango_NoLlamaHere(int z, int x, int y)
    {
        using var handler = new Handler(_ => throw new Exception("No debe consultar HERE"));
        using var client = new HttpClient(handler);
        var servicio = new MapasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        await Assert.ThrowsAsync<ArgumentException>(() => servicio.TeselaAsync(z, x, y, default));
    }

    [Fact]
    public async Task Mapas_TeselaValida_DestinoFijoYDevuelveSoloPng()
    {
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
        using var handler = new Handler(request =>
        {
            Assert.Equal("/v3/base/mc/5/10/12/png", request.RequestUri!.AbsolutePath);
            Assert.Equal("explore.day", QueryHelpers.ParseQuery(request.RequestUri.Query)["style"].ToString());
            var content = new ByteArrayContent(png);
            content.Headers.ContentType = new("image/png");
            return new(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler);
        var servicio = new MapasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        Assert.Equal(png, await servicio.TeselaAsync(5, 10, 12, default));
    }

    [Fact]
    public async Task Mapas_ErrorDeConexion_NoExponeUrlClaveNiExcepcionInterna()
    {
        using var handler = new Handler(_ => throw new HttpRequestException("https://maps.hereapi.com/?apiKey=" + Clave));
        using var client = new HttpClient(handler);
        var servicio = new MapasHereServicio(client, Options.Create(new HereRoutingOpciones { ApiKey = Clave }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => servicio.TeselaAsync(1, 0, 0, default));
        Assert.DoesNotContain(Clave, error.ToString());
        Assert.Null(error.InnerException);
    }

    private static PuntoRutaDto P(decimal lat, decimal lng) => new() { Latitud = lat, Longitud = lng };
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
