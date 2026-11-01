using System.Net.Http.Json;
using System.Text.Json;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services;

public class ViajesApiServicio : IViajesApiServicio
{
    private readonly HttpClient _httpClient;

    public ViajesApiServicio(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool Exito, string Mensaje, List<ViajeViewModel> Viajes)> ListarViajesAsync(int usuarioId)
    {
        if (usuarioId <= 0)
            return (false, "El usuario no es valido.", new());

        try {
            var response = await _httpClient.GetAsync("api/viajes?estado=todos");

            if (!response.IsSuccessStatusCode)
                return (false, await ObtenerMensajeError(response), new());

            var viajes = await response.Content
                .ReadFromJsonAsync<List<ViajeViewModel>>() ?? new();

            return (true, "", viajes);
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.", new());
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.", new());
        }
        catch (JsonException)
        {
            return (false, "La respuesta de la API no es valida.", new());
        }
    }

    public async Task<(bool Exito, string Mensaje)> CrearViajeAsync(int usuarioId, ViajeViewModel modelo)
    {
        if (usuarioId <= 0)
            return (false, "El usuario no es valido.");

        if (modelo == null)
            return (false, "Los datos del viaje son obligatorios.");

        if (string.IsNullOrWhiteSpace(modelo.Origen) ||
            string.IsNullOrWhiteSpace(modelo.Destino))
            return (false, "El origen y destino son obligatorios.");

        if (modelo.Origen.Trim().Equals(
            modelo.Destino.Trim(),
            StringComparison.OrdinalIgnoreCase))
            return (false, "El origen y destino deben ser diferentes.");

        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/viajes", CrearDatosViaje(modelo));

            return response.IsSuccessStatusCode
                ? (true, "El viaje se creó correctamente.")
                : (false, await ObtenerMensajeError(response));
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.");
        }
        catch (JsonException)
        {
            return (false, "No se pudo interpretar la respuesta.");
        }
    }

    public async Task<(bool Exito, string Mensaje, ViajeViewModel? Viaje)> ObtenerViajePorIdAsync(int usuarioId, int viajeId)
    {
        if (usuarioId <= 0 || viajeId <= 0)
            return (false, "El usuario o viaje no son validos.", null);

        try {
            var response = await _httpClient.GetAsync($"api/viajes/{viajeId}");

            if (!response.IsSuccessStatusCode)
                return (false, await ObtenerMensajeError(response), null);

            var viaje = await response.Content
                .ReadFromJsonAsync<ViajeViewModel>();

            return viaje == null
                ? (false, "No se recibieron los datos del viaje.", null)
                : (true, "", viaje);
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.", null);
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.", null);
        }
        catch (JsonException)
        {
            return (false, "La respuesta de la API no es valida.", null);
        }
    }

    public async Task<(bool Exito, string Mensaje)> EditarViajeAsync(
        int usuarioId,
        int viajeId,
        ViajeViewModel modelo
    ) {
        if (usuarioId <= 0 || viajeId <= 0)
            return (false, "El usuario o viaje no son validos.");

        if (modelo == null)
            return (false, "Los datos del viaje son obligatorios.");

        try {
            var response = await _httpClient.PutAsJsonAsync(
                $"api/viajes/{viajeId}", 
                CrearDatosViaje(modelo)
            );

            return response.IsSuccessStatusCode
                ? (true, "El viaje se modificó correctamente.")
                : (false, await ObtenerMensajeError(response));
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.");
        }
    }

    public async Task<(bool Exito, string Mensaje)> DesactivarViajeAsync(int usuarioId, int viajeId)
    {
        if (usuarioId <= 0 || viajeId <= 0)
            return (false, "El usuario o viaje no son validos.");

        try
        {
            var response = await _httpClient.PatchAsync($"api/viajes/{viajeId}/desactivar", null);

            return response.IsSuccessStatusCode
                ? (true, "El viaje se desactivó correctamente.")
                : (false, await ObtenerMensajeError(response));
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.");
        }
    }

    public async Task<(bool Exito, string Mensaje)> AgregarParadaAsync(
        int usuarioId,
        int viajeId,
        string ubicacion,
        int orden
    ) {
        if (usuarioId <= 0 || viajeId <= 0)
            return (false, "El usuario o viaje no son validos.");

        if (string.IsNullOrWhiteSpace(ubicacion))
            return (false, "La ubicación es obligatoria.");

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"api/viajes/{viajeId}/paradas",
                new { ubicacion = ubicacion.Trim(), orden }
            );

            return response.IsSuccessStatusCode
                ? (true, "La parada se agrego.")
                : (false, await ObtenerMensajeError(response));
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.");
        }
    }

    public async Task<(bool Exito, string Mensaje)> EliminarParadaAsync(
        int usuarioId,
        int viajeId,
        int paradaId
    ) {
        if (usuarioId <= 0 || viajeId <= 0 || paradaId <= 0)
            return (false, "Los identificadores no son validos.");

        try {
            var response = await _httpClient.DeleteAsync($"api/viajes/{viajeId}/paradas/{paradaId}");

            return response.IsSuccessStatusCode
                ? (true, "La parada se elimino.")
                : (false, await ObtenerMensajeError(response));
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.");
        }
    }

    public async Task<(bool Exito, string Mensaje)> ReordenarParadasAsync(
        int usuarioId,
        int viajeId,
        List<int> paradasId
    ) {
        if (usuarioId <= 0 || viajeId <= 0)
            return (false, "Los identificadores no son validos.");

        if (paradasId == null)
            return (false, "La lista de paradas es obligatoria.");

        try {
            var response = await _httpClient.PutAsJsonAsync(
                $"api/viajes/{viajeId}/paradas/orden",
                new { paradasIds = paradasId }
            );

            return response.IsSuccessStatusCode
                ? (true, "Las paradas se ordenaron correctamente.")
                : (false, await ObtenerMensajeError(response));
        }
        catch (HttpRequestException)
        {
            return (false, "No se pudo conectar con la API.");
        }
        catch (TaskCanceledException)
        {
            return (false, "La API tardo en responder.");
        }
    }

    private static object CrearDatosViaje(ViajeViewModel modelo)
    {
        var paradas = (modelo.ParadasTexto ?? string.Empty)
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .ToList();

        return new
        {
            origen = modelo.Origen.Trim(),
            destino = modelo.Destino.Trim(),

            diasSemana = modelo.DiasSemana,
            horaSalida = modelo.HoraSalida.ToString(@"hh\:mm\:ss"),
            horaLlegada = modelo.HoraLlegada.ToString(@"hh\:mm\:ss"),

            cantMaxPaquetes = modelo.CantMaxPaquetes,
            pesoMax = modelo.PesoMax,
            desvioMaxKm = modelo.DesvioMaxKm,

            paradas
        };
    }

    private static async Task<string> ObtenerMensajeError(HttpResponseMessage response)
    {
        var contenido = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(contenido))
            return $"Error de API: {(int)response.StatusCode}.";

        try
        {
            using var documento = JsonDocument.Parse(contenido);
            var raiz = documento.RootElement;

            if (raiz.TryGetProperty("mensaje", out var mensaje))
                return mensaje.GetString() ?? "La solicitud no pudo completarse.";

            if (raiz.TryGetProperty("title", out var titulo))
                return titulo.GetString() ?? "La solicitud no pudo completarse.";
        }
        catch (JsonException)
        {
            return "La API devolvió una respuesta invalida.";
        }

        return $"Error de API: {(int)response.StatusCode}.";
    }
}
