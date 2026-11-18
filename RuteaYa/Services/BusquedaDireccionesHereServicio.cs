using System.Text.Json;
using Microsoft.Extensions.Options;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Services;

/// <summary>HERE Geocoding & Search v7: autocomplete y lookup, nunca autosuggest ni coordenadas locales.</summary>
public class BusquedaDireccionesHereServicio(HttpClient cliente, IOptions<HereRoutingOpciones> opciones,
    ILogger<BusquedaDireccionesHereServicio>? logger = null)
    : IBusquedaDireccionesServicio
{
    public async Task<IReadOnlyList<SugerenciaDireccionDto>> SugerirAsync(string texto, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length is < 3 or > 200 ||
            texto.Any(char.IsControl) || !texto.Any(char.IsLetterOrDigit))
            throw new ArgumentException("Ingresá entre 3 y 200 caracteres de una dirección.");
        return await ConsultarAsync("autocomplete", "q=" + Uri.EscapeDataString(texto.Trim()) + "&limit=5&lang=es", root =>
        {
            var items = root.GetProperty("items");
            var sugerencias = new List<SugerenciaDireccionDto>();
            foreach (var item in items.EnumerateArray().Take(5))
            {
                var id = item.GetProperty("id").GetString();
                var nombre = item.GetProperty("title").GetString();
                var direccion = item.GetProperty("address");
                var etiqueta = direccion.GetProperty("label").GetString();
                if (!IdValido(id) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(etiqueta)) throw Invalida();
                var localidad = direccion.TryGetProperty("city", out var city) ? city.GetString() : null;
                sugerencias.Add(new() { IdHere = id!, Nombre = nombre!, Direccion = etiqueta!, Localidad = localidad ?? "" });
            }
            return (IReadOnlyList<SugerenciaDireccionDto>)sugerencias.AsReadOnly();
        }, cancellationToken);
    }

    public async Task<ResultadoGeocodificacionDto> ResolverAsync(string idHere, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IdValido(idHere)) throw new ArgumentException("El identificador HERE no es válido.");
        return await ConsultarAsync("lookup", "id=" + Uri.EscapeDataString(idHere) + "&lang=es", root =>
        {
            var resultado = GeocodificacionHereServicio.ConvertirLookup(root);
            if (!string.Equals(resultado.IdHere, idHere, StringComparison.Ordinal)) throw Invalida();
            return resultado;
        }, cancellationToken);
    }

    private async Task<T> ConsultarAsync<T>(string endpoint, string query, Func<JsonElement, T> convertir, CancellationToken ct)
    {
        var key = opciones.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new HereNoConfiguradoException();
        try
        {
            // Host fijo para cada endpoint. No registrar URL, cuerpo ni excepción interna.
            using var respuesta = await cliente.GetAsync($"https://{endpoint}.search.hereapi.com/v1/{endpoint}?{query}&apiKey={Uri.EscapeDataString(key)}", ct);
            if (!respuesta.IsSuccessStatusCode)
                throw new HttpRequestException("HERE no pudo completar la búsqueda.", null, respuesta.StatusCode);
            await using var contenido = await respuesta.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(contenido, cancellationToken: ct);
            ct.ThrowIfCancellationRequested();
            return convertir(json.RootElement);
        }
        catch (HttpRequestException e)
        {
            logger?.LogWarning("HERE {Servicio} falló. HTTP {Estado}. Sin URL ni cuerpo remoto.", endpoint, (int?)e.StatusCode);
            throw new HttpRequestException("No se pudo consultar HERE para buscar direcciones.", null, e.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { throw; } // Cancelación normal: no fabricar una segunda excepción. La frontera MVC la maneja.
        catch (OperationCanceledException)
        {
            logger?.LogWarning("HERE {Servicio}: timeout sin cancelación del navegador.", endpoint);
            throw new TimeoutException("Se agotó el tiempo de búsqueda de direcciones.");
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { logger?.LogWarning("HERE {Servicio}: respuesta inválida. Tipo {Tipo}.", endpoint, e.GetType().Name); throw Invalida(); }
    }

    private static bool IdValido(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512 &&
        id.StartsWith("here:", StringComparison.Ordinal) && !id.Any(char.IsControl);
    private static InvalidOperationException Invalida() => new("HERE devolvió una dirección inválida o incompleta.");
}
