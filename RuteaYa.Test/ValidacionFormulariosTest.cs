using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;
using RuteaYa.Entidades;
using Host = RuteaYa.Test.ViajesDireccionesIntegracionTest.Host;

namespace RuteaYa.Test;

public class ValidacionFormulariosTest
{
    static string Valor(string html, string nombre) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(nombre)}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    static void CampoInvalido(string html, string nombre)
    {
        Assert.Matches($"<(?:input|select)[^>]*class=\"[^\"]*input-validation-error[^\"]*\"[^>]*name=\"{Regex.Escape(nombre)}\"|<(?:input|select)[^>]*name=\"{Regex.Escape(nombre)}\"[^>]*class=\"[^\"]*input-validation-error", html);
        Assert.Matches($"data-valmsg-for=\"{Regex.Escape(nombre)}\"[^>]*>[^<]+", html);
    }

    [Fact]
    public async Task RegistroSinJavascript_DevuelveTodosLosErroresYConservaNombreValido()
    {
        await using var f = await Host.Crear(navegador:true, validacion:true); f.Mvc.DefaultRequestHeaders.Remove("X-Usuario-Prueba");
        var html = await f.Mvc.GetStringAsync("/Acceso/Registro");
        using var r = await f.Mvc.PostAsync("/Acceso/Registro", new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"] = Valor(html,"__RequestVerificationToken"), ["Nombre"] = "Ana", ["Apellido"] = "", ["Email"] = "correo-invalido", ["Password"] = "abc", ["ConfirmarPassword"] = "otro" }));
        Assert.Equal(HttpStatusCode.OK,r.StatusCode); html=await r.Content.ReadAsStringAsync();
        foreach(var campo in new[]{"Apellido","Email","Password","ConfirmarPassword"}) CampoInvalido(html,campo);
        Assert.Equal("Ana",Valor(html,"Nombre")); Assert.Contains("El correo no es válido",WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task ViajeSinJavascript_ConservaComprobantesYReportaCamposInvalidosJuntos()
    {
        await using var f=await Host.Crear(); var html=await f.Mvc.GetStringAsync("/Panel/NuevoViaje");
        var csrf=Valor(html,"__RequestVerificationToken"); var form=Valor(html,"FormularioDirecciones");
        var origen=await f.Confirmar(form,"origen","Origen válido",csrf); var destino=await f.Confirmar(form,"destino","Destino válido",csrf);
        using var r=await f.Mvc.PostAsync("/Panel/NuevoViaje",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"]=csrf,["FormularioDirecciones"]=form,["Origen"]=origen.Texto,["Destino"]=destino.Texto,
            ["OrigenComprobante"]=origen.Token,["DestinoComprobante"]=destino.Token,["HoraSalida"]="",["HoraLlegada"]="",["PesoMax"]="0",["CantMaxPaquetes"]="0",["DesvioMaxKm"]="-1" }));
        Assert.Equal(HttpStatusCode.OK,r.StatusCode); html=await r.Content.ReadAsStringAsync();
        foreach(var campo in new[]{"HoraSalida","HoraLlegada","PesoMax","CantMaxPaquetes","DesvioMaxKm"}) CampoInvalido(html,campo);
        Assert.Equal(origen.Token,Valor(html,"OrigenComprobante")); Assert.Equal(destino.Token,Valor(html,"DestinoComprobante"));
        await using var db=f.Db(); Assert.Empty(await db.Viajes.ToListAsync()); Assert.All(await db.UbicacionesConfirmadas.ToListAsync(),u=>Assert.Null(u.ConsumidaEnUtc));
    }

    [Fact]
    public async Task SalidaSinJavascript_DevuelveFormularioConErroresPorCampoEnVezDePerderLosDatos()
    {
        await using var f=await Host.Crear(navegador:true,validacion:true);
        await using(var db=f.Db()) { db.Viajes.Add(new Viaje { UsuarioId=7,Origen="Origen guardado",Destino="Destino guardado",Activo=true,HoraSalida=TimeSpan.FromHours(8),HoraLlegada=TimeSpan.FromHours(9),CantMaxPaquetes=2,PesoMax=10,Dias=new List<DiaViaje>{new(){DiaSemana=1}} }); await db.SaveChangesAsync(); }
        var html=await f.Mvc.GetStringAsync("/Oportunidades?viajeId=1");
        using var r=await f.Mvc.PostAsync("/Oportunidades/SeleccionarSalida",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"]=Valor(html,"__RequestVerificationToken"),["ViajeId"]="1",["Fecha"]="",["ZonaHorariaId"]="UTC",["LlegadaSinRestriccion"]="false",["LlegadaMaximaLocal"]="no-es-fecha" }));
        Assert.Equal(HttpStatusCode.OK,r.StatusCode); html=await r.Content.ReadAsStringAsync();
        CampoInvalido(html,"Fecha"); CampoInvalido(html,"LlegadaMaximaLocal"); Assert.Matches("<option(?=[^>]*value=\"UTC\")(?=[^>]*selected=\"selected\")[^>]*>",html);
        await using var verificacion=f.Db(); Assert.Empty(await verificacion.SalidasViaje.ToListAsync());
    }

    [EdgeFact]
    public async Task Navegador_TodosLosFormularios_MuestranTodosLosErrores_YConservanHereVigente()
    {
        await using var f = await Host.Crear(navegador:true, validacion:true);
        await using(var db=f.Db()) {
            db.Viajes.Add(new Viaje { UsuarioId=7,Origen="Origen guardado",Destino="Destino guardado",Activo=true,HoraSalida=TimeSpan.FromHours(8),HoraLlegada=TimeSpan.FromHours(9),
                CantMaxPaquetes=2,PesoMax=10,DesvioMaxKm=3,Dias=new List<DiaViaje>{new(){DiaSemana=1}},
                Paradas=new List<Parada>{new(){Orden=1,Ubicacion="Parada uno"},new(){Orden=2,Ubicacion="Parada dos"}} });
            await db.SaveChangesAsync();
        }
        var resultado = await Helpers.EdgeNavegador.EjecutarAsync(f.Mvc.BaseAddress+"Acceso/Registro");
        Assert.True(resultado == "OK", resultado);
        await using var verificacion=f.Db(); Assert.Single(await verificacion.Viajes.ToListAsync()); Assert.Empty(await verificacion.Envios.ToListAsync());
        Assert.Equal(2,await verificacion.Usuarios.CountAsync());
        Assert.All(await verificacion.UbicacionesConfirmadas.ToListAsync(),u=>Assert.Null(u.ConsumidaEnUtc));
    }
}
