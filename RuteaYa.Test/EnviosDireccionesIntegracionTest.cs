using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using Host = RuteaYa.Test.ViajesDireccionesIntegracionTest.Host;

namespace RuteaYa.Test;

public class EnviosDireccionesIntegracionTest
{
    [EdgeFact]
    public async Task NavegadorReal_SeleccionaDosUbicacionesYPublicaSinAlterarLosDemasCampos()
    {
        await using var f = await Host.Crear(navegador: true, navegadorEnvios: true);
        Assert.Equal("OK", await Helpers.EdgeNavegador.EjecutarAsync(f.Mvc.BaseAddress + "Envios/Crear"));
        await using var db = f.Db(); var e = await db.Envios.SingleAsync();
        Assert.Equal("Retiro UI, Morón, Argentina", e.OrigenDir); Assert.Equal(2.5m, e.PesoKg); Assert.Equal(1500.75m, e.Monto);
        Assert.NotNull(e.RetiroUbicacionId); Assert.NotNull(e.EntregaUbicacionId);
        Assert.Equal(0, f.LlamadasGateway); f.Geo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FormularioAnteriorConTextoYFlagsLibres_NoPublica()
    {
        await using var f = await Host.Crear(); var html = await f.Mvc.GetStringAsync("/Envios/Crear");
        var datos = new Dictionary<string, string> { ["__RequestVerificationToken"] = Extraer(html, "__RequestVerificationToken"),
            ["OrigenDir"] = "Dirección sin seleccionar", ["DestinoDir"] = "Otra dirección", ["PesoKg"] = "1", ["Monto"] = "100",
            ["Tamano"] = "2", ["Descripcion"] = "Formulario anterior", ["FechaLimite"] = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            ["Confirmada"] = "true", ["OrigenLat"] = "-34", ["DestinoLng"] = "-58" };
        using var respuesta = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(datos));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        await using var db = f.Db(); Assert.Empty(await db.Envios.ToListAsync()); Assert.Empty(await db.UbicacionesConfirmadas.ToListAsync());
        f.Geo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MismaDireccionCanonicaEnAmbosCampos_ConservaValidacionAnterior()
    {
        await using var f = await Host.Crear(); var form = await Preparar(f, "Mismo lugar", "Mismo lugar");
        using var respuesta = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(form.Datos));
        Assert.Contains("diferentes", await respuesta.Content.ReadAsStringAsync());
        await using var db = f.Db(); Assert.Empty(await db.Envios.ToListAsync());
        Assert.All(await db.UbicacionesConfirmadas.ToListAsync(), u => Assert.Null(u.ConsumidaEnUtc));
    }

