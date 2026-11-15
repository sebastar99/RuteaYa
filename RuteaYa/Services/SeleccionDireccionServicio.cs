using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using RuteaYa.Logica;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services;

/// <summary>Capacidades temporales por usuario y campo. El consumidor autoriza el recurso y construye el ámbito.</summary>
public class SeleccionDireccionServicio(IBusquedaDireccionesServicio busqueda, IMemoryCache cache, TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private sealed record Entrada(int UsuarioId, string Ambito, DateTimeOffset Vence, string IdHere, string? Estado);

    public async Task<IReadOnlyList<SugerenciaSeleccionDireccion>> SugerirAsync(int usuarioId, string ambito, string texto, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidarContexto(usuarioId, ambito);
        var sugerencias = await busqueda.SugerirAsync(texto, ct);
        ct.ThrowIfCancellationRequested();
        return sugerencias.Select(s => new SugerenciaSeleccionDireccion(
            Guardar("sugerencia", new(usuarioId, ambito, _reloj.GetUtcNow().AddMinutes(10), s.IdHere, null)), s)).ToArray();
    }

    public async Task<EstadoSeleccionDireccion> ResolverAsync(int usuarioId, string ambito, string token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var entrada = Leer("sugerencia", usuarioId, ambito, token);
        var ubicacion = await busqueda.ResolverAsync(entrada.IdHere, ct);
        ct.ThrowIfCancellationRequested();
        if (ubicacion.IdHere != entrada.IdHere) throw new InvalidOperationException("La ubicación no corresponde a la sugerencia elegida.");
        var estado = new EstadoSeleccionDireccion { Ubicacion = ubicacion };
        // Revalidar vencimiento tras el lookup. No elegir acceso ni confirmar automáticamente.
        Leer("sugerencia", usuarioId, ambito, token);
        estado.Token = Guardar("resuelta", entrada with { Estado = JsonSerializer.Serialize(estado) });
        return estado;
    }

    public EstadoSeleccionDireccion Confirmar(int usuarioId, string ambito, string token, int? acceso)
    {
        var entrada = Leer("resuelta", usuarioId, ambito, token);
        var estado = JsonSerializer.Deserialize<EstadoSeleccionDireccion>(entrada.Estado!)!;
        if (acceso is not int indice || indice < -1 || indice >= estado.Ubicacion.Accesos.Count)
            throw new ArgumentException("Elegí position o un access válido para confirmar.");
        estado.Acceso = indice;
        estado.Navegacion = indice == -1 ? estado.Ubicacion.Posicion : estado.Ubicacion.Accesos[indice];
        estado.Confirmada = true;
        estado.Token = Guardar("confirmada", entrada with { Estado = JsonSerializer.Serialize(estado) });
        return estado;
    }

    public EstadoSeleccionDireccion ObtenerConfirmada(int usuarioId, string ambito, string token)
    {
        var entrada = Leer("confirmada", usuarioId, ambito, token);
        var estado = JsonSerializer.Deserialize<EstadoSeleccionDireccion>(entrada.Estado!)!;
        if (!estado.Confirmada || estado.Navegacion == null) throw new ArgumentException("Falta confirmar la ubicación.");
        estado.Token = token;
        return estado;
    }

    private string Guardar(string etapa, Entrada entrada)
    {
        var restante = entrada.Vence - _reloj.GetUtcNow();
        if (restante <= TimeSpan.Zero) throw new SeleccionTemporalNoDisponibleException("La selección venció. Buscá nuevamente.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        cache.Set($"direccion:{etapa}:{token}", entrada, restante);
        return token;
    }

    private Entrada Leer(string etapa, int usuarioId, string ambito, string token)
    {
        ValidarContexto(usuarioId, ambito);
        if (string.IsNullOrEmpty(token) || token.Length != 64 ||
            !cache.TryGetValue<Entrada>($"direccion:{etapa}:{token}", out var entrada) || entrada == null ||
            entrada.UsuarioId != usuarioId || entrada.Ambito != ambito || entrada.Vence <= _reloj.GetUtcNow())
            throw new SeleccionTemporalNoDisponibleException("La selección no está disponible o venció. Volvé a elegir la dirección.");
        return entrada;
    }

    private static void ValidarContexto(int usuarioId, string ambito)
    {
        if (usuarioId <= 0 || string.IsNullOrWhiteSpace(ambito)) throw new ArgumentException("El contexto de dirección no es válido.");
    }
}
