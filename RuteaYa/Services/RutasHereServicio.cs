using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Services;

/// <summary>
/// Adaptador de HERE Routing API v8. Usa automóvil, paradas stopover en orden
/// y departureTime=any (sin hora de salida ni tráfico en tiempo real).
/// Suma los resúmenes de todas las secciones de la primera ruta devuelta.
/// </summary>
public class RutasHereServicio : IRutasServicio
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<HereRoutingOpciones> _opciones;

    public RutasHereServicio(HttpClient httpClient, IOptions<HereRoutingOpciones> opciones)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(opciones);
        _httpClient = httpClient;
        _opciones = opciones;
    }

    public async Task<ResultadoRutaDto> CalcularAsync(
        IReadOnlyList<PuntoRutaDto> puntos,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var coordenadas = ValidarYFormatear(puntos);
        var apiKey = _opciones.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "No se configuró HereRouting:ApiKey. Usá User Secrets o variables de entorno.");

        var parametros = new List<string>
        {
            "transportMode=car", "routingMode=fast", "departureTime=any",
            "return=summary", "alternatives=0",
            $"origin={Uri.EscapeDataString(coordenadas[0])}",
            $"destination={Uri.EscapeDataString(coordenadas[^1])}"
        };
        foreach (var coordenada in coordenadas.Skip(1).Take(coordenadas.Length - 2))
            parametros.Add($"via={Uri.EscapeDataString(coordenada)}");
        parametros.Add($"apiKey={Uri.EscapeDataString(apiKey)}");

        var url = "https://router.hereapi.com/v8/routes?" + string.Join("&", parametros);
        try
        {
            // ResponseContentRead permite que el timeout de HttpClient cubra también el cuerpo.
            using var respuesta = await _httpClient.GetAsync(url, cancellationToken);
            if (!respuesta.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"HERE Routing devolvió HTTP {(int)respuesta.StatusCode}.",
                    null, respuesta.StatusCode);

            await using var contenido = await respuesta.Content.ReadAsStreamAsync(cancellationToken);
            var datos = await JsonSerializer.DeserializeAsync<RespuestaHere>(
                contenido, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ObtenerTotales(datos);
        }
        catch (HttpRequestException error)
        {
            // No incluir URL, cuerpo ni excepción interna: pueden contener la API Key.
            throw new HttpRequestException(
                error.StatusCode.HasValue
                    ? $"HERE Routing devolvió HTTP {(int)error.StatusCode.Value}."
                    : "No se pudo completar la conexión con HERE Routing.",
                null, error.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Se canceló la consulta a HERE Routing.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Se agotó el tiempo de espera de HERE Routing.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("HERE Routing devolvió una respuesta JSON inválida.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("Los totales de HERE Routing exceden el rango permitido.");
        }
    }

    private static string[] ValidarYFormatear(IReadOnlyList<PuntoRutaDto> puntos)
    {
        ArgumentNullException.ThrowIfNull(puntos);
        if (puntos.Count < 2)
            throw new ArgumentException("El recorrido debe contener al menos dos puntos.", nameof(puntos));

        var coordenadas = new string[puntos.Count];
        for (var i = 0; i < puntos.Count; i++)
        {
            var punto = puntos[i];
            if (punto == null || punto.Latitud < -90 || punto.Latitud > 90 ||
                punto.Longitud < -180 || punto.Longitud > 180)
                throw new ArgumentException(
                    $"El punto en la posición {i} debe tener latitud entre -90 y 90 y longitud entre -180 y 180.",
                    nameof(puntos));

            coordenadas[i] = punto.Latitud.ToString(CultureInfo.InvariantCulture) + "," +
                punto.Longitud.ToString(CultureInfo.InvariantCulture);
        }
        return coordenadas;
    }

    private static ResultadoRutaDto ObtenerTotales(RespuestaHere? datos)
    {
        if (datos == null)
            throw new InvalidOperationException("HERE Routing devolvió una respuesta vacía.");
        ValidarAvisos(datos.Notices);

        var ruta = datos.Routes?.FirstOrDefault();
        if (ruta == null || ruta.Sections == null || ruta.Sections.Count == 0)
            throw new InvalidOperationException("HERE Routing no devolvió una ruta con secciones.");
        ValidarAvisos(ruta.Notices);

        long metros = 0;
        long segundos = 0;
        foreach (var seccion in ruta.Sections)
        {
            var resumen = seccion?.Summary;
            if (resumen?.Length == null || resumen.Duration == null ||
                resumen.Length < 0 || resumen.Duration < 0)
                throw new InvalidOperationException(
                    "HERE Routing devolvió una sección sin distancia y duración válidas.");
            ValidarAvisos(seccion!.Notices);
            metros = checked(metros + resumen.Length.Value);
            segundos = checked(segundos + resumen.Duration.Value);
        }
        return new ResultadoRutaDto { DistanciaMetros = metros, DuracionSegundos = segundos };
    }

    private static void ValidarAvisos(List<AvisoHere?>? avisos)
    {
        if (avisos?.Any(a => a?.Severity == "critical" || a?.Code == "noRouteFound") == true)
            throw new InvalidOperationException(
                "HERE Routing informó un error o una restricción crítica; la ruta no es utilizable.");
    }

    // Modelos exclusivos del adaptador: no forman parte del contrato de negocio.
    private sealed class RespuestaHere
    {
        [JsonPropertyName("routes")]
        public List<RutaHere?>? Routes { get; set; }
        [JsonPropertyName("notices")]
        public List<AvisoHere?>? Notices { get; set; }
    }

    private sealed class RutaHere
    {
        [JsonPropertyName("sections")]
        public List<SeccionHere?>? Sections { get; set; }
        [JsonPropertyName("notices")]
        public List<AvisoHere?>? Notices { get; set; }
    }

    private sealed class SeccionHere
    {
        [JsonPropertyName("summary")]
        public ResumenHere? Summary { get; set; }
        [JsonPropertyName("notices")]
        public List<AvisoHere?>? Notices { get; set; }
    }

    private sealed class ResumenHere
    {
        [JsonPropertyName("length")]
        public long? Length { get; set; }
        [JsonPropertyName("duration")]
        public long? Duration { get; set; }
    }

    private sealed class AvisoHere
    {
        [JsonPropertyName("severity")]
        public string? Severity { get; set; }
        [JsonPropertyName("code")]
        public string? Code { get; set; }
    }
}
