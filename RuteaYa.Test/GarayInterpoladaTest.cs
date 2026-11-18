using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;
using Host = RuteaYa.Test.ViajesDireccionesIntegracionTest.Host;

namespace RuteaYa.Test;

public class GarayInterpoladaTest
{
    // Campos públicos observados en el Lookup real de Garay 431, Ramos Mejía.
    // El identificador se reemplaza por uno ficticio; no hay credenciales ni llamadas reales en los tests.
    private const string Id = "here:af:garay-431-regresion";
    private const string Direccion = "Garay 431, B1704 Ramos Mejía, Argentina";
    private const string Consulta = "Garay 431 Ramos Mejía";
    private const string Lookup = """
        {"id":"here:af:garay-431-regresion","resultType":"houseNumber","houseNumberType":"interpolated",
         "address":{"label":"Garay 431, B1704 Ramos Mejía, Argentina","countryCode":"ARG","countryName":"Argentina",
         "stateCode":"BA","state":"Buenos Aires","county":"La Matanza","city":"Ramos Mejía","street":"Garay","postalCode":"B1704","houseNumber":"431"},
         "position":{"lat":-34.65151,"lng":-58.562},"access":[{"lat":-34.65161,"lng":-58.56189}]}
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LookupCompletoInterpolado_SeConfirma_ConAccesoOConPosition_SinAlterarCalidad(bool sinAcceso, bool estimada)
    {
        var json = JsonNode.Parse(Lookup)!;
        if (sinAcceso) json.AsObject().Remove("access");
        json["estimatedPointAddress"] = estimada; json["houseNumberFallback"] = estimada;
        using var http = new HttpClient(new Handler(json.ToJsonString()));
        var b = new BusquedaDireccionesHereServicio(http, Options.Create(new HereRoutingOpciones { ApiKey = "ficticia" }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var ahora = DateTimeOffset.UtcNow; var reloj = new Mock<TimeProvider>(); reloj.Setup(r => r.GetUtcNow()).Returns(() => ahora);
        var s = new SeleccionDireccionServicio(b, cache, reloj.Object);
        var sugerida = Assert.Single(await s.SugerirAsync(7, "origen", Consulta));
        var resuelta = await s.ResolverAsync(7, "origen", sugerida.Token);
        var confirmada = s.ConfirmarAutomaticamente(7, "origen", resuelta.Token);
        Assert.True(confirmada.Confirmada); Assert.Equal(Direccion, confirmada.Ubicacion.DireccionNormalizada);
        Assert.Equal("interpolated", confirmada.Ubicacion.TipoNumeroPuerta);
        Assert.Equal(estimada, confirmada.Ubicacion.PosicionEstimada); Assert.Equal(estimada, confirmada.Ubicacion.NumeroPuertaCorregido);
        Assert.Equal(sinAcceso ? -1 : 0, confirmada.Acceso);
        Assert.Equal(sinAcceso ? -34.65151m : -34.65161m, confirmada.Navegacion!.Latitud);
        Assert.Equal(sinAcceso ? -58.562m : -58.56189m, confirmada.Navegacion.Longitud);
        Assert.Equal(-34.65151m, confirmada.Ubicacion.Posicion.Latitud);
        Assert.Equal(sinAcceso ? 0 : 1, confirmada.Ubicacion.Accesos.Count);
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => s.ConfirmarAutomaticamente(8, "origen", resuelta.Token));
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => s.ConfirmarAutomaticamente(7, "entrega", resuelta.Token));
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => s.ConfirmarAutomaticamente(7, "origen", "token-libre"));
        ahora = ahora.AddMinutes(11);
        Assert.Throws<SeleccionTemporalNoDisponibleException>(() => s.ObtenerConfirmada(7, "origen", confirmada.Token));
    }

    [Theory]
    [InlineData("sin_position")]
    [InlineData("position_invalida")]
    [InlineData("access_invalido")]
    [InlineData("street")]
    [InlineData("locality")]
    public async Task EstimacionNoPermiteCoordenadasInvalidasNiResultadoGenerico(string caso)
    {
        var json = JsonNode.Parse(Lookup)!;
        if (caso == "sin_position") json.AsObject().Remove("position");
        if (caso == "position_invalida") json["position"]!["lat"] = 91;
        if (caso == "access_invalido") json["access"]![0]!["lng"] = 181;
        if (caso is "street" or "locality") json["resultType"] = caso;
        using var http = new HttpClient(new Handler(json.ToJsonString()));
        var b = new BusquedaDireccionesHereServicio(http, Options.Create(new HereRoutingOpciones { ApiKey = "ficticia" }));
        using var cache = new MemoryCache(new MemoryCacheOptions()); var s = new SeleccionDireccionServicio(b, cache);
        var sugerida = Assert.Single(await s.SugerirAsync(7, "origen", Consulta));
        if (caso is "street" or "locality") {
            var resuelta = await s.ResolverAsync(7, "origen", sugerida.Token);
            Assert.Throws<ArgumentException>(() => s.ConfirmarAutomaticamente(7, "origen", resuelta.Token));
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => s.ResolverAsync(7, "origen", sugerida.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComponenteCompartido_GuardaGarayEnViajeYEnvio_ConComprobanteServidor(bool envio)
    {
        await using var f = await Host.Crear();
        using var http = new HttpClient(new Handler(Lookup));
        var u = await new BusquedaDireccionesHereServicio(http, Options.Create(new HereRoutingOpciones { ApiKey = "ficticia" })).ResolverAsync(Id);
        f.Busqueda.Setup(b => b.SugerirAsync(Consulta, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new SugerenciaDireccionDto { IdHere = Id, Nombre = "Garay 431", Localidad = "Ramos Mejía", Direccion = Direccion } });
        f.Busqueda.Setup(b => b.ResolverAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(u);
        var html = await f.Mvc.GetStringAsync(envio ? "/Envios/Crear" : "/Panel/NuevoViaje");
        string Valor(string nombre) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(nombre)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        var csrf = Valor("__RequestVerificationToken"); var form = Valor("FormularioDirecciones");
        var origen = await f.Confirmar(form, envio ? "retiro" : "origen", Consulta, csrf, envio ? "DireccionesEnvio" : "DireccionesViaje");
        var destino = await f.Confirmar(form, envio ? "entrega" : "destino", "Destino de prueba", csrf, envio ? "DireccionesEnvio" : "DireccionesViaje");
        var datos = new Dictionary<string,string> { ["__RequestVerificationToken"] = csrf, ["FormularioDirecciones"] = form };
        if (envio) {
            datos["OrigenDir"] = origen.Texto; datos["DestinoDir"] = destino.Texto; datos["RetiroComprobante"] = origen.Token; datos["EntregaComprobante"] = destino.Token;
            datos["Descripcion"] = "Regresión Garay"; datos["PesoKg"] = "1"; datos["Monto"] = "100"; datos["Tamano"] = "0"; datos["FechaLimite"] = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        } else {
            datos["Origen"] = origen.Texto; datos["Destino"] = destino.Texto; datos["OrigenComprobante"] = origen.Token; datos["DestinoComprobante"] = destino.Token;
            datos["DiasSemana"] = "1"; datos["HoraSalida"] = "08:00:00"; datos["HoraLlegada"] = "09:00:00"; datos["CantMaxPaquetes"] = "2"; datos["PesoMax"] = "10"; datos["DesvioMaxKm"] = "3";
        }
        // Valores libres del navegador no sustituyen el punto verificado por Lookup.
        datos["OrigenLat"] = "90"; datos["OrigenLng"] = "0";
        using var respuesta = await f.Mvc.PostAsync(envio ? "/Envios/Crear" : "/Panel/NuevoViaje", new FormUrlEncodedContent(datos));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        await using var db = f.Db();
        var guardada = await db.UbicacionesConfirmadas.SingleAsync(x => x.DireccionNormalizada == Direccion);
        Assert.NotNull(guardada.ConsumidaEnUtc); Assert.Equal("interpolated", guardada.TipoNumeroPuerta);
        Assert.Equal(-34.65151m, guardada.PosicionLatitud); Assert.Equal(-34.65161m, guardada.NavegacionLatitud);
        if (envio) Assert.Equal(guardada.Id, (await db.Envios.SingleAsync()).RetiroUbicacionId);
        else Assert.Equal(guardada.Id, (await db.Viajes.SingleAsync()).OrigenUbicacionId);
    }

    private sealed class Handler(string lookup) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var json = r.RequestUri!.AbsolutePath.EndsWith("/autocomplete")
                ? "{\"items\":[{\"id\":\"" + Id + "\",\"title\":\"Garay 431\",\"address\":{\"label\":\"" + Direccion + "\",\"city\":\"Ramos Mejía\"}}]}" : lookup;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
