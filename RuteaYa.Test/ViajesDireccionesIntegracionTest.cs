using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Services;
using ApiController = RuteaYa.Api.Controllers.ViajesController;
using MvcController = RuteaYa.Web.Controllers.PanelController;
using System.Runtime.CompilerServices;

namespace RuteaYa.Test;

public class ViajesDireccionesIntegracionTest
{
    [Fact]
    public async Task Api_ValidaDireccionCanonicaAntesConsumirNoElTextoLibre()
    {
        await using var f = await Host.Crear();
        var html = await f.Mvc.GetStringAsync("/Panel/NuevoViaje"); var csrf = Valor(html, "__RequestVerificationToken"); var form = Valor(html, "FormularioDirecciones");
        var o = await f.Confirmar(form, "origen", "Misma dirección", csrf); var d = await f.Confirmar(form, "destino", "Misma dirección", csrf);
        await using var db = f.Db(); var filas = await db.UbicacionesConfirmadas.ToListAsync();
        CampoViajeUbicacionDto Campo(CampoUbicacion tipo, string token) { var u = filas.Single(u => u.Campo == tipo); return new() {
            Contexto = new(u.FormularioId, u.TipoRecurso, u.RecursoId, u.Campo, u.ClaveCampo, u.RevisionRecurso), Comprobante = token }; }
        var dto = CrearDto(); dto.Origen = "Supuestamente distinto"; dto.Ubicaciones = new() {
            Origen = Campo(CampoUbicacion.OrigenViaje, o.Token), Destino = Campo(CampoUbicacion.DestinoViaje, d.Token) };
        using var respuesta = await f.Api.PostAsJsonAsync("/api/viajes", dto);
        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("diferentes", await respuesta.Content.ReadAsStringAsync());
        Assert.Empty(await db.Viajes.ToListAsync());
        Assert.All(await db.UbicacionesConfirmadas.AsNoTracking().ToListAsync(), u => Assert.Null(u.ConsumidaEnUtc));
    }

