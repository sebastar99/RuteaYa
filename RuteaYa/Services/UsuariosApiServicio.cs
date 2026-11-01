
using System.Net.Http.Json;
using System.Text.Json;
using RuteaYa.Web.Models;

namespace RuteaYa.Web.Services
{
    public class UsuariosApiServicio : IUsuariosApiServicio
    {
        private readonly HttpClient _httpClient;

        public UsuariosApiServicio(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(bool Exito, string Mensaje)> RegistrarAsync(
            RegistroUsuarioViewModel modelo)
        {
            var datos = new
            {
                nombre = modelo.Nombre,
                apellido = modelo.Apellido,
                email = modelo.Email,
                password = modelo.Password
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    "api/usuarios/registro", datos);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Registro realizado correctamente.");
                }

                var mensaje = await ObtenerMensajeError(response);

                return (false, mensaje);
            }
            catch (HttpRequestException)
            {
                return (false, "No se pudo conectar con el servidor.");
            }
            catch (TaskCanceledException)
            {
                return (false, "La solicitud tardó demasiado tiempo.");
            }
        }


        public async Task<(
            bool Exito,
            string Mensaje,
            UsuarioApiRespuesta? Usuario,
            string? Token)> LoginAsync(
                LoginUsuarioViewModel modelo)
        {
            var datos = new
            {
                email = modelo.Email,
                password = modelo.Password
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    "api/usuarios/login", datos);

                if (!response.IsSuccessStatusCode)
                {
                    var mensaje = await ObtenerMensajeError(response);
                    return (false, mensaje, null, null);
                }

                var resultado = await response.Content
                    .ReadFromJsonAsync<LoginApiRespuesta>();

                if (resultado == null ||
                    string.IsNullOrWhiteSpace(resultado.Token) ||
                    resultado.Usuario == null)
                {
                    return (
                        false,
                        "La API devolvió una respuesta de login inválida.",
                        null,
                        null);
                }

                return (
                    true,
                    "Inicio de sesión correcto.",
                    resultado.Usuario,
                    resultado.Token);
            }
            catch (HttpRequestException)
            {
                return (
                    false,
                    "No se pudo conectar con el servidor.",
                    null,
                    null);
            }
            catch (TaskCanceledException)
            {
                return (
                    false,
                    "La solicitud tardó demasiado tiempo.",
                    null,
                    null);
            }
        }


        private static async Task<string> ObtenerMensajeError(
            HttpResponseMessage response)
        {
            var contenido = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(contenido))
            {
                return $"Error del servidor: {(int)response.StatusCode}.";
            }

            try
            {
                using var documento = JsonDocument.Parse(contenido);

                if (documento.RootElement.TryGetProperty(
                    "mensaje", out var mensaje))
                {
                    return mensaje.GetString()
                        ?? "La solicitud no pudo completarse.";
                }

                if (documento.RootElement.TryGetProperty(
                    "title", out var titulo))
                {
                    return titulo.GetString()
                        ?? "La solicitud no pudo completarse.";
                }
            }
            catch (JsonException)
            {
                // La respuesta no contiene JSON válido.
            }

            return "La solicitud no pudo completarse.";
        }
    }
}