    [Fact]
    public async Task MvcLogicaEf_CreaConDireccionesConComas_YConservaPositionFrenteANavegacion()
    {
        await using var f = await Host.Crear(); var formulario = await Preparar(f);
        formulario.Datos.Add(new("OrigenLat", "90")); formulario.Datos.Add(new("DestinoLng", "0"));
        formulario.Datos.Add(new("Confirmada", "true")); formulario.Datos.Add(new("UsuarioId", "8"));
        using var respuesta = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(formulario.Datos));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains(respuesta.Headers.Location!.ToString(), new[] { "/Envios", "/Envios/Index" });
        await using var db = f.Db();
        var e = await db.Envios.Include(e => e.RetiroUbicacion).Include(e => e.EntregaUbicacion).SingleAsync();
        Assert.Equal("Retiro real, Morón, Argentina", e.OrigenDir); Assert.Equal("Entrega real, Morón, Argentina", e.DestinoDir);
        Assert.Equal(-34m, e.OrigenLat); Assert.Equal(-58m, e.OrigenLng); // columnas anteriores = position.
        Assert.Equal(-35m, e.RetiroUbicacion!.NavegacionLatitud); Assert.Equal(-59m, e.EntregaUbicacion!.NavegacionLongitud);
        Assert.Equal(EstadoEnvio.BuscandoConductor, e.Estado); Assert.Equal(2.5m, e.PesoKg); Assert.Equal(1500m, e.Monto);
        Assert.All(await db.UbicacionesConfirmadas.ToListAsync(), u => Assert.NotNull(u.ConsumidaEnUtc));
        Assert.Equal(0, f.LlamadasGateway); f.Geo.VerifyNoOtherCalls();
        // El mismo POST no vuelve a publicar ni a consumir las ubicaciones.
        using var repetida = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(formulario.Datos));
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Contains("comprobante_consumido", await repetida.Content.ReadAsStringAsync());
        Assert.Single(await db.Envios.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("texto")]
    [InlineData("duplicado")]
    [InlineData("usuario")]
    [InlineData("vencido")]
    [InlineData("sin_comprobantes")]
    public async Task EndpointAnterior_NoPermiteEludirUbicacionesConfirmadas(string ataque)
    {
        await using var f = await Host.Crear(); var form = await Preparar(f);
        if (ataque == "texto") Reemplazar(form.Datos, "OrigenDir", "Dirección libre distinta de la seleccionada");
        if (ataque == "duplicado") Reemplazar(form.Datos, "EntregaComprobante", Valor(form.Datos, "RetiroComprobante"));
        if (ataque == "usuario") { f.Mvc.DefaultRequestHeaders.Remove("X-Usuario-Prueba"); f.Mvc.DefaultRequestHeaders.Add("X-Usuario-Prueba", "8"); }
        if (ataque == "vencido") f.Reloj.Ahora = f.Reloj.Ahora.AddMinutes(11);
        if (ataque == "sin_comprobantes") { Reemplazar(form.Datos, "RetiroComprobante", ""); Reemplazar(form.Datos, "EntregaComprobante", ""); }
        // Campos libres y contextos enviados por el cliente no constituyen confirmación.
        form.Datos.Add(new("OrigenLat", "90")); form.Datos.Add(new("DestinoLng", "0")); form.Datos.Add(new("Confirmada", "true"));
        using var respuesta = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(form.Datos));
        Assert.NotEqual(HttpStatusCode.Redirect, respuesta.StatusCode);
        await using var db = f.Db(); Assert.Empty(await db.Envios.ToListAsync());
        Assert.All(await db.UbicacionesConfirmadas.ToListAsync(), u => Assert.Null(u.ConsumidaEnUtc));
        f.Geo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrearAsync_ValidaComprobantesYDatosCanonicosSinUsarCoordenadasOTextoDelCliente(bool otroUsuario)
    {
        await using var f = await Host.Crear(); var form = await Preparar(f);
        await using var db = f.Db(); var filas = await db.UbicacionesConfirmadas.ToListAsync();
        ContextoUbicacionDto Contexto(CampoUbicacion campo) { var u = filas.Single(u => u.Campo == campo); return new(u.FormularioId, u.TipoRecurso, u.RecursoId, u.Campo, u.ClaveCampo, u.RevisionRecurso); }
        var dto = new CrearEnvioDto { OrigenDir = "Texto falso", DestinoDir = "Otro texto falso", Descripcion = "Prueba", PesoKg = 1, Monto = 100,
            FechaLimite = DateTime.Today.AddDays(1), Tamano = TamanoEnvio.Mediano,
            Ubicaciones = new() { Retiro = Contexto(CampoUbicacion.RetiroEnvio), Entrega = Contexto(CampoUbicacion.EntregaEnvio),
                ComprobanteRetiro = Valor(form.Datos, "RetiroComprobante"), ComprobanteEntrega = Valor(form.Datos, "EntregaComprobante") } };
        var servicio = new EnviosLogica(db, f.Geo.Object, new UbicacionesConfirmadasServicio(db, reloj: f.Reloj));
        if (otroUsuario) await Assert.ThrowsAsync<ComprobanteUbicacionException>(() => servicio.CrearAsync(8, dto));
        else {
            await servicio.CrearAsync(7, dto);
            Assert.Equal("Retiro real, Morón, Argentina", (await db.Envios.SingleAsync()).OrigenDir);
        }
        f.Geo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FalloSqlRealEnMemoria_HaceRollbackDeLosDosConsumosYPermiteReintentar()
    {
        await using var f = await Host.Crear(); var form = await Preparar(f);
        await using (var db = f.Db()) {
            // Trigger solo en SQLite efímero. Fuerza un error real de inserción, sin tocar SQL Server ni migraciones.
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FallarEnvio BEFORE INSERT ON Envios BEGIN SELECT RAISE(ABORT, 'prueba de rollback'); END;");
        }
        using var respuesta = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(form.Datos));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.DoesNotContain("prueba de rollback", await respuesta.Content.ReadAsStringAsync());
        await using (var db = f.Db()) {
            Assert.Empty(await db.Envios.ToListAsync());
            Assert.All(await db.UbicacionesConfirmadas.ToListAsync(), u => Assert.Null(u.ConsumidaEnUtc));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER FallarEnvio;");
        }
        using var reintento = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(form.Datos));
        Assert.Equal(HttpStatusCode.Redirect, reintento.StatusCode);
    }

    [Fact]
    public async Task DatosLegadosSiguenLegiblesSinMarcarConfirmacion()
    {
        await using var f = await Host.Crear();
        await using (var db = f.Db()) { db.Envios.Add(new Envio { UsuarioId = 7, OrigenDir = "Retiro antiguo", DestinoDir = "Entrega antigua",
            OrigenLat = 0, DestinoLat = -34.12m, Descripcion = "Envío antiguo", PesoKg = 1, Monto = 100 }); await db.SaveChangesAsync(); }
        using var respuesta = await f.Mvc.GetAsync("/Envios");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode); Assert.Contains("Retiro antiguo", await respuesta.Content.ReadAsStringAsync());
        await using var lectura = f.Db(); var e = await lectura.Envios.SingleAsync();
        Assert.Null(e.RetiroUbicacionId); Assert.Null(e.EntregaUbicacionId); Assert.Equal(-34.12m, e.DestinoLat);
    }

    [Fact]
    public async Task ValidacionDeOtrosCampos_ConservaComprobantesParaCorregirYGuardar()
    {
        await using var f = await Host.Crear(); var form = await Preparar(f);
        Reemplazar(form.Datos, "PesoKg", "-1");
        using var invalido = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(form.Datos));
        Assert.Equal(HttpStatusCode.OK, invalido.StatusCode);
        Assert.Contains(Valor(form.Datos, "RetiroComprobante"), await invalido.Content.ReadAsStringAsync());
        await using (var db = f.Db()) { Assert.Empty(await db.Envios.ToListAsync()); Assert.All(await db.UbicacionesConfirmadas.ToListAsync(), u => Assert.Null(u.ConsumidaEnUtc)); }
        Reemplazar(form.Datos, "PesoKg", "2.5");
        using var valido = await f.Mvc.PostAsync("/Envios/Crear", new FormUrlEncodedContent(form.Datos));
        Assert.Equal(HttpStatusCode.Redirect, valido.StatusCode);
    }

    private static async Task<(string Formulario, List<KeyValuePair<string, string>> Datos)> Preparar(Host f, string retiroTexto = "Retiro real", string entregaTexto = "Entrega real")
    {
        var html = await f.Mvc.GetStringAsync("/Envios/Crear");
        Assert.Equal(2, Regex.Matches(html, "data-direccion-here").Count);
        Assert.Contains("name=\"OrigenDir\"", html); Assert.Contains("name=\"DestinoDir\"", html);
        var csrf = Extraer(html, "__RequestVerificationToken"); var formulario = Extraer(html, "FormularioDirecciones");
        var retiro = await f.Confirmar(formulario, "retiro", retiroTexto, csrf, "DireccionesEnvio");
        var entrega = await f.Confirmar(formulario, "entrega", entregaTexto, csrf, "DireccionesEnvio");
        return (formulario, new() { new("__RequestVerificationToken", csrf), new("FormularioDirecciones", formulario),
            new("OrigenDir", retiro.Texto), new("DestinoDir", entrega.Texto), new("RetiroComprobante", retiro.Token), new("EntregaComprobante", entrega.Token),
            new("Descripcion", "Paquete de prueba"), new("PesoKg", "2.5"), new("Monto", "1500"), new("Tamano", "2"), new("FechaLimite", DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")) });
    }
    private static string Extraer(string html, string nombre) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(nombre)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static string Valor(List<KeyValuePair<string, string>> datos, string clave) => datos.Single(d => d.Key == clave).Value;
    private static void Reemplazar(List<KeyValuePair<string, string>> datos, string clave, string valor) { datos.RemoveAll(d => d.Key == clave); datos.Add(new(clave, valor)); }
}
