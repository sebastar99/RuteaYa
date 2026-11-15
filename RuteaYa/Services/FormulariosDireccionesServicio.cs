using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services;

/// <summary>Estado servidor común. Viajes guarda por su API; Envíos conserva MVC → Lógica → EF.</summary>
public sealed class FormulariosDireccionesServicio(IViajesApiServicio api, IUbicacionesConfirmadasServicio persistencia,
    SeleccionDireccionServicio selecciones, IMemoryCache cache, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private sealed class Campo(ContextoUbicacionDto contexto, string texto, bool confirmada, int paradaId = 0)
    {
        public ContextoUbicacionDto Contexto = contexto;
        public string Original = texto; public bool Confirmada = confirmada; public int ParadaId = paradaId;
        public string? Token; public string? Canonica; public DateTimeOffset? Vence;
    }
    private sealed class Formulario(int usuario, int? viaje, DateTimeOffset vence)
    {
        public TipoRecursoUbicacion Tipo = TipoRecursoUbicacion.Viaje;
        public int Usuario = usuario; public int? Viaje = viaje; public DateTimeOffset Vence = vence;
        public Dictionary<string, Campo> Campos = new();
    }
    private Formulario Leer(int usuario, string token)
    {
        if (usuario <= 0 || token?.Length != 64 || !cache.TryGetValue<Formulario>("form-direcciones:" + token, out var f) ||
            f == null || f.Usuario != usuario || f.Vence <= _reloj.GetUtcNow())
            throw new SeleccionTemporalNoDisponibleException("El formulario venció o no está disponible. Volvé a abrir la pantalla de creación o edición.");
        return f;
    }
    private async Task<ContextoUbicacionDto> Contexto(Formulario f, CampoUbicacion tipo, string clave, Guid? formulario, CancellationToken ct, int? paradaId = null)
    {
        var resultado = await api.CrearContextoUbicacionAsync(new() { ViajeId = f.Viaje, FormularioId = formulario, Campo = tipo, ClaveCampo = clave, ParadaId = paradaId }, ct);
        if (!resultado.Exito || resultado.Contexto == null) throw new InvalidOperationException(resultado.Mensaje);
        if (f.Campos.Count > 0 && resultado.Contexto.RevisionRecurso != f.Campos.First().Value.Contexto.RevisionRecurso)
            throw new SeleccionTemporalNoDisponibleException("El viaje cambió mientras se preparaba el formulario. Volvé a abrirlo.");
        return resultado.Contexto;
    }
    public async Task PrepararAsync(int usuario, ViajeViewModel modelo, CancellationToken ct = default)
    {
        if (usuario <= 0) throw new ArgumentException("Usuario inválido.");
        var f = new Formulario(usuario, modelo.Id > 0 ? modelo.Id : null, _reloj.GetUtcNow().AddMinutes(10));
        var origen = await Contexto(f, CampoUbicacion.OrigenViaje, "OrigenViaje", null, ct);
        f.Campos.Add("origen", new(origen, modelo.Origen, modelo.OrigenConfirmado));
        f.Campos.Add("destino", new(await Contexto(f, CampoUbicacion.DestinoViaje, "DestinoViaje", origen.FormularioId, ct), modelo.Destino, modelo.DestinoConfirmado));
        foreach (var p in modelo.Paradas.OrderBy(p => p.Orden)) {
            p.ClaveCampo = IdentidadParadaFormulario.Existente(origen.FormularioId, p.Id);
            f.Campos.Add(p.ClaveCampo, new(await Contexto(f, CampoUbicacion.Parada, p.ClaveCampo, origen.FormularioId, ct, p.Id), p.Ubicacion, p.Confirmada, p.Id));
        }
        modelo.FormularioDirecciones = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set("form-direcciones:" + modelo.FormularioDirecciones, f, TimeSpan.FromMinutes(10));
        Rellenar(usuario, modelo);
    }
    public async Task<ParadaViewModel> AgregarParadaAsync(int usuario, string token, CancellationToken ct)
    {
        var f = Leer(usuario, token);
        if (f.Tipo != TipoRecursoUbicacion.Viaje) throw new ArgumentException("Este formulario no admite paradas.");
        if (f.Campos.Count >= 52) throw new ArgumentException("El formulario admite hasta 50 paradas. Si agotaste los campos, volvé a abrirlo.");
        var clave = Guid.NewGuid().ToString("N");
        var contexto = await Contexto(f, CampoUbicacion.Parada, clave, f.Campos["origen"].Contexto.FormularioId, ct);
        lock (f) { if (f.Campos.Count >= 52) throw new ArgumentException("Máximo 50 paradas."); f.Campos.Add(clave, new(contexto, "", false)); }
        return new() { ClaveCampo = clave, CampoDireccion = CampoVisual(f, token, clave, $"Paradas[{clave}].Ubicacion", $"Paradas[{clave}].Comprobante", "Parada", null) };
    }
    public Task<IReadOnlyList<SugerenciaSeleccionDireccion>> SugerirAsync(int usuario, string token, string clave, string texto, CancellationToken ct) =>
        selecciones.SugerirAsync(usuario, ObtenerCampo(usuario, token, clave).Contexto.AmbitoSeleccion, texto, ct);
    public Task<EstadoSeleccionDireccion> ResolverAsync(int usuario, string formulario, string clave, string token, CancellationToken ct) =>
        selecciones.ResolverAsync(usuario, ObtenerCampo(usuario, formulario, clave).Contexto.AmbitoSeleccion, token, ct);
    public async Task<EstadoSeleccionDireccion> ConfirmarAsync(int usuario, string formulario, string clave, string token, int? acceso, CancellationToken ct)
    {
        var campo = ObtenerCampo(usuario, formulario, clave);
        var estado = selecciones.Confirmar(usuario, campo.Contexto.AmbitoSeleccion, token, acceso);
        var comprobante = await persistencia.EmitirAsync(usuario, campo.Contexto, estado.Token, ct);
        Leer(usuario, formulario);
        campo.Token = comprobante.Token; campo.Canonica = estado.Ubicacion.DireccionNormalizada; campo.Vence = comprobante.VenceEnUtc;
        estado.Token = comprobante.Token;
        return estado;
    }
    private Campo ObtenerCampo(int usuario, string formulario, string clave)
    {
        var f = Leer(usuario, formulario);
        lock (f) { if (f.Campos.TryGetValue(clave, out var campo)) return campo; }
        throw new ArgumentException("El campo no pertenece al formulario.");
    }
    public void PrepararGuardado(int usuario, ViajeViewModel modelo)
    {
        var f = Leer(usuario, modelo.FormularioDirecciones!);
        if (f.Tipo != TipoRecursoUbicacion.Viaje) throw new ArgumentException("El formulario no corresponde a Viajes.");
        if (f.Viaje != (modelo.Id > 0 ? modelo.Id : null)) throw new ArgumentException("El formulario no corresponde al viaje.");
        CampoViajeUbicacionDto Entrada(string clave, string texto, string? token, int paradaId = 0)
        {
            var c = ObtenerCampo(usuario, modelo.FormularioDirecciones!, clave);
            if (paradaId != c.ParadaId || (!string.IsNullOrEmpty(token) && (token != c.Token || texto != c.Canonica || c.Vence <= _reloj.GetUtcNow())) ||
                string.IsNullOrEmpty(token) && (!f.Viaje.HasValue || texto != c.Original))
                throw new ArgumentException("Elegí y confirmá cada dirección nueva o modificada.");
            return new() { Contexto = c.Contexto, Comprobante = token, Texto = texto, IdParada = paradaId };
        }
        if (modelo.Paradas == null || modelo.Paradas.Count > 50 || modelo.Paradas.Select(p => p.ClaveCampo).Distinct().Count() != modelo.Paradas.Count)
            throw new ArgumentException("La lista de paradas no es válida.");
        modelo.Ubicaciones = new() {
            Origen = Entrada("origen", modelo.Origen, modelo.OrigenComprobante), Destino = Entrada("destino", modelo.Destino, modelo.DestinoComprobante),
            Paradas = modelo.Paradas.Select(p => Entrada(p.ClaveCampo, p.Ubicacion, p.Comprobante, p.Id)).ToList()
        };
    }
    public void Rellenar(int usuario, ViajeViewModel modelo)
    {
        var f = Leer(usuario, modelo.FormularioDirecciones!);
        if (f.Tipo != TipoRecursoUbicacion.Viaje) throw new ArgumentException("El formulario no corresponde a Viajes.");
        modelo.OrigenCampo = CampoVisual(f, modelo.FormularioDirecciones!, "origen", "Origen", "OrigenComprobante", "Origen", modelo.OrigenComprobante);
        modelo.DestinoCampo = CampoVisual(f, modelo.FormularioDirecciones!, "destino", "Destino", "DestinoComprobante", "Destino", modelo.DestinoComprobante);
        foreach (var p in modelo.Paradas)
            p.CampoDireccion = CampoVisual(f, modelo.FormularioDirecciones!, p.ClaveCampo, $"Paradas[{p.ClaveCampo}].Ubicacion", $"Paradas[{p.ClaveCampo}].Comprobante", "Parada", p.Comprobante);
    }
    private static CampoDireccionViewModel CampoVisual(Formulario f, string token, string clave, string texto, string comprobante, string etiqueta, string? publicado)
    {
        if (!f.Campos.TryGetValue(clave, out var c)) throw new ArgumentException("El campo no pertenece al formulario.");
        var prefijo = (f.Tipo == TipoRecursoUbicacion.Viaje ? "/DireccionesViaje/" : "/DireccionesEnvio/") + token + "/";
        return new() { IdHtml = (f.Tipo == TipoRecursoUbicacion.Viaje ? "viaje-" : "envio-") + clave, Etiqueta = etiqueta, NombreTexto = texto, NombreToken = comprobante,
            TextoInicial = publicado != null && publicado == c.Token ? c.Canonica! : c.Original,
            TokenInicial = publicado != null && publicado == c.Token ? c.Token : null,
            ConservarActual = f.Viaje.HasValue && c.Token == null && (clave is "origen" or "destino" || c.ParadaId > 0),
            ConfirmadaPersistida = c.Confirmada,
            UrlSugerencias = prefijo + "Sugerencias?campo=" + clave, UrlResolver = prefijo + "Resolver?campo=" + clave,
            UrlConfirmar = prefijo + "Confirmar?campo=" + clave };
    }

    public async Task PrepararEnvioAsync(int usuario, CrearEnvioViewModel modelo, CancellationToken ct = default)
    {
        var f = new Formulario(usuario, null, _reloj.GetUtcNow().AddMinutes(10)) { Tipo = TipoRecursoUbicacion.Envio };
        var retiro = await persistencia.CrearContextoAsync(usuario, TipoRecursoUbicacion.Envio, null, CampoUbicacion.RetiroEnvio, "RetiroEnvio", ct: ct);
        var entrega = await persistencia.CrearContextoAsync(usuario, TipoRecursoUbicacion.Envio, null, CampoUbicacion.EntregaEnvio, "EntregaEnvio", retiro.FormularioId, ct);
        f.Campos.Add("retiro", new(retiro, "", false)); f.Campos.Add("entrega", new(entrega, "", false));
        modelo.FormularioDirecciones = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set("form-direcciones:" + modelo.FormularioDirecciones, f, TimeSpan.FromMinutes(10));
        RellenarEnvio(usuario, modelo);
    }
    public void PrepararGuardadoEnvio(int usuario, CrearEnvioViewModel modelo)
    {
        modelo.Ubicaciones = null; // jamás utilizar el contexto o los flags ligados libremente por MVC.
        var f = Leer(usuario, modelo.FormularioDirecciones!);
        if (f.Tipo != TipoRecursoUbicacion.Envio) throw new ArgumentException("El formulario no corresponde a Crear Envío.");
        Campo Verificar(string clave, string texto, string? token) {
            var campo = ObtenerCampo(usuario, modelo.FormularioDirecciones!, clave);
            if (string.IsNullOrEmpty(token) || campo.Token != token || campo.Canonica != texto || campo.Vence <= _reloj.GetUtcNow())
                throw new ArgumentException("Elegí y confirmá las direcciones de retiro y entrega.");
            return campo;
        }
        var retiro = Verificar("retiro", modelo.OrigenDir, modelo.RetiroComprobante);
        var entrega = Verificar("entrega", modelo.DestinoDir, modelo.EntregaComprobante);
        modelo.Ubicaciones = new() { Retiro = retiro.Contexto, Entrega = entrega.Contexto,
            ComprobanteRetiro = modelo.RetiroComprobante!, ComprobanteEntrega = modelo.EntregaComprobante! };
    }
    public void RellenarEnvio(int usuario, CrearEnvioViewModel modelo)
    {
        var f = Leer(usuario, modelo.FormularioDirecciones!);
        if (f.Tipo != TipoRecursoUbicacion.Envio) throw new ArgumentException("El formulario no corresponde a Crear Envío.");
        modelo.RetiroCampo = CampoVisual(f, modelo.FormularioDirecciones!, "retiro", "OrigenDir", "RetiroComprobante", "Dirección de retiro", modelo.RetiroComprobante);
        modelo.EntregaCampo = CampoVisual(f, modelo.FormularioDirecciones!, "entrega", "DestinoDir", "EntregaComprobante", "Dirección de entrega", modelo.EntregaComprobante);
    }
}
