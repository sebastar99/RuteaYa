using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services;

/// <summary>Estado efímero de la pantalla. No es una fuente de datos confirmados del motor.</summary>
public class PrevisualizacionRecorridoServicio(
    IViajesLogica viajes, IGeocodificacionServicio geocodificacion,
    IRutasGeometriaServicio rutas, IMemoryCache cache, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private sealed record Sesion(int UsuarioId, int ViajeId, string Huella, DateTimeOffset Vence, string Modelo);
    private static string Clave(string token) => "previsualizacion-recorrido:" + token;

    public async Task<PrevisualizacionRecorridoViewModel?> ObtenerAsync(
        int usuarioId, int viajeId, string? token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var viaje = await viajes.ObtenerPorIdAsync(usuarioId, viajeId);
        if (viaje == null || viaje.UsuarioId != usuarioId) return null;
        if (string.IsNullOrEmpty(token))
            return new() { ViajeId = viaje.Id, Origen = viaje.Origen, Destino = viaje.Destino };
        var sesion = LeerSesion(usuarioId, viajeId, token);
        if (sesion.Huella != Huella(viaje))
        {
            cache.Remove(Clave(token));
            throw new SeleccionTemporalNoDisponibleException("El viaje cambió. Renová la previsualización antes de continuar.");
        }
        return JsonSerializer.Deserialize<PrevisualizacionRecorridoViewModel>(sesion.Modelo)!;
    }

    public Task<PrevisualizacionRecorridoViewModel?> BuscarAsync(
        int usuarioId, int viajeId, CancellationToken ct = default) => CrearSesionAsync(usuarioId, viajeId, false, ct);

    public Task<PrevisualizacionRecorridoViewModel?> IniciarAutocompletadoAsync(
        int usuarioId, int viajeId, CancellationToken ct = default) => CrearSesionAsync(usuarioId, viajeId, true, ct);

    private async Task<PrevisualizacionRecorridoViewModel?> CrearSesionAsync(
        int usuarioId, int viajeId, bool autocompletado, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var viaje = await viajes.ObtenerPorIdAsync(usuarioId, viajeId);
        if (viaje == null || viaje.UsuarioId != usuarioId) return null;
        if (viaje.Paradas.Count > 18 || viaje.Paradas.Select(p => p.Orden).Distinct().Count() != viaje.Paradas.Count)
            throw new ArgumentException("La previsualización admite hasta 18 paradas, con orden único.");
        var modelo = new PrevisualizacionRecorridoViewModel
        {
            ViajeId = viaje.Id, Origen = viaje.Origen, Destino = viaje.Destino,
            Sesion = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), UsaAutocompletado = autocompletado
        };
        var direcciones = new List<(string Etiqueta, string Direccion)> { ("Origen", viaje.Origen) };
        direcciones.AddRange(viaje.Paradas.OrderBy(p => p.Orden).Select(p => ($"Parada {p.Orden}", p.Ubicacion)));
        direcciones.Add(("Destino", viaje.Destino));
        foreach (var (etiqueta, direccion) in direcciones)
            modelo.Ubicaciones.Add(new()
            {
                Etiqueta = etiqueta, Direccion = direccion,
                Candidatos = autocompletado ? new() : (await geocodificacion.GeocodificarAsync(direccion, ct)).ToList()
            });
        ct.ThrowIfCancellationRequested();
        var sesion = new Sesion(usuarioId, viajeId, Huella(viaje), _reloj.GetUtcNow().AddMinutes(10),
            JsonSerializer.Serialize(modelo));
        cache.Set(Clave(modelo.Sesion), sesion, TimeSpan.FromMinutes(10));
        return modelo;
    }

    public async Task<PrevisualizacionRecorridoViewModel?> ConfirmarAsync(
        int usuarioId, int viajeId, ConfirmarRecorridoFormulario formulario, CancellationToken ct = default)
    {
        var modelo = await ObtenerAsync(usuarioId, viajeId, formulario.Sesion, ct);
        if (modelo == null) return null;
        if (modelo.UsaAutocompletado) throw new ArgumentException("Confirmá las direcciones mediante el autocompletado.");
        // Un token vacío nunca permite confirmar el modelo inicial.
        var sesion = LeerSesion(usuarioId, viajeId, formulario.Sesion);
        if (formulario.Selecciones == null || formulario.Selecciones.Count != modelo.Ubicaciones.Count)
            throw new ArgumentException("Confirmá un candidato y un punto de navegación para cada ubicación.");
        var puntos = new List<PuntoRutaDto>();
        var confirmados = new List<ResultadoGeocodificacionDto>();
        for (var i = 0; i < modelo.Ubicaciones.Count; i++)
        {
            var seleccion = formulario.Selecciones[i];
            var candidatos = modelo.Ubicaciones[i].Candidatos;
            if (seleccion == null || seleccion.Candidato is not int candidato || candidato < 0 || candidato >= candidatos.Count ||
                seleccion.Acceso is not int acceso || acceso < -1 || acceso >= candidatos[candidato].Accesos.Count)
                throw new ArgumentException("Confirmá un candidato y un punto de navegación válido para cada ubicación.");
            var elegido = candidatos[candidato];
            puntos.Add(acceso == -1 ? elegido.Posicion : elegido.Accesos[acceso]);
            confirmados.Add(elegido);
        }
        modelo.PuntosConfirmados = puntos;
        modelo.CandidatosConfirmados = confirmados;
        modelo.AccesosConfirmados = formulario.Selecciones.Select(s => s.Acceso!.Value).ToList();
        return await CalcularYGuardarAsync(usuarioId, viajeId, formulario.Sesion, modelo, sesion, ct);
    }

    public static string AmbitoDireccion(string sesion, int ubicacion) => $"recorrido:{sesion}:{ubicacion}";

    public async Task<PrevisualizacionRecorridoViewModel?> ConfirmarDireccionesAsync(
        int usuarioId, int viajeId, ConfirmarDireccionesRecorridoFormulario formulario,
        SeleccionDireccionServicio direcciones, CancellationToken ct = default)
    {
        var modelo = await ObtenerAsync(usuarioId, viajeId, formulario.Sesion, ct);
        if (modelo == null) return null;
        var sesion = LeerSesion(usuarioId, viajeId, formulario.Sesion);
        if (!modelo.UsaAutocompletado || formulario.Confirmaciones == null || formulario.Confirmaciones.Count != modelo.Ubicaciones.Count)
            throw new ArgumentException("Confirmá cada dirección de este recorrido.");
        modelo.ConfirmacionesDirecciones = formulario.Confirmaciones.Select((token, i) =>
            direcciones.ObtenerConfirmada(usuarioId, AmbitoDireccion(formulario.Sesion, i), token)).ToList();
        modelo.PuntosConfirmados = modelo.ConfirmacionesDirecciones.Select(c => c.Navegacion!).ToList();
        modelo.CandidatosConfirmados = modelo.ConfirmacionesDirecciones.Select(c => c.Ubicacion).ToList();
        modelo.AccesosConfirmados = modelo.ConfirmacionesDirecciones.Select(c => c.Acceso!.Value).ToList();
        return await CalcularYGuardarAsync(usuarioId, viajeId, formulario.Sesion, modelo, sesion, ct);
    }

    private async Task<PrevisualizacionRecorridoViewModel?> CalcularYGuardarAsync(int usuarioId, int viajeId,
        string token, PrevisualizacionRecorridoViewModel modelo, Sesion sesion, CancellationToken ct)
    {
        modelo.Ruta = await rutas.CalcularGeometriaAsync(modelo.PuntosConfirmados, ct);
        // Revalidar propiedad, contenido y vencimiento después de la llamada externa.
        if (await ObtenerAsync(usuarioId, viajeId, token, ct) == null) return null;
        cache.Set(Clave(token), sesion with { Modelo = JsonSerializer.Serialize(modelo) },
            sesion.Vence - _reloj.GetUtcNow());
        return modelo;
    }

    private Sesion LeerSesion(int usuarioId, int viajeId, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64 ||
            !cache.TryGetValue<Sesion>(Clave(token), out var sesion) || sesion == null ||
            sesion.UsuarioId != usuarioId || sesion.ViajeId != viajeId || sesion.Vence <= _reloj.GetUtcNow())
            throw new SeleccionTemporalNoDisponibleException("La previsualización venció o no está disponible. Renová la previsualización.");
        return sesion;
    }

    private static string Huella(ViajeDto viaje) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { viaje.Id, viaje.UsuarioId, viaje.Origen, viaje.Destino,
            Paradas = viaje.Paradas.OrderBy(p => p.Orden).Select(p => new { p.Id, p.Orden, p.Ubicacion }) }))));
}
