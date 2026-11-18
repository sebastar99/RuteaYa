using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RuteaYa.Web.Services;

/// <summary>Proxy de destinos fijos: nunca recibe URLs del navegador ni devuelve respuestas crudas de HERE.</summary>
public class MapasHereServicio(HttpClient cliente, IOptions<HereRoutingOpciones> opciones)
{
    public async Task<List<JsonElement>> AtribucionesAsync(CancellationToken ct)
    {
        using var respuesta = await SolicitarAsync("copyright", ct);
        try
        {
            using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            var grupos = root.GetProperty("resources").GetProperty("base").GetProperty("styles").GetProperty("explore.day");
            var resultado = new List<JsonElement>();
            foreach (var grupo in grupos.EnumerateArray())
                foreach (var aviso in root.GetProperty("copyrights").GetProperty(grupo.GetString()!).EnumerateArray())
                {
                    // Copiar exclusivamente metadatos públicos conocidos, nunca el cuerpo del proveedor.
                    var label = aviso.GetProperty("label").GetString();
                    var texto = aviso.GetProperty("copyrightText").GetString();
                    var min = aviso.GetProperty("minLevel").GetInt32();
                    var max = aviso.GetProperty("maxLevel").GetInt32();
                    if (string.IsNullOrWhiteSpace(label) || min < 0 || max < min || max > 30) throw new JsonException();
                    var boxes = new List<object>();
                    foreach (var box in aviso.GetProperty("boundingBoxes").EnumerateArray())
                    {
                        var west = box.GetProperty("west").GetDecimal();
                        var east = box.GetProperty("east").GetDecimal();
                        var south = box.GetProperty("south").GetDecimal();
                        var north = box.GetProperty("north").GetDecimal();
                        if (west is < -180 or > 180 || east is < -180 or > 180 ||
                            south < -90 || north > 90 || south > north) throw new JsonException();
                        boxes.Add(new { west, east, south, north });
                    }
                    if (boxes.Count == 0) throw new JsonException();
                    resultado.Add(JsonSerializer.SerializeToElement(new { label, copyrightText = texto,
                        minLevel = min, maxLevel = max, boundingBoxes = boxes }));
                }
            return resultado;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new InvalidOperationException("HERE no devolvió atribuciones de mapa válidas."); }
    }

    public async Task<byte[]> TeselaAsync(int z, int x, int y, CancellationToken ct)
    {
        if (z is < 0 or > 18 || x < 0 || y < 0 || x >= (1 << z) || y >= (1 << z))
            throw new ArgumentException("Tesela inválida.");
        using var respuesta = await SolicitarAsync($"base/mc/{z}/{x}/{y}/png?style=explore.day", ct);
        if (respuesta.Content.Headers.ContentType?.MediaType != "image/png")
            throw new InvalidOperationException("HERE no devolvió una imagen PNG.");
        var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("HERE no devolvió una imagen PNG válida.");
        return bytes;
    }

    private async Task<HttpResponseMessage> SolicitarAsync(string recurso, CancellationToken ct)
    {
        var key = opciones.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new HereNoConfiguradoException();
        try
        {
            var respuesta = await cliente.GetAsync("https://maps.hereapi.com/v3/" + recurso +
                (recurso.Contains('?') ? "&" : "?") + "apiKey=" + Uri.EscapeDataString(key), ct);
            if (respuesta.IsSuccessStatusCode) return respuesta;
            var estado = respuesta.StatusCode;
            respuesta.Dispose();
            throw new HttpRequestException("No se pudo consultar el mapa de HERE.", null, estado);
        }
        catch (HttpRequestException e) { throw new HttpRequestException("No se pudo consultar el mapa de HERE.", null, e.StatusCode); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("Se agotó el tiempo de espera del mapa de HERE."); }
        catch (OperationCanceledException) { throw; }
    }
}
