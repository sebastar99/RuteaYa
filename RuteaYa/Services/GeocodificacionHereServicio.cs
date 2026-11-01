using System.Text.Json;
using Microsoft.Extensions.Options;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Web.Services;

/// <summary>
/// Adaptador de HERE Geocoding & Search v7. Mantiene candidatos, posición de
/// referencia y accesos separados. No selecciona una dirección ni la da por exacta.
/// Reutiliza la configuración HereRouting:ApiKey sin copiar credenciales.
/// </summary>
public class GeocodificacionHereServicio : IGeocodificacionServicio
{
    private static readonly JsonSerializerOptions JsonOpciones = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly HttpClient _httpClient;
    private readonly IOptions<HereRoutingOpciones> _opciones;

    public GeocodificacionHereServicio(HttpClient httpClient, IOptions<HereRoutingOpciones> opciones)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(opciones);
        _httpClient = httpClient;
        _opciones = opciones;
    }

    public async Task<IReadOnlyList<ResultadoGeocodificacionDto>> GeocodificarAsync(
        string direccion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(direccion) || direccion.Any(char.IsControl) ||
            !direccion.Any(char.IsLetterOrDigit))
            throw new ArgumentException(
                "La dirección debe contener texto o números y no puede incluir caracteres de control.",
                nameof(direccion));

        var apiKey = _opciones.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No se configuró HereRouting:ApiKey para geocodificación.");

        // No limitar a un candidato ni filtrar tipos: podría ocultar ambigüedad o baja precisión.
        var url = "https://geocode.search.hereapi.com/v1/geocode?q=" + Uri.EscapeDataString(direccion.Trim()) +
            "&addressNamesMode=normalized&apiKey=" + Uri.EscapeDataString(apiKey);
        try
        {
            // El timeout de HttpClient incluye la descarga del cuerpo (ResponseContentRead).
            using var respuesta = await _httpClient.GetAsync(url, cancellationToken);
            if (!respuesta.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"HERE Geocoding devolvió HTTP {(int)respuesta.StatusCode}.", null, respuesta.StatusCode);

            await using var contenido = await respuesta.Content.ReadAsStreamAsync(cancellationToken);
            var datos = await JsonSerializer.DeserializeAsync<RespuestaHere>(
                contenido, JsonOpciones, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (datos?.Items == null)
                throw RespuestaInvalida();

            var resultados = new List<ResultadoGeocodificacionDto>();
            foreach (var item in datos.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                resultados.Add(Convertir(item));
            }
            return resultados.AsReadOnly();
        }
        catch (HttpRequestException error)
        {
            // Ni URL, consulta, cuerpo remoto ni excepción interna: pueden incluir datos sensibles.
            throw new HttpRequestException(
                error.StatusCode.HasValue
                    ? $"HERE Geocoding devolvió HTTP {(int)error.StatusCode.Value}."
                    : "No se pudo completar la conexión con HERE Geocoding.", null, error.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Se canceló la geocodificación.", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Se agotó el tiempo de espera de HERE Geocoding.");
        }
        catch (JsonException)
        {
            throw RespuestaInvalida();
        }
    }

    private static ResultadoGeocodificacionDto Convertir(ItemHere? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Id) ||
            string.IsNullOrWhiteSpace(item.Address?.Label) || string.IsNullOrWhiteSpace(item.ResultType))
            throw RespuestaInvalida();

        var resultado = new ResultadoGeocodificacionDto
        {
            IdHere = item.Id,
            DireccionNormalizada = item.Address.Label,
            Posicion = ConvertirCoordenadas(item.Position),
            Accesos = item.Access?.Select(ConvertirCoordenadas).ToList() ?? new(),
            TipoResultado = item.ResultType,
            TipoNumeroPuerta = item.HouseNumberType,
            NumeroPuertaCorregido = item.HouseNumberFallback,
            PosicionEstimada = item.EstimatedPointAddress,
            PuntajeConsulta = item.Scoring?.QueryScore
        };
        if (resultado.PuntajeConsulta is decimal puntaje)
            ValidarPuntaje(puntaje);

        if (item.Scoring?.FieldScore != null)
        {
            foreach (var campo in item.Scoring.FieldScore)
            {
                var valores = campo.Value.ValueKind == JsonValueKind.Array
                    ? campo.Value.EnumerateArray().ToArray()
                    : new[] { campo.Value };
                var puntajes = new List<decimal>();
                foreach (var valor in valores)
                {
                    if (valor.ValueKind != JsonValueKind.Number || !valor.TryGetDecimal(out var numero))
                        throw RespuestaInvalida();
                    ValidarPuntaje(numero);
                    puntajes.Add(numero);
                }
                resultado.PuntajesCampos.Add(campo.Key, puntajes);
            }
        }
        return resultado;
    }

    private static PuntoRutaDto ConvertirCoordenadas(CoordenadasHere? punto)
    {
        if (punto?.Lat == null || punto.Lng == null || punto.Lat < -90 || punto.Lat > 90 ||
            punto.Lng < -180 || punto.Lng > 180)
            throw RespuestaInvalida();
        return new PuntoRutaDto { Latitud = punto.Lat.Value, Longitud = punto.Lng.Value };
    }

    private static void ValidarPuntaje(decimal puntaje)
    {
        if (puntaje < 0 || puntaje > 1)
            throw RespuestaInvalida();
    }

    private static InvalidOperationException RespuestaInvalida() =>
        new("HERE Geocoding devolvió una respuesta inválida o incompleta.");

    // Estos modelos JSON no se exponen a la lógica de negocio.
    private sealed class RespuestaHere
    {
        public List<ItemHere?>? Items { get; set; }
    }

    private sealed class ItemHere
    {
        public string? Id { get; set; }
        public string? ResultType { get; set; }
        public string? HouseNumberType { get; set; }
        public bool HouseNumberFallback { get; set; }
        public bool EstimatedPointAddress { get; set; }
        public DireccionHere? Address { get; set; }
        public CoordenadasHere? Position { get; set; }
        public List<CoordenadasHere?>? Access { get; set; }
        public CalidadHere? Scoring { get; set; }
    }

    private sealed class DireccionHere
    {
        public string? Label { get; set; }
    }

    private sealed class CoordenadasHere
    {
        public decimal? Lat { get; set; }
        public decimal? Lng { get; set; }
    }

    private sealed class CalidadHere
    {
        public decimal? QueryScore { get; set; }
        public Dictionary<string, JsonElement>? FieldScore { get; set; }
    }
}
