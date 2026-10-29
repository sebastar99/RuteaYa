using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services
{
    public class ViajesApiServicio : IViajesApiServicio
    {
        private readonly HttpClient _httpClient;

        public ViajesApiServicio(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(bool Exito, string Mensaje, List<ViajeViewModel> Viajes)> ListarViajesAsync(
            int usuarioId
        ) {
            if (usuarioId <= 0)
            {
                return (false, "El usuario no es valido.", new List<ViajeViewModel>());
            }

            try {
                var respuesta = await _httpClient.GetAsync(
                    $"api/viajes?usuarioId={usuarioId}&estado=todos"
                );

                if (!respuesta.IsSuccessStatusCode)
                {
                    return (
                        false,
                        await ObtenerMensajeError(respuesta),
                        new List<ViajeViewModel>()
                    );
                }

                var viajes = await respuesta
                    .Content
                    .ReadFromJsonAsync<List<ViajeViewModel>>()
                    ?? new List<ViajeViewModel>();

                return (true, string.Empty, viajes);
            }
            catch (HttpRequestException)
            {
                return (false, "No se pudo conectar con la API.", new List<ViajeViewModel>());
            }
            catch (TaskCanceledException)
            {
                return (false, "La API tardo en responder.", new List<ViajeViewModel>());
            }
            catch (JsonException)
            {
                return (
                    false,
                    "La API devolvio una respuesta con formato invalido.",
                    new List<ViajeViewModel>()
                );
            }
        }

        public async Task<(bool Exito, string Mensaje)> CrearViajeAsync(
            int usuarioId, 
            ViajeViewModel modelo
        ) {
            if (usuarioId <= 0) {
                return (false, "El usuario no es valido.");
            }
            if (modelo == null) {
                return (false, "Los datos del viaje son obligatorios.");
            }

            if (string.IsNullOrWhiteSpace(modelo.Origen) ||
                string.IsNullOrWhiteSpace(modelo.Destino))
            {
                return (false, "El origen y el destino son obligatorios.");
            }
            if (string.Equals(modelo.Origen.Trim(), modelo.Destino.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (false, "El origen y el destino deben ser diferentes.");
            }

            var datos = CrearDatosViaje(modelo);

            try {
                var respuesta = await _httpClient.PostAsJsonAsync(
                    $"api/viajes?usuarioId={usuarioId}",
                    datos
                );

                if (respuesta.IsSuccessStatusCode)
                {
                    return (true, "El viaje se creo correctamente.");
                }

                return (false, await ObtenerMensajeError(respuesta));
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
                return (false, "No se pudo interpretar la respuesta de la API.");
            }
        }

        

        public async Task<(bool Exito, string Mensaje, ViajeViewModel? Viaje)> ObtenerViajePorIdAsync(
            int usuarioId, 
            int viajeId
        ) {
            if (usuarioId <= 0 || viajeId <= 0)
            {
                return (false, "El usuario o el viaje no son validos.", null);
            }

            try {
                var respuesta = await _httpClient.GetAsync(
                    $"api/viajes/{viajeId}?usuarioId={usuarioId}"
                );

                if (!respuesta.IsSuccessStatusCode)
                {
                    return (false, await ObtenerMensajeError(respuesta), null);
                }

                var viaje = await respuesta.Content.ReadFromJsonAsync<ViajeViewModel>();

                if (viaje is null)
                {
                    return (false, "La API no devolvio los datos del viaje.", null);
                }

                return (true, string.Empty, viaje);
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
                return (
                    false,
                    "La API devolvio una respuesta que no se pudo interpretar.",
                    null
                );
            }
        }

    
        public async Task<(bool Exito, string Mensaje)> EditarViajeAsync(
            int usuarioId, 
            int viajeId,
            ViajeViewModel modelo
        ) {
            if (usuarioId <= 0 || viajeId <= 0)
            {
                return (false, "El usuario o el viaje no son validos.");
            }
            if(modelo == null)
            {
                return(false, "Los datos del viaje son obligatorios.");
            }

            try {
                var respuesta = await _httpClient.PutAsJsonAsync(
                    $"api/viajes/{viajeId}?usuarioId={usuarioId}",
                    CrearDatosViaje(modelo)
                );

                if (respuesta.IsSuccessStatusCode)
                {
                    return (true, "El viajes se modifico.");
                }
                return (false, await ObtenerMensajeError(respuesta));
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
                return (false, "La API devolvio una respuesta que no se pudo interpretar.");
            }

        }

        public async Task<(bool Exito, string Mensaje)> DesactivarViajeAsync(int usuarioId, int viajeId)
        {
            if (usuarioId <= 0 || viajeId <= 0)
            {
                return (false, "El usuario o el viaje no son validos.");
            }

            try {
                using var solicitud = new HttpRequestMessage(
                    HttpMethod.Patch,
                    $"api/viajes/{viajeId}/desactivar?usuarioId={usuarioId}"
                );

                var respuesta = await _httpClient.SendAsync(solicitud);

                if (respuesta.IsSuccessStatusCode)
                {
                    return(true, "El viaje se desactivo correctamente");
                }   
                return (false, await ObtenerMensajeError(respuesta));
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
            {
                return (false, "El usuario o el viaje no son validos.");
            }

            if (string.IsNullOrWhiteSpace(ubicacion))
            {
                return (false, "La ubicacion de la parada es obligatoria.");
            }

            try {
                var datos = new {ubicacion = ubicacion.Trim(), orden};

                var respuesta = await _httpClient.PostAsJsonAsync(
                    $"api/viajes/{viajeId}/paradas?usuarioId={usuarioId}", 
                    datos
                );

                if (respuesta.IsSuccessStatusCode)
                {
                    return (true, "La parada se agrego.");
                }
                return (false, await ObtenerMensajeError(respuesta));
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
            {
                return (false, "El usuario, la parada o el viaje no son validos.");
            }

            try {
                var respuesta = await _httpClient.DeleteAsync(
                    $"api/viajes/{viajeId}/paradas/{paradaId}?usuarioId={usuarioId}"
                );

                if (respuesta.IsSuccessStatusCode)
                {
                    return (true, "La parada se elimino.");
                }
                return (false, await ObtenerMensajeError(respuesta));
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
            {
                return (false, "El usuario o el viaje no son validos.");
            }
            if (paradasId == null)
            {
                return (false, "La lista de paradas es obligatoria.");
            }

            try {
                var datos = new {paradasId};

                var respuesta = await _httpClient.PutAsJsonAsync(
                    $"api/viajes/{viajeId}/paradas/orden?usuarioId={usuarioId}",
                    datos
                );

                if (respuesta.IsSuccessStatusCode)
                {
                    return (true, "Las paradas se ordenaron correctamente.");
                }
                return (false, await ObtenerMensajeError(respuesta));
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


    // =============================================================================================
        private static async Task<string> ObtenerMensajeError(HttpResponseMessage respuesta)
        {
            var contenido = await respuesta.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(contenido))
            {
                return $"La API respondio con el código {(int)respuesta.StatusCode}.";
            }

            try {
                using var documento = JsonDocument.Parse(contenido);
                var raiz = documento.RootElement;

                if (raiz.ValueKind == JsonValueKind.Object)
                {
                    if (raiz.TryGetProperty("mensaje", out var mensaje))
                    {
                        return mensaje.ValueKind == JsonValueKind.String
                            ? mensaje.GetString() ?? "No se pudo completar la accion."
                            : "No se pudo completar la accion.";
                    }

                    if (raiz.TryGetProperty("title", out var titulo))
                    {
                        return titulo.ValueKind == JsonValueKind.String
                            ? titulo.GetString() ?? "No se pudo completar la accion."
                            : "No se pudo completar la accion.";
                    }
                }
            }
            catch (JsonException)
            {
                return "No se pudo interpretar la respuesta de la API.";
            }

            return $"La API respondió con el código {(int)respuesta.StatusCode}.";
        }

        private static object CrearDatosViaje(ViajeViewModel modelo)
        {
            var paradas = (modelo.ParadasTexto ?? string.Empty)
                .Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            return new
            {
                origen = modelo.Origen,
                destino = modelo.Destino,

                diasSemana = modelo.DiasSemana,
                horaSalida = modelo.HoraSalida.ToString(@"hh\:mm\:ss"),
                horaLlegada = modelo.HoraLlegada.ToString(@"hh\:mm\:ss"),

                cantMaxPaquetes = modelo.CantMaxPaquetes,
                pesoMax = modelo.PesoMax,
                desvioMaxKm = modelo.DesvioMaxKm,

                paradas
            };
        }

    }
}