    [Fact]
    public async Task Api_UnComprobanteDeParadaNoSePuedeAplicarAOtraParadaExistente()
    {
        await using var f = await Host.Crear(); var legacy = CrearDto(); legacy.Paradas = new() { "Parada uno", "Parada dos" };
        using var creada = await f.Api.PostAsJsonAsync("/api/viajes", legacy);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        await using var db = f.Db(); var viaje = await db.Viajes.Include(v => v.Paradas).SingleAsync();
        var actuales = viaje.Paradas.OrderBy(p => p.Orden).ToArray();
        var html = await f.Mvc.GetStringAsync("/Panel/EditarViaje/" + viaje.Id); var csrf = Valor(html, "__RequestVerificationToken"); var form = Valor(html, "FormularioDirecciones");
        var claves = Regex.Matches(html, "name=\"Paradas.Index\"[^>]*value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        var prueba = await f.Confirmar(form, claves[0], "Nueva parada", csrf);
        var u = await db.UbicacionesConfirmadas.SingleAsync();
        async Task<ContextoUbicacionDto> Contexto(CampoUbicacion campo, string clave) {
            using var respuesta = await f.Api.PostAsJsonAsync("/api/viajes/ubicaciones/contexto", new SolicitudContextoViajeDto { ViajeId = viaje.Id,
                FormularioId = u.FormularioId, Campo = campo, ClaveCampo = clave });
            return (await respuesta.Content.ReadFromJsonAsync<ContextoUbicacionDto>())!;
        }
        var dto = new EditarViajeDto { Origen = viaje.Origen, Destino = viaje.Destino, DiasSemana = new() { 1 }, HoraSalida = TimeSpan.FromHours(8),
            HoraLlegada = TimeSpan.FromHours(9), PesoMax = 10, CantMaxPaquetes = 2, DesvioMaxKm = 3 };
        dto.Ubicaciones = new() { Origen = new() { Contexto = await Contexto(CampoUbicacion.OrigenViaje, "OrigenViaje"), Texto = viaje.Origen },
            Destino = new() { Contexto = await Contexto(CampoUbicacion.DestinoViaje, "DestinoViaje"), Texto = viaje.Destino },
            Paradas = new() { new() { Contexto = new(u.FormularioId, u.TipoRecurso, u.RecursoId, u.Campo, u.ClaveCampo, u.RevisionRecurso),
                IdParada = actuales[1].Id, Comprobante = prueba.Token } } };
        using var resultado = await f.Api.PutAsJsonAsync("/api/viajes/" + viaje.Id, dto);
        Assert.Equal(HttpStatusCode.Conflict, resultado.StatusCode);
        Assert.Null((await db.UbicacionesConfirmadas.AsNoTracking().SingleAsync()).ConsumidaEnUtc);
        Assert.Equal(2, await db.Paradas.CountAsync());
    }

    [EdgeFact]
    public async Task Navegador_CrearViajeConParadaDinamica_GuardaPorApi()
    {
        await using var f = await Host.Crear(navegador: true);
        var resultado = await Helpers.EdgeNavegador.EjecutarAsync(f.Mvc.BaseAddress + "Panel/NuevoViaje");
        Assert.Equal("OK", resultado);
        await using var db = f.Db(); var viaje = await db.Viajes.Include(v => v.Paradas).SingleAsync();
        Assert.Equal("Origen UI, Morón, Argentina", viaje.Origen);
        Assert.Equal("Parada UI, Morón, Argentina", viaje.Paradas.Single().Ubicacion);
        Assert.NotNull(viaje.Paradas.Single().UbicacionConfirmadaId);
        Assert.Equal(3, await db.UbicacionesConfirmadas.CountAsync(u => u.ConsumidaEnUtc != null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Api_RechazaComprobanteVencidoOCambiadoDeCampoSinGuardar(bool vencido)
    {
        await using var f = await Host.Crear();
        var html = await f.Mvc.GetStringAsync("/Panel/NuevoViaje"); var csrf = Valor(html, "__RequestVerificationToken"); var form = Valor(html, "FormularioDirecciones");
        var origen = await f.Confirmar(form, "origen", "Origen", csrf); var destino = await f.Confirmar(form, "destino", "Destino", csrf);
        await using var db = f.Db(); var filas = await db.UbicacionesConfirmadas.ToListAsync();
        ContextoUbicacionDto C(CampoUbicacion campo) { var u = filas.Single(u => u.Campo == campo); return new(u.FormularioId, u.TipoRecurso, u.RecursoId, u.Campo, u.ClaveCampo, u.RevisionRecurso); }
        var dto = CrearDto(); dto.Ubicaciones = new() { Origen = new() { Contexto = C(CampoUbicacion.OrigenViaje), Comprobante = vencido ? origen.Token : destino.Token },
            Destino = new() { Contexto = C(CampoUbicacion.DestinoViaje), Comprobante = destino.Token } };
        if (vencido) f.Reloj.Ahora = f.Reloj.Ahora.AddMinutes(11);
        using var respuesta = await f.Api.PostAsJsonAsync("/api/viajes", dto);
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Empty(await db.Viajes.ToListAsync());
        Assert.All(filas, u => Assert.Null(u.ConsumidaEnUtc));
    }

    [Fact]
    public async Task EditarConfirmado_CambiaUnCampoConComprobanteYConservaLosDemas()
    {
        await using var f = await Host.Crear();
        var alta = await f.Mvc.GetStringAsync("/Panel/NuevoViaje"); var csrf = Valor(alta, "__RequestVerificationToken"); var form = Valor(alta, "FormularioDirecciones");
        var o = await f.Confirmar(form, "origen", "Origen", csrf); var d = await f.Confirmar(form, "destino", "Destino", csrf);
        using var creada = await f.Mvc.PostAsync("/Panel/NuevoViaje", new FormUrlEncodedContent(Datos(csrf, form, o.Texto, d.Texto, o.Token, d.Token)));
        Assert.Equal(HttpStatusCode.Redirect, creada.StatusCode);
        int id; Guid? destinoId;
        await using (var db = f.Db()) { var v = await db.Viajes.SingleAsync(); id = v.Id; destinoId = v.DestinoUbicacionId; }
        var editar = await f.Mvc.GetStringAsync("/Panel/EditarViaje/" + id); csrf = Valor(editar, "__RequestVerificationToken"); form = Valor(editar, "FormularioDirecciones");
        var nuevo = await f.Confirmar(form, "origen", "Nuevo origen", csrf);
        var datos = Datos(csrf, form, nuevo.Texto, d.Texto, nuevo.Token, ""); datos.Add(new("Id", id.ToString()));
        using var guardada = await f.Mvc.PostAsync("/Panel/EditarViaje", new FormUrlEncodedContent(datos));
        Assert.Equal(HttpStatusCode.Redirect, guardada.StatusCode);
        await using var lectura = f.Db(); var viaje = await lectura.Viajes.SingleAsync();
        Assert.Equal(nuevo.Texto, viaje.Origen); Assert.Equal(destinoId, viaje.DestinoUbicacionId);
        Assert.Equal(3, await lectura.UbicacionesConfirmadas.CountAsync(u => u.ConsumidaEnUtc != null));
        // Un cliente anterior no puede desincronizar texto y coordenadas de una ubicación confirmada.
        var legacy = CrearDto(); legacy.Origen = "Sin prueba"; legacy.Destino = d.Texto;
        using var rechazada = await f.Api.PutAsJsonAsync("/api/viajes/" + id, legacy);
        Assert.Equal(HttpStatusCode.BadRequest, rechazada.StatusCode);
    }

    [Fact]
    public async Task MvcApiLogica_CreaDireccionesConfirmadasYParadasConComasEnOrdenYRechazaReuso()
    {
        await using var f = await Host.Crear();
        var html = await f.Mvc.GetStringAsync("/Panel/NuevoViaje");
        Assert.Contains("data-direccion-here", html);
        Assert.DoesNotContain("name=\"ParadasTexto\"", html);
        var csrf = Valor(html, "__RequestVerificationToken");
        var form = Valor(html, "FormularioDirecciones");
        var campos = await f.Confirmar(form, "origen", "Origen real", csrf);
        var destino = await f.Confirmar(form, "destino", "Destino real", csrf);
        var p1 = await f.Agregar(form, csrf); var p2 = await f.Agregar(form, csrf);
        var parada1 = await f.Confirmar(form, p1, "Parada uno", csrf); var parada2 = await f.Confirmar(form, p2, "Parada dos", csrf);
        var valores = Datos(csrf, form, campos.Texto, destino.Texto, campos.Token, destino.Token);
        Agregar(valores, p2, 0, parada2.Texto, parada2.Token); Agregar(valores, p1, 0, parada1.Texto, parada1.Token);
        using var respuesta = await f.Mvc.PostAsync("/Panel/NuevoViaje", new FormUrlEncodedContent(valores));
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        await using (var db = f.Db()) {
            var viaje = await db.Viajes.Include(v => v.OrigenUbicacion).Include(v => v.Paradas).SingleAsync();
            Assert.Equal("Origen real, Morón, Argentina", viaje.Origen);
            Assert.Equal(-35m, viaje.OrigenUbicacion!.NavegacionLatitud);
            Assert.Equal(new[] { parada2.Texto, parada1.Texto }, viaje.Paradas.OrderBy(p => p.Orden).Select(p => p.Ubicacion));
            Assert.All(viaje.Paradas, p => Assert.NotNull(p.UbicacionConfirmadaId));
            Assert.All(await db.UbicacionesConfirmadas.ToListAsync(), u => Assert.NotNull(u.ConsumidaEnUtc));
        }
        using var repetido = await f.Mvc.PostAsync("/Panel/NuevoViaje", new FormUrlEncodedContent(valores));
        Assert.Equal(HttpStatusCode.OK, repetido.StatusCode);
        Assert.Contains("comprobante_consumido", await repetido.Content.ReadAsStringAsync());
        await using var lectura = f.Db(); Assert.Single(await lectura.Viajes.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Api_NoConfiaEnTextoCoordenadasOUsuarioDeOtroComprobante(bool otroUsuario)
    {
        await using var f = await Host.Crear();
        var html = await f.Mvc.GetStringAsync("/Panel/NuevoViaje");
        var csrf = Valor(html, "__RequestVerificationToken"); var form = Valor(html, "FormularioDirecciones");
        var origen = await f.Confirmar(form, "origen", "Origen", csrf); var destino = await f.Confirmar(form, "destino", "Destino", csrf);
        await using var db = f.Db();
        var filas = await db.UbicacionesConfirmadas.ToListAsync();
        ContextoUbicacionDto Contexto(CampoUbicacion campo) { var u = filas.Single(u => u.Campo == campo); return new(u.FormularioId, u.TipoRecurso, u.RecursoId, u.Campo, u.ClaveCampo, u.RevisionRecurso); }
        f.Api.DefaultRequestHeaders.Remove("X-Usuario-Prueba"); f.Api.DefaultRequestHeaders.Add("X-Usuario-Prueba", otroUsuario ? "8" : "7");
        var dto = CrearDto(); dto.Origen = "Texto manipulado"; dto.Destino = "Otro texto manipulado";
        dto.Ubicaciones = new() { Origen = new() { Contexto = Contexto(CampoUbicacion.OrigenViaje), Comprobante = origen.Token },
            Destino = new() { Contexto = Contexto(CampoUbicacion.DestinoViaje), Comprobante = destino.Token } };
        using var respuesta = await f.Api.PostAsJsonAsync("/api/viajes", dto);
        Assert.Equal(otroUsuario ? HttpStatusCode.Conflict : HttpStatusCode.Created, respuesta.StatusCode);
        if (otroUsuario) Assert.Empty(await db.Viajes.ToListAsync());
        else Assert.Equal(origen.Texto, (await db.Viajes.SingleAsync()).Origen);
    }

    [Fact]
    public async Task EditarLegado_ConservaTextosSinInventarConfirmacionYReordenaSinRecrearParadas()
    {
        await using var f = await Host.Crear(); int id, p1, p2;
        await using (var db = f.Db()) {
            var v = new Viaje { UsuarioId = 7, Origen = "Origen legado", Destino = "Destino legado", Activo = true, PesoMax = 10, CantMaxPaquetes = 2,
                HoraSalida = TimeSpan.FromHours(8), HoraLlegada = TimeSpan.FromHours(9), Dias = new List<DiaViaje> { new() { DiaSemana = 1 } },
                Paradas = new List<Parada> { new() { Ubicacion = "Primera, con coma", Orden = 1 }, new() { Ubicacion = "Segunda, con coma", Orden = 2 } } };
            db.Viajes.Add(v); await db.SaveChangesAsync(); id = v.Id; p1 = v.Paradas.Single(p => p.Orden == 1).Id; p2 = v.Paradas.Single(p => p.Orden == 2).Id;
        }
        var html = await f.Mvc.GetStringAsync("/Panel/EditarViaje/" + id);
        Assert.Contains("pendiente de confirmaci", WebUtility.HtmlDecode(html));
        var csrf = Valor(html, "__RequestVerificationToken"); var form = Valor(html, "FormularioDirecciones");
        var claves = Regex.Matches(html, "name=\"Paradas.Index\"[^>]*value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        var valores = Datos(csrf, form, "Origen legado", "Destino legado", "", ""); valores.Add(new("Id", id.ToString()));
        Agregar(valores, claves[1], p2, "Segunda, con coma", ""); Agregar(valores, claves[0], p1, "Primera, con coma", "");
        using var guardado = await f.Mvc.PostAsync("/Panel/EditarViaje", new FormUrlEncodedContent(valores));
        Assert.Equal(HttpStatusCode.Redirect, guardado.StatusCode);
        await using (var db = f.Db()) {
            var v = await db.Viajes.Include(v => v.Paradas).SingleAsync();
            Assert.Null(v.OrigenUbicacionId); Assert.Null(v.DestinoUbicacionId);
            Assert.Equal(new[] { p2, p1 }, v.Paradas.OrderBy(p => p.Orden).Select(p => p.Id));
            Assert.Empty(await db.UbicacionesConfirmadas.ToListAsync());
        }
        valores.RemoveAll(p => p.Key == "Origen"); valores.Add(new("Origen", "Cambio sin confirmar"));
        using var alterado = await f.Mvc.PostAsync("/Panel/EditarViaje", new FormUrlEncodedContent(valores));
        Assert.Contains("confirm", WebUtility.HtmlDecode(await alterado.Content.ReadAsStringAsync()));
        await using var lectura = f.Db(); Assert.Equal("Origen legado", (await lectura.Viajes.SingleAsync()).Origen);
    }

    [Fact]
    public async Task DireccionesFormulario_RechazaUsuarioAjenoCampoYAntiforgeryYApiRechazaSinConfirmaciones()
    {
        await using var f = await Host.Crear();
        var html = await f.Mvc.GetStringAsync("/Panel/NuevoViaje"); var form = Valor(html, "FormularioDirecciones");
        using var sinCsrf = await f.Mvc.PostAsync($"/DireccionesViaje/{form}/Resolver?campo=origen", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "libre" }));
        Assert.Equal(HttpStatusCode.BadRequest, sinCsrf.StatusCode);
        f.Mvc.DefaultRequestHeaders.Remove("X-Usuario-Prueba"); f.Mvc.DefaultRequestHeaders.Add("X-Usuario-Prueba", "8");
        using var ajeno = await f.Mvc.GetAsync($"/DireccionesViaje/{form}/Sugerencias?campo=origen&texto=calle");
        Assert.Equal(HttpStatusCode.Conflict, ajeno.StatusCode);
        var dto = CrearDto(); dto.Ubicaciones = new() {
            Origen = new() { Contexto = new(Guid.NewGuid(), TipoRecursoUbicacion.Viaje, null, CampoUbicacion.OrigenViaje, "OrigenViaje", null) },
            Destino = new() { Contexto = new(Guid.NewGuid(), TipoRecursoUbicacion.Viaje, null, CampoUbicacion.DestinoViaje, "DestinoViaje", null) } };
        using var invalido = await f.Api.PostAsJsonAsync("/api/viajes", dto);
        Assert.Equal(HttpStatusCode.Conflict, invalido.StatusCode);
        await using var db = f.Db(); Assert.Empty(await db.Viajes.ToListAsync());
    }

    private static CrearViajeDto CrearDto() => new() { Origen = "Origen", Destino = "Destino", DiasSemana = new() { 1 },
        HoraSalida = TimeSpan.FromHours(8), HoraLlegada = TimeSpan.FromHours(9), CantMaxPaquetes = 2, PesoMax = 10, DesvioMaxKm = 3 };
    private static List<KeyValuePair<string, string>> Datos(string csrf, string form, string origen, string destino, string t1, string t2) => new() {
        new("__RequestVerificationToken", csrf), new("FormularioDirecciones", form), new("Origen", origen), new("Destino", destino),
        new("OrigenComprobante", t1), new("DestinoComprobante", t2), new("DiasSemana", "1"), new("HoraSalida", "08:00:00"),
        new("HoraLlegada", "09:00:00"), new("CantMaxPaquetes", "2"), new("PesoMax", "10"), new("DesvioMaxKm", "3") };
    private static void Agregar(List<KeyValuePair<string, string>> datos, string clave, int id, string texto, string token) {
        datos.Add(new("Paradas.Index", clave)); datos.Add(new($"Paradas[{clave}].ClaveCampo", clave)); datos.Add(new($"Paradas[{clave}].Id", id.ToString()));
        datos.Add(new($"Paradas[{clave}].Ubicacion", texto)); datos.Add(new($"Paradas[{clave}].Comprobante", token)); }
    private static string Valor(string html, string nombre) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(nombre)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);

    private sealed class DbPrueba(DbContextOptions<RuteaYaDbContext> options) : RuteaYaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Entity<Envio>().Property(e => e.RowVersion).ValueGeneratedNever();
            foreach (var p in new[] { "PosicionLatitud", "PosicionLongitud", "NavegacionLatitud", "NavegacionLongitud" }) b.Entity<UbicacionConfirmada>().Property<decimal>(p).HasConversion<double>(); }
    }
    internal sealed class Host : IAsyncDisposable
    {
        private SqliteConnection _connection = new("Data Source=:memory:"); private WebApplication _api = null!, _mvc = null!;
        public HttpClient Mvc = null!, Api = null!;
        public Mock<IGeocodificacionServicio> Geo = new(MockBehavior.Strict);
        public int LlamadasGateway;
        public RelojPrueba Reloj = new();
        public RuteaYaDbContext Db() => new DbPrueba(new DbContextOptionsBuilder<RuteaYaDbContext>().UseSqlite(_connection).Options);
        public static async Task<Host> Crear(bool navegador = false, bool navegadorEnvios = false, [CallerFilePath] string archivo = "") {
            var f = new Host(); await f._connection.OpenAsync();
            await using (var db = f.Db()) { await db.Database.EnsureCreatedAsync(); db.Usuarios.AddRange(
                new Usuario { Id = 7, Nombre = "Siete", Apellido = "Test", Email = "siete@test.invalid", PasswordHash = "ficticio" },
                new Usuario { Id = 8, Nombre = "Ocho", Apellido = "Test", Email = "ocho@test.invalid", PasswordHash = "ficticio" }); await db.SaveChangesAsync(); }
            var api = Builder(typeof(ApiController)); api.Services.AddScoped(_ => f.Db()); api.Services.AddSingleton<TimeProvider>(f.Reloj);
            api.Services.AddScoped<IUbicacionesConfirmadasServicio, UbicacionesConfirmadasServicio>(); api.Services.AddScoped<IViajesLogica, ViajesLogica>();
            f._api = Aplicacion(api); await f._api.StartAsync(); var url = Direccion(f._api);
            f.Api = new HttpClient { BaseAddress = new Uri(url) }; f.Api.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            var mvc = Builder(typeof(MvcController)); mvc.Services.AddScoped(_ => f.Db()); mvc.Services.AddMemoryCache(); mvc.Services.AddHttpContextAccessor(); mvc.Services.AddSingleton<TimeProvider>(f.Reloj);
            mvc.Services.AddScoped<IViajesApiServicio>(sp => new ViajesApiServicio(new HttpClient(new IdentidadApi(sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), () => Interlocked.Increment(ref f.LlamadasGateway))) { BaseAddress = new Uri(url) }));
            mvc.Services.AddSingleton(f.Geo.Object); mvc.Services.AddScoped<EnviosLogica>();
            var busqueda = new Mock<IBusquedaDireccionesServicio>();
            busqueda.Setup(b => b.SugerirAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string t, CancellationToken _) =>
                new[] { new SugerenciaDireccionDto { IdHere = "here:af:" + t, Nombre = t, Localidad = "Morón", Direccion = t + ", Morón" } });
            busqueda.Setup(b => b.ResolverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, CancellationToken _) =>
                new ResultadoGeocodificacionDto { IdHere = id, DireccionNormalizada = id[8..] + ", Morón, Argentina", TipoResultado = "houseNumber",
                    Posicion = new() { Latitud = -34, Longitud = -58 }, Accesos = new() { new() { Latitud = -35, Longitud = -59 } } });
            mvc.Services.AddSingleton(busqueda.Object); mvc.Services.AddScoped<SeleccionDireccionServicio>();
            mvc.Services.AddScoped<IFuenteSeleccionUbicacionVerificada, FuenteSeleccionUbicacionVerificada>();
            mvc.Services.AddScoped<IUbicacionesConfirmadasServicio, UbicacionesConfirmadasServicio>(); mvc.Services.AddScoped<FormulariosDireccionesServicio>();
            f._mvc = Aplicacion(mvc, navegador, archivo, navegadorEnvios); await f._mvc.StartAsync();
            f.Mvc = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(Direccion(f._mvc)) };
            f.Mvc.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7"); return f;
        }
        public async Task<(string Texto, string Token)> Confirmar(string form, string campo, string texto, string csrf, string prefijo = "DireccionesViaje") {
            var url = $"/{prefijo}/{form}/";
            using var sugeridas = JsonDocument.Parse(await Mvc.GetStringAsync(url + "Sugerencias?campo=" + campo + "&texto=" + Uri.EscapeDataString(texto)));
            var sugerencia = sugeridas.RootElement[0].GetProperty("token").GetString()!;
            using var resolver = await Post(url + "Resolver?campo=" + campo, csrf, ("token", sugerencia));
            using var resuelta = JsonDocument.Parse(await resolver.Content.ReadAsStringAsync());
            using var confirmar = await Post(url + "Confirmar?campo=" + campo, csrf, ("token", resuelta.RootElement.GetProperty("token").GetString()!), ("acceso", "0"));
            Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
            using var final = JsonDocument.Parse(await confirmar.Content.ReadAsStringAsync());
            return (final.RootElement.GetProperty("ubicacion").GetProperty("direccionNormalizada").GetString()!, final.RootElement.GetProperty("token").GetString()!);
        }
        public async Task<string> Agregar(string form, string csrf) {
            using var respuesta = await Post($"/DireccionesViaje/{form}/Paradas", csrf);
            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode); return Valor(await respuesta.Content.ReadAsStringAsync(), "Paradas.Index"); }
        private Task<HttpResponseMessage> Post(string url, string csrf, params (string Key, string Value)[] datos) => Mvc.PostAsync(url,
            new FormUrlEncodedContent(datos.Select(d => new KeyValuePair<string, string>(d.Key, d.Value)).Append(new("__RequestVerificationToken", csrf))));
        private static WebApplicationBuilder Builder(Type controller) {
            var b = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = controller.Assembly.GetName().Name, EnvironmentName = "Testing" });
            b.Logging.ClearProviders(); b.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));
            b.Services.AddDataProtection().UseEphemeralDataProtectionProvider(); b.Services.AddControllersWithViews().AddApplicationPart(controller.Assembly);
            b.Services.AddAuthentication("Prueba").AddScheme<AuthenticationSchemeOptions, Auth>("Prueba", _ => { }); b.Services.AddAuthorization(); return b; }
        private static WebApplication Aplicacion(WebApplicationBuilder b, bool navegador = false, string archivo = "", bool navegadorEnvios = false) {
            var a = b.Build();
            if (navegador) {
                a.Use(async (context, next) => {
                    context.Request.Headers["X-Usuario-Prueba"] = "7";
                    if (context.Request.Method != "GET" || !(navegadorEnvios ? context.Request.Path.StartsWithSegments("/Envios") : context.Request.Path.StartsWithSegments("/Panel"))) { await next(); return; }
                    var original = context.Response.Body; await using var buffer = new MemoryStream(); context.Response.Body = buffer;
                    await next(); buffer.Position = 0; var html = await new StreamReader(buffer).ReadToEndAsync();
                    html = html.Replace("</body>", "<script src=\"/pruebas/formulario-browser.js\"></script></body>");
                    context.Response.Body = original; context.Response.ContentLength = null; await context.Response.WriteAsync(html);
                });
                a.MapGet("/js/{nombre}", (string nombre) => Results.Text(File.ReadAllText(Path.Combine(Path.GetDirectoryName(archivo)!, "..", "RuteaYa", "wwwroot", "js", Path.GetFileName(nombre))), "application/javascript"));
                a.MapGet("/pruebas/formulario-browser.js", () => Results.Text(File.ReadAllText(Path.Combine(Path.GetDirectoryName(archivo)!, "Fixtures", navegadorEnvios ? "envios-browser.js" : "viajes-browser.js")), "application/javascript"));
            }
            a.UseRouting(); a.UseAuthentication(); a.UseAuthorization();
            a.MapControllerRoute("default", "{controller=Acceso}/{action=Login}/{id?}"); return a; }
        private static string Direccion(WebApplication app) => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        public async ValueTask DisposeAsync() { Mvc?.Dispose(); Api?.Dispose(); if (_mvc != null) { await _mvc.StopAsync(); await _mvc.DisposeAsync(); }
            if (_api != null) { await _api.StopAsync(); await _api.DisposeAsync(); } await _connection.DisposeAsync(); }
    }
    internal sealed class RelojPrueba : TimeProvider { public DateTimeOffset Ahora = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Ahora; }
    private sealed class IdentidadApi(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor, Action llamada) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            llamada(); request.Headers.Add("X-Usuario-Prueba", accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)); return base.SendAsync(request, ct); }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var id = Request.Headers["X-Usuario-Prueba"].ToString();
            if (id is not ("7" or "8")) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, Scheme.Name)), Scheme.Name))); }
    }
}
